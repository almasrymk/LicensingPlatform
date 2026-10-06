using System.Text;
using System.Text.Json;
using Licensing.Application.Integrations;
using Licensing.Infrastructure.Persistence;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Infrastructure.Messaging;

/// <summary>
/// Reads integration events from the outbox for one tenant, ordered by <c>(OccurredAt, Id)</c>. The cursor is opaque to
/// callers: the position of the last item returned.
/// </summary>
internal sealed class ChangeFeed(AppDbContext db) : IChangeFeed
{
    public static readonly Error InvalidCursor = Error.Validation("VALIDATION_FAILED", "The cursor is not valid.");

    public async Task<Result<ChangeFeedPage>> ReadAsync(Guid tenantId, string? cursor, int take, CancellationToken ct)
    {
        var query = db.OutboxMessages.AsNoTracking().Where(m => m.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!TryDecode(cursor, out var at, out var id))
                return InvalidCursor;
            query = query.Where(m => m.OccurredAt > at || (m.OccurredAt == at && m.Id.CompareTo(id) > 0));
        }

        var rows = await query
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(take + 1)
            .Select(m => new { m.Id, m.Type, m.OccurredAt, m.CustomerId, m.Payload })
            .ToListAsync(ct);

        var hasMore = rows.Count > take;
        var page = rows.Take(take).ToList();
        var items = page.Select(r => new ChangeItem(r.Id, r.Type, r.OccurredAt, r.CustomerId, ReadGuid(r.Payload, "SubscriptionId"), ReadGuid(r.Payload, "LicenseId"))).ToList();
        var next = page.Count == 0 ? cursor : Encode(page[^1].OccurredAt, page[^1].Id);
        return new ChangeFeedPage(items, next, hasMore);
    }

    internal static string Encode(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.UtcTicks}:{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static bool TryDecode(string cursor, out DateTimeOffset at, out Guid id)
    {
        at = default;
        id = default;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParseExact(parts[1], "N", out id))
                return false;
            at = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static Guid? ReadGuid(string payload, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty(property, out var value) && value.TryGetGuid(out var guid) ? guid : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
