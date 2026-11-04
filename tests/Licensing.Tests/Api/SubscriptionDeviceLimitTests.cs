using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>
/// INT-015 (ADR-004): a subscription may hold any number of licenses, but the plan's MaxActivations limits the devices
/// of the whole subscription. Another license never adds capacity.
/// </summary>
public sealed class SubscriptionDeviceLimitTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public SubscriptionDeviceLimitTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Cloud() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private static async Task<(Guid Id, string Key)> SecondLicenseAsync(LicenseSetup.Issued issued)
    {
        var license = await (await issued.Admin.PostJsonAsync("/api/v1/licenses", new { subscriptionId = issued.SubscriptionId })).OkJsonAsync(HttpStatusCode.Created);
        return (license.GetProperty("license").GetProperty("id").GetGuid(), license.GetProperty("productKey").GetString()!);
    }

    private static Task<HttpResponseMessage> ActivateAsync(HttpClient cloud, string key, string productCode, string device) =>
        cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = key, deviceId = device, productCode });

    private Task<int> ActiveDevicesAsync(Guid subscriptionId) =>
        _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == subscriptionId).Select(s => s.ActiveDevices).FirstAsync());

    [Fact]
    public async Task A_second_license_shares_the_subscription_limit_instead_of_adding_devices()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var second = await SecondLicenseAsync(issued);
        var cloud = await Cloud();

        await (await ActivateAsync(cloud, issued.Key, issued.ProductCode, "LIMIT-A-01")).OkJsonAsync();
        await (await ActivateAsync(cloud, issued.Key, issued.ProductCode, "LIMIT-A-02")).OkJsonAsync();
        await (await ActivateAsync(cloud, second.Key, issued.ProductCode, "LIMIT-B-01")).OkJsonAsync();

        var refused = await ActivateAsync(cloud, second.Key, issued.ProductCode, "LIMIT-B-02");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("LIC_ACTIVATION_LIMIT", await refused.Content.ReadAsStringAsync());
        Assert.Equal(3, await ActiveDevicesAsync(issued.SubscriptionId));
        Assert.Equal(1, await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.Id == second.Id).Select(l => l.ActiveActivations).FirstAsync()));
    }

    [Fact]
    public async Task Releasing_a_device_or_regenerating_a_key_frees_subscription_capacity()
    {
        var issued = await _setup.IssueAsync(maxActivations: 2);
        var second = await SecondLicenseAsync(issued);
        var cloud = await Cloud();
        await (await ActivateAsync(cloud, issued.Key, issued.ProductCode, "FREE-A-01")).OkJsonAsync();
        await (await ActivateAsync(cloud, issued.Key, issued.ProductCode, "FREE-A-02")).OkJsonAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ActivateAsync(cloud, second.Key, issued.ProductCode, "FREE-B-01")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await cloud.PostJsonAsync($"/api/v1/integration/licenses/{issued.LicenseId}/devices/FREE-A-01/release", new { })).StatusCode);
        Assert.Equal(1, await ActiveDevicesAsync(issued.SubscriptionId));
        await (await ActivateAsync(cloud, second.Key, issued.ProductCode, "FREE-B-01")).OkJsonAsync();

        await (await issued.Admin.PostAsync($"/api/v1/licenses/{issued.LicenseId}/regenerate-key", null)).OkJsonAsync();
        Assert.Equal(1, await ActiveDevicesAsync(issued.SubscriptionId));
    }

    [Fact]
    public async Task Parallel_activations_across_licenses_never_exceed_the_subscription_limit()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var second = await SecondLicenseAsync(issued);
        var cloud = await Cloud();

        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            ActivateAsync(cloud, i % 2 == 0 ? issued.Key : second.Key, issued.ProductCode, $"RACE-DEVICE-{i:D2}")));

        Assert.Equal(3, answers.Count(a => a.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, await ActiveDevicesAsync(issued.SubscriptionId));
        var perLicense = await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.SubscriptionId == issued.SubscriptionId).SumAsync(l => l.ActiveActivations));
        Assert.Equal(3, perLicense);
    }

    [Fact]
    public async Task An_unlimited_plan_has_no_subscription_limit()
    {
        var issued = await _setup.IssueAsync(maxActivations: null);
        var second = await SecondLicenseAsync(issued);
        var cloud = await Cloud();

        for (var i = 0; i < 5; i++)
            await (await ActivateAsync(cloud, i % 2 == 0 ? issued.Key : second.Key, issued.ProductCode, $"OPEN-DEVICE-{i:D2}")).OkJsonAsync();

        Assert.Equal(5, await ActiveDevicesAsync(issued.SubscriptionId));
    }

    [Fact]
    public async Task The_integration_answer_reports_the_subscription_limit_and_its_devices()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var second = await SecondLicenseAsync(issued);
        var cloud = await Cloud();
        await (await ActivateAsync(cloud, issued.Key, issued.ProductCode, "REPORT-A-01")).OkJsonAsync();
        await (await ActivateAsync(cloud, second.Key, issued.ProductCode, "REPORT-B-01")).OkJsonAsync();
        var customerId = await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).Select(s => s.CustomerId).FirstAsync());

        var answer = await (await cloud.GetAsync($"/api/v1/integration/customers/{customerId}/entitlements?productCode={issued.ProductCode}")).OkJsonAsync();

        var subscription = answer.GetProperty("subscriptions").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == issued.SubscriptionId);
        Assert.Equal(3, subscription.GetProperty("maxDevices").GetInt32());
        Assert.Equal(2, subscription.GetProperty("activeDevices").GetInt32());
        Assert.Equal("Subscription", subscription.GetProperty("deviceLimitScope").GetString());
    }
}
