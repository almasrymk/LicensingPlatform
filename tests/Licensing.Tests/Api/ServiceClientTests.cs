using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>
/// E7: the website signs in as an API client (client id and secret, scoped) instead of a portal user with a password.
/// The client's scopes grant the portal permissions of the same name, only those in ApiScopes.GrantablePermissions.
/// </summary>
public sealed class ServiceClientTests : IClassFixture<TestAppFixture>
{
    private static readonly string[] WebsiteScopes = ["catalog.read", "customers.manage", "subscriptions.read", "subscriptions.manage", "licenses.read", "licenses.manage"];
    private readonly TestApp _app;
    public ServiceClientTests(TestAppFixture fixture) => _app = fixture.App;

    private async Task<HttpClient> ServiceClientAsync(string admin, params string[] scopes)
    {
        var owner = await _app.LoginAsync(admin);
        var created = await (await owner.PostJsonAsync("/api/v1/api-clients", new { name = "Website (test)", scopes })).OkJsonAsync(HttpStatusCode.Created);
        return await _app.ClientTokenAsync(created.GetProperty("client").GetProperty("clientId").GetString()!, created.GetProperty("clientSecret").GetString()!);
    }

    [Fact]
    public async Task A_scoped_client_does_what_the_website_needs_through_the_portal_routes()
    {
        var site = await ServiceClientAsync(DemoAccounts.NourAdmin, WebsiteScopes);

        var plans = await (await site.GetAsync("/api/v1/plans?status=Published&page=1&pageSize=200")).OkJsonAsync();
        var plan = plans.GetProperty("items").EnumerateArray().First(p => p.GetProperty("kind").GetString() == "Paid");
        var customer = await (await site.PostJsonAsync("/api/v1/customers",
            new { name = "Sample Company (service client)", email = "billing@example.test", externalReference = $"web-{Guid.NewGuid():N}" })).OkJsonAsync(HttpStatusCode.Created);
        var customerId = customer.GetProperty("customer").GetProperty("id").GetGuid();
        var subscription = await (await site.PostJsonAsync("/api/v1/subscriptions",
            new { customerId, planId = plan.GetProperty("id").GetGuid(), notes = "Website order", externalReference = $"web-{Guid.NewGuid():N}" })).OkJsonAsync(HttpStatusCode.Created);
        var subscriptionId = subscription.GetProperty("subscription").GetProperty("id").GetGuid();
        var issued = await (await site.PostJsonAsync("/api/v1/licenses", new { subscriptionId })).OkJsonAsync(HttpStatusCode.Created);
        var licenseId = issued.GetProperty("license").GetProperty("id").GetGuid();

        var listed = await (await site.GetAsync($"/api/v1/licenses?customerId={customerId}&page=1&pageSize=200")).OkJsonAsync();
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetGuid() == licenseId);
        await (await site.GetAsync($"/api/v1/licenses/{licenseId}")).OkJsonAsync();
        await (await site.GetAsync($"/api/v1/subscriptions?customerId={customerId}&page=1&pageSize=50")).OkJsonAsync();
        await (await site.PostJsonAsync($"/api/v1/licenses/{licenseId}/suspend", new { reason = "test", byCustomer = true })).OkJsonAsync();
    }

    [Theory]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("GET", "/api/v1/api-clients")]
    [InlineData("GET", "/api/v1/audit")]
    [InlineData("POST", "/api/v1/plans")]
    public async Task Administration_stays_with_people_whatever_the_client_scopes(string method, string path)
    {
        var site = await ServiceClientAsync(DemoAccounts.NourAdmin, WebsiteScopes);

        var response = method == "GET" ? await site.GetAsync(path) : await site.PostJsonAsync(path, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_client_without_the_scope_is_refused_and_one_tenants_client_never_sees_another_tenant()
    {
        var readOnly = await ServiceClientAsync(DemoAccounts.NourAdmin, "licenses.read");
        var ofoq = await ServiceClientAsync(DemoAccounts.OfoqAdmin, WebsiteScopes);
        var nourAdmin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var nourLicense = (await (await nourAdmin.GetAsync("/api/v1/licenses?page=1&pageSize=1")).OkJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid();

        var create = await readOnly.PostJsonAsync("/api/v1/customers", new { name = "Not allowed", externalReference = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await create.ErrorCodeAsync());
        await (await readOnly.GetAsync($"/api/v1/licenses/{nourLicense}")).OkJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await ofoq.GetAsync($"/api/v1/licenses/{nourLicense}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ofoq.PostJsonAsync($"/api/v1/licenses/{nourLicense}/suspend", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task The_new_customers_manage_scope_can_be_given_to_a_client()
    {
        var owner = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var created = await owner.PostJsonAsync("/api/v1/api-clients", new { name = "Customers", scopes = new[] { "customers.manage" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var unknown = await owner.PostJsonAsync("/api/v1/api-clients", new { name = "Admin", scopes = new[] { "users.manage" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
