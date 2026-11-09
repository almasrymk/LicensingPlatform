using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Licensing.Application.Integrations;
using Licensing.Infrastructure.Persistence;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Licensing.Infrastructure.Messaging;

public sealed class ChangeFeedOptions
{
    public const string Section = "Integration:ChangeFeed";

    /// <summary>
    /// Entries younger than this are not returned yet. An entry's time is taken before its transaction commits, so a
    /// slow commit could otherwise appear behind a cursor that already passed it. The full reconcile covers the rest.
    /// </summary>
    public int SettleSeconds { get; set; } = 5;

    public int MaxTake { get; set; } = 500;

    /// <summary>
    /// R11: days the outbox keeps entries for the feed (0 keeps everything). The daily maintenance job deletes processed
    /// entries older than this, and a cursor older than this is <c>INTEGRATION_CURSOR_EXPIRED</c>, so a consumer that
    /// was away longer reconciles in full instead of silently missing the purged entries.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// The outbox as a change feed (INT-006). The cursor holds the last (occurredAt) returned and the ids already
/// returned at that instant, so ties are handled the same on SQL Server and SQLite without comparing Guids.
/// </summary>
public sealed class ChangeFeedReader(AppDbContext db, IOptions<ChangeFeedOptions> options) : IChangeFeed
{
    // Event timestamps come from DateTimeOffset.UtcNow (DomainEvent), so the settle window uses the same clock.
    private static readonly TimeProvider WallClock = TimeProvider.System;

    public async Task<Result<ChangeFeedPage>> ReadAsync(Guid tenantId, string? cursor, int take, CancellationToken ct)
    {
        var o = options.Value;
        take = Math.Clamp(take, 1, Math.Max(1, o.MaxTake));
        Cursor? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            after = Cursor.TryParse(cursor);
            if (after is null) return ChangeFeedErrors.CursorInvalid;
            if (o.RetentionDays > 0 && after.At < WallClock.GetUtcNow().AddDays(-o.RetentionDays))
                return ChangeFeedErrors.CursorExpired;
        }

        var upTo = WallClock.GetUtcNow().AddSeconds(-Math.Max(0, o.SettleSeconds));
        var query = db.OutboxMessages.AsNoTracking().Where(m => m.TenantId == tenantId && m.OccurredAt <= upTo);
        if (after is not null)
        {
            var at = after.At;
            var seen = after.Ids;
            query = query.Where(m => m.OccurredAt > at || (m.OccurredAt == at && !seen.Contains(m.Id)));
        }

        var rows = await query.OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(take + 1)
            .Select(m => new { m.Id, m.Type, m.OccurredAt, m.Payload })
            .ToListAsync(ct);
        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var parsed = rows.Select(r => (r.Id, r.Type, r.OccurredAt, Ids: Ids.From(r.Payload))).ToList();
        var customers = await ResolveCustomersAsync(parsed.Select(p => p.Ids).ToList(), ct);
        var items = parsed.Select(p => new ChangeFeedItem(p.Id, p.Type, p.OccurredAt,
            p.Ids.Customer ?? Lookup(customers.BySubscription, p.Ids.Subscription) ?? Lookup(customers.ByLicense, p.Ids.License),
            p.Ids.Subscription ?? Lookup(customers.SubscriptionOfLicense, p.Ids.License), p.Ids.License, p.Ids.Device)).ToList();

        string? next;
        if (rows.Count == 0)
            // Nothing up to the settle horizon: a cursor moves to it, so a quiet tenant's cursor never grows old enough
            // to expire (R11). Without a cursor there is still nothing to resume from.
            next = after is not null && upTo > after.At ? new Cursor(upTo, []).Encode() : cursor;
        else
        {
            var last = rows[^1].OccurredAt;
            var ids = rows.Where(r => r.OccurredAt == last).Select(r => r.Id);
            if (after is not null && after.At == last)
                ids = ids.Concat(after.Ids);
            next = new Cursor(last, ids.Distinct().ToList()).Encode();
        }

        return new ChangeFeedPage(items, next, hasMore);
    }

    private static Guid? Lookup(Dictionary<Guid, Guid> map, Guid? key) => key is { } k && map.TryGetValue(k, out var v) ? v : null;

    private sealed record Resolved(Dictionary<Guid, Guid> BySubscription, Dictionary<Guid, Guid> ByLicense, Dictionary<Guid, Guid> SubscriptionOfLicense);

    /// <summary>Most subscription and license events carry no customer id; Monitor Cloud needs it to find the tenant.</summary>
    private async Task<Resolved> ResolveCustomersAsync(IReadOnlyList<Ids> all, CancellationToken ct)
    {
        var subscriptionIds = all.Where(i => i.Customer is null && i.Subscription is not null).Select(i => i.Subscription!.Value).Distinct().ToList();
        var licenseIds = all.Where(i => i.License is not null && (i.Customer is null || i.Subscription is null)).Select(i => i.License!.Value).Distinct().ToList();

        var bySubscription = subscriptionIds.Count == 0 ? [] :
            await db.Subscriptions.AsNoTracking().Where(s => subscriptionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.CustomerId, ct);
        var licenses = licenseIds.Count == 0 ? [] :
            await db.Licenses.AsNoTracking().Where(l => licenseIds.Contains(l.Id)).Select(l => new { l.Id, l.CustomerId, l.SubscriptionId }).ToListAsync(ct);
        return new Resolved(bySubscription, licenses.ToDictionary(l => l.Id, l => l.CustomerId), licenses.ToDictionary(l => l.Id, l => l.SubscriptionId));
    }

    private sealed record Ids(Guid? Customer, Guid? Subscription, Guid? License, string? Device = null)
    {
        public static Ids From(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                return new Ids(Read(doc.RootElement, "CustomerId"), Read(doc.RootElement, "SubscriptionId"), Read(doc.RootElement, "LicenseId"),
                    doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("DeviceId", out var device) && device.ValueKind == JsonValueKind.String
                        ? device.GetString() : null);
            }
            catch (JsonException)
            {
                return new Ids(null, null, null);
            }
        }

        private static Guid? Read(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.TryGetGuid(out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>"v1.{utcTicks}.{id,id,...}" in base64url. Opaque to callers.</summary>
    private sealed record Cursor(DateTimeOffset At, List<Guid> Ids)
    {
        private const int MaxIds = 5000;

        public string Encode() =>
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"v1.{At.UtcTicks}.{string.Join(',', Ids.Select(i => i.ToString("N")))}"));

        public static Cursor? TryParse(string value)
        {
            try
            {
                if (value.Length > 200_000) return null;
                var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)).Split('.');
                if (parts.Length != 3 || parts[0] != "v1" || !long.TryParse(parts[1], out var ticks) || ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks)
                    return null;
                var ids = new List<Guid>();
                foreach (var part in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Guid.TryParseExact(part, "N", out var id) || ids.Count >= MaxIds) return null;
                    ids.Add(id);
                }
                return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), ids);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
