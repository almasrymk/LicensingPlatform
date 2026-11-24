using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>
/// INT-004 / INT-005: Monitor Cloud renews and gives back a seat by license id (it never keeps product keys).
/// Routes: POST /api/v1/integration/licenses/{id}/devices/{deviceId}/heartbeat | release.
/// </summary>
public sealed class SeatIntegrationTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public SeatIntegrationTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Cloud() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private static string Heartbeat(Guid license, string device) => $"/api/v1/integration/licenses/{license}/devices/{device}/heartbeat";

    private static string Release(Guid license, string device) => $"/api/v1/integration/licenses/{license}/devices/{device}/release";

    private async Task ActivateAsync(HttpClient cloud, LicenseSetup.Issued issued, string device) =>
        await (await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = device, productCode = issued.ProductCode })).OkJsonAsync();

    private Task<int> ActiveActivationsAsync(Guid license) =>
        _app.WithDbAsync(db => db.Licenses.Where(l => l.Id == license).Select(l => l.ActiveActivations).FirstAsync());

    [Fact]
    public async Task Heartbeat_by_license_id_renews_the_seat_and_records_the_device()
    {
        var issued = await _setup.IssueAsync();
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued, "SEAT-HB-DEVICE-01");
        _app.Clock.Advance(TimeSpan.FromHours(1));

        var beat = await (await cloud.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-HB-DEVICE-01"), new { appVersion = "1.2.0", os = "Windows" })).OkJsonAsync();

        Assert.Equal("active", beat.GetProperty("status").GetString());
        Assert.Equal(issued.LicenseId, beat.GetProperty("licenseId").GetGuid());
        Assert.Equal(issued.SubscriptionId, beat.GetProperty("subscriptionId").GetGuid());
        Assert.False(string.IsNullOrEmpty(beat.GetProperty("token").GetString()));
        Assert.True(beat.GetProperty("checkAfter").GetDateTimeOffset() > _app.Clock.GetUtcNow());
        var activation = await _app.WithDbAsync(db => db.LicenseActivations.AsNoTracking().SingleAsync(a => a.LicenseId == issued.LicenseId && a.DeviceId == "SEAT-HB-DEVICE-01"));
        Assert.Equal("1.2.0", activation.AppVersion);
        Assert.True((_app.Clock.GetUtcNow() - activation.LastHeartbeatAt!.Value).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Heartbeat_of_a_device_without_a_seat_is_LIC_DEVICE_NOT_ACTIVATED()
    {
        var issued = await _setup.IssueAsync();
        var cloud = await Cloud();

        var response = await cloud.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-NEVER-ACTIVE-01"), new { });

        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Heartbeat_follows_the_license_rules()
    {
        var issued = await _setup.IssueAsync();
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued, "SEAT-RULE-DEVICE-1");
        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { reason = "test" })).OkJsonAsync();

        var response = await cloud.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-RULE-DEVICE-1"), new { });

        Assert.Equal("LIC_SUSPENDED", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Release_frees_the_slot_once_and_then_reports_not_activated()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued, "SEAT-REL-DEVICE-01");
        Assert.Equal(1, await ActiveActivationsAsync(issued.LicenseId));

        var first = await cloud.PostAsync(new Uri(Release(issued.LicenseId, "SEAT-REL-DEVICE-01"), UriKind.Relative), null);
        var second = await cloud.PostAsync(new Uri(Release(issued.LicenseId, "SEAT-REL-DEVICE-01"), UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await second.ErrorCodeAsync());
        Assert.Equal(0, await ActiveActivationsAsync(issued.LicenseId));
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await cloud.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-REL-DEVICE-01"), new { })).ErrorCodeAsync());
        // The freed slot can be taken by another device (MaxActivations 1).
        await ActivateAsync(cloud, issued, "SEAT-REL-DEVICE-02");
    }

    [Fact]
    public async Task Another_tenants_client_cannot_renew_or_release_the_seat()
    {
        var issued = await _setup.IssueAsync();
        var cloud = await Cloud();
        await ActivateAsync(cloud, issued, "SEAT-XT-DEVICE-001");
        var foreign = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);

        var beat = await foreign.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-XT-DEVICE-001"), new { });
        var release = await foreign.PostAsync(new Uri(Release(issued.LicenseId, "SEAT-XT-DEVICE-001"), UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, beat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, release.StatusCode);
        Assert.Equal(1, await ActiveActivationsAsync(issued.LicenseId));
    }

    [Fact]
    public async Task A_user_token_cannot_call_the_seat_routes()
    {
        var issued = await _setup.IssueAsync();
        var user = await _app.LoginAsync(DemoAccounts.NourAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostJsonAsync(Heartbeat(issued.LicenseId, "SEAT-USER-DEVICE-1"), new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync(new Uri(Release(issued.LicenseId, "SEAT-USER-DEVICE-1"), UriKind.Relative), null)).StatusCode);
    }
}
