using System.Net;
using System.Text.Json;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

public sealed class ChangeFeedFixture : IDisposable
{
    /// <summary>No settle window, so entries written by a test are readable at once.</summary>
    public TestApp App { get; } = new(new Dictionary<string, string?> { ["Integration:ChangeFeed:SettleSeconds"] = "0" });
    public void Dispose() => App.Dispose();
}

/// <summary>INT-006: GET /api/v1/integration/changes, the outbox as an incremental feed for Monitor Cloud (ADR-002).</summary>
public sealed class ChangeFeedTests : IClassFixture<ChangeFeedFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public ChangeFeedTests(ChangeFeedFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Cloud() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private static async Task<(List<JsonElement> Items, string? Cursor)> ReadAllAsync(HttpClient client, string? cursor, int take)
    {
        var items = new List<JsonElement>();
        for (var pages = 0; pages < 1000; pages++)
        {
            var query = cursor is null ? $"take={take}" : $"take={take}&cursor={Uri.EscapeDataString(cursor)}";
            var page = await (await client.GetAsync($"/api/v1/integration/changes?{query}")).OkJsonAsync();
            items.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.Clone()));
            cursor = page.GetProperty("nextCursor").GetString();
            if (!page.GetProperty("hasMore").GetBoolean())
                return (items, cursor);
        }
        throw new InvalidOperationException("The feed never ended.");
    }

    [Fact]
    public async Task Paging_returns_every_entry_once_in_order_with_the_customer_resolved()
    {
        var issued = await _setup.IssueAsync();
        var cloud = await Cloud();
        await (await cloud.PostJsonAsync($"/api/v1/integration/subscriptions/{issued.SubscriptionId}/suspend", new { reason = "feed test" })).OkJsonAsync();
        var customerId = await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).Select(s => s.CustomerId).FirstAsync());

        var (items, _) = await ReadAllAsync(cloud, null, take: 3);

        var ids = items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var times = items.Select(i => i.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
        var suspended = Assert.Single(items, i => i.GetProperty("type").GetString() == "SubscriptionSuspendedV1" && i.GetProperty("subscriptionId").GetGuid() == issued.SubscriptionId);
        Assert.Equal(customerId, suspended.GetProperty("customerId").GetGuid());
        var license = Assert.Single(items, i => i.GetProperty("type").GetString() == "LicenseIssuedV1" && i.GetProperty("licenseId").GetGuid() == issued.LicenseId);
        Assert.Equal(issued.SubscriptionId, license.GetProperty("subscriptionId").GetGuid());
        Assert.Equal(customerId, license.GetProperty("customerId").GetGuid());
    }

    [Fact]
    public async Task A_saved_cursor_returns_only_newer_entries()
    {
        var cloud = await Cloud();
        var (_, cursor) = await ReadAllAsync(cloud, null, take: 200);
        Assert.NotNull(cursor);

        var issued = await _setup.IssueAsync();
        var (fresh, after) = await ReadAllAsync(cloud, cursor, take: 200);
        var (none, _) = await ReadAllAsync(cloud, after, take: 200);

        Assert.Contains(fresh, i => i.GetProperty("type").GetString() == "LicenseIssuedV1" && i.GetProperty("licenseId").GetGuid() == issued.LicenseId);
        Assert.Empty(none);
    }

    [Fact]
    public async Task Only_the_callers_tenant_is_returned_and_payloads_stay_inside()
    {
        await _setup.IssueAsync();
        var cloud = await Cloud();
        var raw = await (await cloud.GetAsync("/api/v1/integration/changes?take=500")).Content.ReadAsStringAsync();
        var nourTenant = await _app.WithDbAsync(db => db.ApiClients.IgnoreQueryFilters().Where(c => c.ClientId == DemoAccounts.NourClientId).Select(c => c.TenantId).FirstAsync());
        var foreign = await _app.WithDbAsync(db => db.OutboxMessages.Where(m => m.TenantId != nourTenant && m.TenantId != null).Select(m => m.Id).ToListAsync());

        Assert.DoesNotContain("productKeyHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", raw, StringComparison.OrdinalIgnoreCase);
        var returned = JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        Assert.NotEmpty(returned);
        Assert.DoesNotContain(foreign, returned.Contains);
    }

    [Fact]
    public async Task A_client_without_subscriptions_read_or_a_user_is_refused()
    {
        var limited = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);
        var user = await _app.LoginAsync(DemoAccounts.NourAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync("/api/v1/integration/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/integration/changes")).StatusCode);
    }

    private static string CursorAt(DateTimeOffset at) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes($"v1.{at.UtcTicks}.{Guid.NewGuid():N}"));

    [Fact]
    public async Task A_cursor_older_than_the_retention_is_INTEGRATION_CURSOR_EXPIRED()
    {
        // R11: entries after it may be purged, so the consumer must reconcile instead of resuming.
        var cloud = await Cloud();

        var expired = await cloud.GetAsync($"/api/v1/integration/changes?cursor={CursorAt(DateTimeOffset.UtcNow.AddDays(-31))}");
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal("INTEGRATION_CURSOR_EXPIRED", await expired.ErrorCodeAsync());

        (await cloud.GetAsync($"/api/v1/integration/changes?cursor={CursorAt(DateTimeOffset.UtcNow.AddDays(-29))}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_cursor_with_nothing_new_moves_forward_so_a_quiet_tenant_never_expires()
    {
        var cloud = await Cloud();
        var (_, cursor) = await ReadAllAsync(cloud, null, take: 500);
        // A quiet month later the saved cursor would be old; the empty page already moved it to the read horizon.
        var old = CursorAt(DateTimeOffset.UtcNow.AddDays(-20));

        var page = await (await cloud.GetAsync($"/api/v1/integration/changes?cursor={Uri.EscapeDataString(cursor!)}")).OkJsonAsync();
        var fromOld = await (await cloud.GetAsync($"/api/v1/integration/changes?take=500&cursor={old}")).OkJsonAsync();

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.NotNull(page.GetProperty("nextCursor").GetString());
        Assert.NotEqual(old, fromOld.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task The_purge_removes_only_old_entries_that_were_dispatched()
    {
        var tenantId = await _app.WithDbAsync(db => db.OutboxMessages.Where(m => m.TenantId != null).Select(m => m.TenantId).FirstAsync());
        var old = DateTimeOffset.UtcNow.AddDays(-45);
        var ids = new { Processed = Guid.NewGuid(), Pending = Guid.NewGuid(), Dead = Guid.NewGuid(), Recent = Guid.NewGuid() };
        await _app.WithDbAsync(async db =>
        {
            db.OutboxMessages.AddRange(
                new OutboxMessage { Id = ids.Processed, Type = "PurgeTestV1", Payload = "{}", TenantId = tenantId, OccurredAt = old, ProcessedAt = old },
                new OutboxMessage { Id = ids.Pending, Type = "PurgeTestV1", Payload = "{}", TenantId = tenantId, OccurredAt = old },
                new OutboxMessage { Id = ids.Dead, Type = "PurgeTestV1", Payload = "{}", TenantId = tenantId, OccurredAt = old, DeadLettered = true },
                new OutboxMessage { Id = ids.Recent, Type = "PurgeTestV1", Payload = "{}", TenantId = tenantId, OccurredAt = DateTimeOffset.UtcNow.AddDays(-1), ProcessedAt = DateTimeOffset.UtcNow });
            db.InboxMessages.Add(new InboxMessage { MessageId = ids.Processed, Handler = "purge-test", ProcessedAt = old });
            return await db.SaveChangesAsync();
        });

        var removed = await _app.Get<Licensing.Infrastructure.Jobs.JobRunner>().PurgeOutboxAsync(default);

        Assert.True(removed >= 2);
        var left = await _app.WithDbAsync(db => db.OutboxMessages.Where(m => m.Type == "PurgeTestV1").Select(m => m.Id).ToListAsync());
        Assert.Equal(new[] { ids.Pending, ids.Recent }.OrderBy(i => i), left.OrderBy(i => i));
        Assert.False(await _app.WithDbAsync(db => db.InboxMessages.AnyAsync(m => m.Handler == "purge-test")));
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("djEuYWJjLnh5eg")]
    public async Task An_invalid_cursor_is_INTEGRATION_CURSOR_INVALID(string cursor)
    {
        var cloud = await Cloud();

        var response = await cloud.GetAsync($"/api/v1/integration/changes?cursor={cursor}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INTEGRATION_CURSOR_INVALID", await response.ErrorCodeAsync());
    }
}

/// <summary>With the default settle window an entry written a moment ago is not returned yet.</summary>
public sealed class ChangeFeedSettleTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public ChangeFeedSettleTests(TestAppFixture fixture) => _app = fixture.App;

    [Fact]
    public async Task Entries_younger_than_the_settle_window_wait()
    {
        var issued = await new LicenseSetup(_app).IssueAsync();
        var cloud = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

        var page = await (await cloud.GetAsync("/api/v1/integration/changes?take=500")).OkJsonAsync();

        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(),
            i => i.TryGetProperty("licenseId", out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == issued.LicenseId);
    }
}
