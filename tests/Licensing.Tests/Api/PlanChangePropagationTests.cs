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

    private Task<DateTimeOffset?> OverCapacitySinceAsync(Guid subscriptionId) =>
        _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == subscriptionId).Select(s => s.OverCapacitySince).FirstAsync());

    [Fact]
    public async Task Over_capacity_is_marked_shown_and_cleared_without_touching_devices()
    {
        // B4: mark, refuse new activations with a clear code, publish to the change feed and show usage; never deactivate.
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERCAP-01");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERCAP-02");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERCAP-03");
        var basic = await PlanAsync(issued.Admin, issued.ProductId, "OVERBASIC", 2, 365, ["reports"]);
        var (cursor, _) = await ReadFeedAsync(cloud, null);

        var changed = await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, basic)).OkJsonAsync();

        Assert.NotNull(await OverCapacitySinceAsync(issued.SubscriptionId));
        var shown = changed.GetProperty("subscription");
        Assert.Equal(3, shown.GetProperty("activeDevices").GetInt32());
        Assert.Equal(2, shown.GetProperty("maxDevices").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, shown.GetProperty("overCapacitySince").ValueKind);
        var refused = await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "OVERCAP-04", productCode = issued.ProductCode });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("LIC_SUBSCRIPTION_OVER_CAPACITY", await refused.Content.ReadAsStringAsync());
        // A device already active keeps working.
        await (await cloud.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "OVERCAP-01", productCode = issued.ProductCode })).OkJsonAsync();
        Assert.Equal(3, await _app.WithDbAsync(db => db.LicenseActivations.IgnoreQueryFilters()
            .CountAsync(a => a.LicenseId == issued.LicenseId && a.Status == ActivationStatus.Active)));
        var (afterMark, marked) = await ReadFeedAsync(cloud, cursor);
        Assert.Contains(marked, t => t.Type == "SubscriptionOverCapacityV1");

        // One device released: 2 of 2 is within the limit again, so the flag clears; a new device still needs a free seat.
        Assert.Equal(HttpStatusCode.NoContent, (await cloud.PostJsonAsync($"/api/v1/integration/licenses/{issued.LicenseId}/devices/OVERCAP-03/release", new { })).StatusCode);
        Assert.Null(await OverCapacitySinceAsync(issued.SubscriptionId));
        var (_, restored) = await ReadFeedAsync(cloud, afterMark);
        Assert.Contains(restored, t => t.Type == "SubscriptionCapacityRestoredV1");
        var full = await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "OVERCAP-05", productCode = issued.ProductCode });
        Assert.Contains("LIC_ACTIVATION_LIMIT_REACHED", await full.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upgrading_an_over_capacity_subscription_clears_the_flag()
    {
        var issued = await _setup.IssueAsync(maxActivations: 2);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERUP-01");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERUP-02");
        var tiny = await PlanAsync(issued.Admin, issued.ProductId, "TINY", 1, 365, ["reports"]);
        var big = await PlanAsync(issued.Admin, issued.ProductId, "BIG", 5, 365, ["reports"]);
        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, tiny)).OkJsonAsync();
        Assert.NotNull(await OverCapacitySinceAsync(issued.SubscriptionId));

        await (await ChangePlanAsync(issued.Admin, issued.SubscriptionId, big)).OkJsonAsync();

        Assert.Null(await OverCapacitySinceAsync(issued.SubscriptionId));
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVERUP-03");
    }

    [Fact]
    public async Task The_reconciliation_marks_a_subscription_that_was_over_capacity_before_the_flag_existed()
    {
        var issued = await _setup.IssueAsync(maxActivations: 2);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVEROLD-01");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "OVEROLD-02");
        // As if the plan's limit had been lowered in place before B4: nothing recounted, no flag.
        var planId = await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).Select(s => s.PlanId).FirstAsync());
        await _app.WithDbAsync(db => db.Plans.IgnoreQueryFilters().Where(p => p.Id == planId).ExecuteUpdateAsync(p => p.SetProperty(x => x.MaxActivations, 1)));
        Assert.Null(await OverCapacitySinceAsync(issued.SubscriptionId));

        await _app.WithDbAsync(db => db.ReconcileDeviceCountersAsync(100, default));

        Assert.NotNull(await OverCapacitySinceAsync(issued.SubscriptionId));
    }

    [Fact]
    public async Task A_device_reset_or_a_new_key_reaches_the_change_feed_with_the_device()
    {
        // INT-024: Monitor Cloud marks the device unlicensed from the feed instead of waiting for its next seat check.
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "RESET-FEED-01");
        await ActivateAsync(cloud, issued.Key, issued.ProductCode, "RESET-FEED-02");
        var (cursor, _) = await ReadFeedAsync(cloud, null);
        var activationId = await _app.WithDbAsync(db => db.LicenseActivations.IgnoreQueryFilters()
            .Where(a => a.LicenseId == issued.LicenseId && a.DeviceId == "RESET-FEED-01").Select(a => a.Id).FirstAsync());

        await (await issued.Admin.PostAsync($"/api/v1/licenses/{issued.LicenseId}/activations/{activationId}/reset", null)).OkJsonAsync();
        await (await issued.Admin.PostAsync($"/api/v1/licenses/{issued.LicenseId}/regenerate-key", null)).OkJsonAsync();

        var devices = new List<string?>();
        var next = cursor;
        while (true)
        {
            var query = next is null ? "take=200" : $"cursor={Uri.EscapeDataString(next)}&take=200";
            var page = await (await cloud.GetAsync($"/api/v1/integration/changes?{query}")).OkJsonAsync();
            foreach (var item in page.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("type").GetString() == "LicenseActivationResetV1"))
            {
                Assert.Equal(issued.LicenseId, item.GetProperty("licenseId").GetGuid());
                Assert.Equal(issued.SubscriptionId, item.GetProperty("subscriptionId").GetGuid());
                Assert.NotEqual(JsonValueKind.Null, item.GetProperty("customerId").ValueKind);
                devices.Add(item.GetProperty("deviceId").GetString());
            }
            next = page.GetProperty("nextCursor").GetString() ?? next;
            if (!page.GetProperty("hasMore").GetBoolean()) break;
        }
        Assert.Equal(["RESET-FEED-01", "RESET-FEED-02"], devices.Order().ToList());
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
