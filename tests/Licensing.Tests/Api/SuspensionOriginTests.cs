using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>
/// INT-013: a customer can resume only what they suspended themselves. The website (a service account on the portal
/// routes) marks self-service calls with <c>byCustomer</c>; a provider (staff) suspension stays until staff resume it.
/// The integration routes accept the same flag.
/// </summary>
public sealed class SuspensionOriginTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public SuspensionOriginTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Website() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    [Fact]
    public async Task A_customer_cannot_resume_a_subscription_the_provider_suspended()
    {
        var issued = await _setup.IssueAsync();
        var website = await Website();
        await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/suspend", new { reason = "unpaid" })).OkJsonAsync();

        var refused = await website.PostJsonAsync($"/api/v1/integration/subscriptions/{issued.SubscriptionId}/resume", new { byCustomer = true });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("SUBSCRIPTION_SUSPENDED_BY_PROVIDER", await refused.ErrorCodeAsync());
        // The website acting for staff (no byCustomer) still can.
        var resumed = await (await website.PostJsonAsync($"/api/v1/integration/subscriptions/{issued.SubscriptionId}/resume", new { })).OkJsonAsync();
        Assert.Equal("Active", resumed.GetProperty("subscription").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_customer_resumes_a_subscription_they_suspended()
    {
        var issued = await _setup.IssueAsync();
        var website = await Website();
        await (await website.PostJsonAsync($"/api/v1/integration/subscriptions/{issued.SubscriptionId}/suspend", new { byCustomer = true })).OkJsonAsync();

        var resumed = await (await website.PostJsonAsync($"/api/v1/integration/subscriptions/{issued.SubscriptionId}/resume", new { byCustomer = true })).OkJsonAsync();

        Assert.Equal("Active", resumed.GetProperty("subscription").GetProperty("status").GetString());
        // The flag is cleared by the resume: a later staff suspension is not resumable by the customer.
        await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/suspend", new { reason = "unpaid" })).OkJsonAsync();
        Assert.Equal("SUBSCRIPTION_SUSPENDED_BY_PROVIDER", await (await website.PostJsonAsync(
            $"/api/v1/integration/subscriptions/{issued.SubscriptionId}/resume", new { byCustomer = true })).ErrorCodeAsync());
    }

    [Fact]
    public async Task The_portal_routes_the_website_uses_honour_the_flag()
    {
        var issued = await _setup.IssueAsync();
        var staff = issued.Admin;
        await (await staff.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/suspend", new { reason = "unpaid" })).OkJsonAsync();
        Assert.Equal("SUBSCRIPTION_SUSPENDED_BY_PROVIDER", await (await staff.PostJsonAsync(
            $"/api/v1/subscriptions/{issued.SubscriptionId}/resume", new { byCustomer = true })).ErrorCodeAsync());
        await (await staff.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/resume", new { })).OkJsonAsync();

        await (await staff.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { byCustomer = true })).OkJsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/resume", new { byCustomer = true })).StatusCode);
        await (await staff.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { reason = "abuse" })).OkJsonAsync();
        Assert.Equal("LICENSE_SUSPENDED_BY_PROVIDER", await (await staff.PostJsonAsync(
            $"/api/v1/licenses/{issued.LicenseId}/resume", new { byCustomer = true })).ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"/api/v1/licenses/{issued.LicenseId}/resume", null)).StatusCode);
    }

    [Fact]
    public async Task A_customer_cannot_resume_a_license_the_provider_suspended()
    {
        var issued = await _setup.IssueAsync();
        var website = await Website();
        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { reason = "abuse" })).OkJsonAsync();

        var refused = await website.PostJsonAsync($"/api/v1/integration/licenses/{issued.LicenseId}/resume", new { byCustomer = true });
        Assert.Equal("LICENSE_SUSPENDED_BY_PROVIDER", await refused.ErrorCodeAsync());

        // Older callers send no body at all; that is a staff resume, as before.
        var resumed = await website.PostAsync($"/api/v1/integration/licenses/{issued.LicenseId}/resume", null);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);

        await (await website.PostJsonAsync($"/api/v1/integration/licenses/{issued.LicenseId}/suspend", new { byCustomer = true })).OkJsonAsync();
        Assert.Equal(HttpStatusCode.OK, (await website.PostJsonAsync($"/api/v1/integration/licenses/{issued.LicenseId}/resume", new { byCustomer = true })).StatusCode);
    }
}
