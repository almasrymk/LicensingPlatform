using System.Net;
using System.Text.Json;
using Licensing.Domain.Licensing;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>
/// E4: a plan change reaches every license that is not revoked (entitlements, features, expiry) and the change feed,
/// and a downgrade never deactivates devices.
/// </summary>
public sealed class PlanChangePropagationTests : IClassFixture<ChangeFeedFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public PlanChangePropagationTests(ChangeFeedFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private static async Task<Guid> PlanAsync(HttpClient admin, Guid productId, string code, int? maxActivations, int? durationDays, string[] features)
    {
        var plan = await (await admin.PostJsonAsync("/api/v1/plans", new
        {
            productId, code, name = "خطة " + code, price = 200, currency = "EGP", durationDays, maxActivations,
            heartbeatIntervalHours = 12, offlineGraceDays = 5, features,
        })).OkJsonAsync(HttpStatusCode.Created);
        var id = plan.GetProperty("id").GetGuid();
        await (await admin.PostAsync($"/api/v1/plans/{id}/publish", null)).OkJsonAsync();
        return id;
    }

    private Task<HttpClient> Cloud() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private static async Task ActivateAsync(HttpClient cloud, string key, string productCode, string device) =>
        await (await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = key, deviceId = device, productCode })).OkJsonAsync();

    private Task<License> LicenseAsync(Guid id) => _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().AsNoTracking().FirstAsync(l => l.Id == id));

    /// <summary>Reads the change feed from <paramref name="cursor"/> to its end: the last cursor and every item's type and license.</summary>
    private static async Task<(string? Cursor, List<(string? Type, Guid? License)> Items)> ReadFeedAsync(HttpClient cloud, string? cursor)
    {
        var items = new List<(string? Type, Guid? License)>();
        while (true)
        {
            var query = cursor is null ? "take=200" : $"cursor={Uri.EscapeDataString(cursor)}&take=200";
            var page = await (await cloud.GetAsync($"/api/v1/integration/changes?{query}")).OkJsonAsync();
            items.AddRange(page.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("type").GetString(),
                i.GetProperty("licenseId").ValueKind == JsonValueKind.Null ? (Guid?)null : i.GetProperty("licenseId").GetGuid())));
            cursor = page.GetProperty("nextCursor").GetString() ?? cursor;
            if (!page.GetProperty("hasMore").GetBoolean()) return (cursor, items);
        }
    }

    private static Task<HttpResponseMessage> ChangePlanAsync(HttpClient admin, Guid subscriptionId, Guid planId) =>
        admin.PostJsonAsync($"/api/v1/subscriptions/{subscriptionId}/change-plan", new { planId });

    [Fact]
    public async Task An_upgrade_gives_every_live_license_the_new_entitlements_and_reaches_the_change_feed()
    {
        var issued = await _setup.IssueAsync(maxActivations: 2);
        var revoked = await (await issued.Admin.PostJsonAsync("/api/v1/licenses", new { subscriptionId = issued.SubscriptionId })).OkJsonAsync(HttpStatusCode.Created);
        var revokedId = revoked.GetProperty("license").GetProperty("id").GetGuid();
        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{revokedId}/revoke", new { reason = "test" })).OkJsonAsync();
        var pro = await PlanAsync(issued.Admin, issued.ProductId, "PRO", 10, 365, ["reports", "export", "api"]);
        var cloud = await Cloud();
        var (cursor, _) = await ReadFeedAsync(cloud, null);

        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, pro)).OkJsonAsync();

        var live = await LicenseAsync(issued.LicenseId);
        Assert.Equal("PRO", live.PlanCode);
        Assert.Equal(pro, live.PlanId);
        Assert.Equal(10, live.MaxActivations);
        Assert.Contains("api", live.Features);
        Assert.Equal(12, live.HeartbeatIntervalHours);
        var untouched = await LicenseAsync(revokedId);
        Assert.Equal("STD", untouched.PlanCode);
        Assert.Equal(LicenseStatus.Revoked, untouched.Status);

        var (_, types) = await ReadFeedAsync(cloud, cursor);
        Assert.Contains(types, t => t.Type == "SubscriptionPlanChangedV1");
        Assert.Contains(types, t => t.Type == "LicenseEntitlementsChangedV1" && t.License == issued.LicenseId);
        Assert.DoesNotContain(types, t => t.Type == "LicenseEntitlementsChangedV1" && t.License == revokedId);

        // The validate answer (and so the token Monitor Cloud passes to the agent) carries the new plan at once.
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "PLAN-UP-DEVICE-01");
        var check = await (await cloud.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "PLAN-UP-DEVICE-01", productCode = issued.ProductCode })).OkJsonAsync();
        Assert.Equal("PRO", check.GetProperty("planCode").GetString());
        Assert.Contains("api", check.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task A_downgrade_below_the_devices_in_use_keeps_them_active_and_refuses_new_ones()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "PLAN-DOWN-01");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "PLAN-DOWN-02");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "PLAN-DOWN-03");
        var basic = await PlanAsync(issued.Admin, issued.ProductId, "BASIC", 1, 365, ["reports"]);

        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, basic)).OkJsonAsync();

        Assert.Equal(3, await _app.WithDbAsync(db => db.LicenseActivations.IgnoreQueryFilters()
            .CountAsync(a => a.LicenseId == issued.LicenseId && a.Status == ActivationStatus.Active)));
        var validated = await (await cloud.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "PLAN-DOWN-03", productCode = issued.ProductCode })).OkJsonAsync();
        Assert.Equal("BASIC", validated.GetProperty("planCode").GetString());
        var refused = await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "PLAN-DOWN-04", productCode = issued.ProductCode });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.True(await _app.WithDbAsync(db => db.AuditRecords.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "subscription.over_capacity" && a.EntityId == issued.SubscriptionId.ToString())));
    }

    [Fact]
    public async Task A_plan_without_an_end_date_lifts_the_license_expiry_and_a_dated_plan_keeps_it()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3, durationDays: 30);
        var before = (await LicenseAsync(issued.LicenseId)).ExpiresAt;
        Assert.NotNull(before);
        var dated = await PlanAsync(issued.Admin, issued.ProductId, "DATED", 3, 90, ["reports"]);
        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, dated)).OkJsonAsync();
        Assert.Equal(before, (await LicenseAsync(issued.LicenseId)).ExpiresAt);

        var lifetime = await PlanAsync(issued.Admin, issued.ProductId, "LIFETIME", 3, null, ["reports"]);
        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, lifetime)).OkJsonAsync();

        var after = await LicenseAsync(issued.LicenseId);
        Assert.Null(after.ExpiresAt);
        Assert.Equal(LicenseStatus.Active, after.Status);
    }
}
