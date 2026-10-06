using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>LP-1..LP-5: the integration API used by Monitor Cloud.</summary>
public sealed class IntegrationTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public IntegrationTests(TestAppFixture f)
    {
        _app = f.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Client() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private async Task<(LicenseSetup.Issued Issued, Guid CustomerId, HttpClient Client)> ActivatedAsync(string deviceId = "INTEG-DEVICE-0001")
    {
        var issued = await _setup.IssueAsync();
        var client = await Client();
        var activated = await (await client.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId, productCode = issued.ProductCode })).OkJsonAsync();
        return (issued, activated.GetProperty("customerId").GetGuid(), client);
    }

    [Fact]
    public async Task Activation_response_carries_licence_customer_and_subscription_ids_and_the_cid_claim()
    {
        var issued = await _setup.IssueAsync();
        var client = await Client();

        var activated = await (await client.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "INTEG-IDS-0001" })).OkJsonAsync();

        Assert.Equal(issued.LicenseId, activated.GetProperty("licenseId").GetGuid());
        Assert.Equal(issued.SubscriptionId, activated.GetProperty("subscriptionId").GetGuid());
        var customerId = activated.GetProperty("customerId").GetGuid();
        Assert.NotEqual(Guid.Empty, customerId);
        Assert.Equal("active", activated.GetProperty("status").GetString());
        var token = new JwtSecurityTokenHandler().ReadJwtToken(activated.GetProperty("token").GetString());
        Assert.Equal(customerId.ToString(), token.Claims.Single(c => c.Type == "cid").Value);
        Assert.Equal(issued.LicenseId.ToString(), token.Claims.Single(c => c.Type == "lid").Value);
    }

    [Fact]
    public async Task Customers_and_entitlements_describe_the_customers_subscription_and_licence()
    {
        var (issued, customerId, client) = await ActivatedAsync();

        var customers = await (await client.GetAsync("/api/v1/integration/customers?pageSize=200")).OkJsonAsync();
        Assert.Contains(customers.GetProperty("items").EnumerateArray(), c => c.GetProperty("id").GetGuid() == customerId);

        var entitlements = await (await client.GetAsync($"/api/v1/integration/customers/{customerId}/entitlements?productCode={issued.ProductCode}")).OkJsonAsync();
        Assert.Equal("Active", entitlements.GetProperty("customerStatus").GetString());
        var subscription = Assert.Single(entitlements.GetProperty("subscriptions").EnumerateArray());
        Assert.Equal(issued.SubscriptionId, subscription.GetProperty("id").GetGuid());
        Assert.Equal("STD", subscription.GetProperty("planCode").GetString());
        Assert.Contains("reports", subscription.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        var license = Assert.Single(entitlements.GetProperty("licenses").EnumerateArray());
        Assert.Equal(issued.LicenseId, license.GetProperty("id").GetGuid());
        Assert.Equal(3, license.GetProperty("maxActivations").GetInt32());
        Assert.Equal(1, license.GetProperty("activeActivations").GetInt32());
    }

    [Fact]
    public async Task Entitlements_of_another_product_are_empty_and_unknown_customers_are_not_found()
    {
        var (_, customerId, client) = await ActivatedAsync("INTEG-DEVICE-0002");

        var other = await (await client.GetAsync($"/api/v1/integration/customers/{customerId}/entitlements?productCode=NOPE")).OkJsonAsync();
        Assert.Empty(other.GetProperty("subscriptions").EnumerateArray());
        Assert.Empty(other.GetProperty("licenses").EnumerateArray());

        var missing = await client.GetAsync($"/api/v1/integration/customers/{Guid.NewGuid()}/entitlements");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Plans_lists_published_plans_of_the_product()
    {
        var issued = await _setup.IssueAsync();
        var client = await Client();

        var plans = await (await client.GetAsync($"/api/v1/integration/plans?productCode={issued.ProductCode}")).OkJsonAsync();

        var plan = Assert.Single(plans.EnumerateArray());
        Assert.Equal("STD", plan.GetProperty("code").GetString());
        Assert.Equal(3, plan.GetProperty("maxActivations").GetInt32());
        Assert.Equal("EGP", plan.GetProperty("price").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Heartbeat_and_release_work_by_licence_id_without_the_product_key()
    {
        var (issued, _, client) = await ActivatedAsync("INTEG-DEVICE-0003");
        var url = $"/api/v1/integration/licenses/{issued.LicenseId}/devices/INTEG-DEVICE-0003";

        var beat = await (await client.PostJsonAsync(url + "/heartbeat", new { appVersion = "2.1.0", os = "Windows" })).OkJsonAsync();
        Assert.Equal("active", beat.GetProperty("status").GetString());
        Assert.Equal(issued.LicenseId, beat.GetProperty("licenseId").GetGuid());

        var activations = await (await client.GetAsync($"/api/v1/integration/licenses/{issued.LicenseId}/activations")).OkJsonAsync();
        var activation = Assert.Single(activations.EnumerateArray());
        Assert.Equal("2.1.0", activation.GetProperty("appVersion").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(url + "/release", null)).StatusCode);
        var again = await client.PostAsync(url + "/release", null);
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await again.ErrorCodeAsync());
        var afterRelease = await client.PostJsonAsync(url + "/heartbeat", new { });
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await afterRelease.ErrorCodeAsync());

        var count = await _app.WithDbAsync(db => Task.FromResult(db.Licenses.Where(l => l.Id == issued.LicenseId).Select(l => l.ActiveActivations).First()));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Change_feed_returns_events_in_order_with_a_cursor_and_customer_ids()
    {
        var (_, customerId, client) = await ActivatedAsync("INTEG-DEVICE-0004");

        var all = new List<(string Type, Guid? Customer, DateTimeOffset At)>();
        string? cursor = null;
        for (var i = 0; i < 100; i++)
        {
            var url = "/api/v1/integration/changes?take=5" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await (await client.GetAsync(url)).OkJsonAsync();
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                var customer = item.GetProperty("customerId");
                all.Add((item.GetProperty("type").GetString()!, customer.ValueKind == System.Text.Json.JsonValueKind.Null ? null : customer.GetGuid(), item.GetProperty("occurredAt").GetDateTimeOffset()));
            }
            cursor = page.GetProperty("nextCursor").GetString();
            if (!page.GetProperty("hasMore").GetBoolean())
                break;
        }

        Assert.Contains(all, e => e.Type == "LicenseIssuedV1" && e.Customer == customerId);
        Assert.Contains(all, e => e.Type == "SubscriptionStartedV1" && e.Customer == customerId);
        Assert.Equal(all.Select(e => e.At).Order().ToList(), all.Select(e => e.At).ToList());

        var empty = await (await client.GetAsync($"/api/v1/integration/changes?cursor={cursor}")).OkJsonAsync();
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.False(empty.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected()
    {
        var client = await Client();

        var response = await client.GetAsync("/api/v1/integration/changes?cursor=not-a-cursor");

        Assert.Equal("VALIDATION_FAILED", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Another_tenants_client_cannot_see_the_customer_or_its_events()
    {
        var (issued, customerId, _) = await ActivatedAsync("INTEG-DEVICE-0005");
        var ofoqAdmin = await _app.LoginAsync(DemoAccounts.OfoqAdmin);
        var created = await (await ofoqAdmin.PostJsonAsync("/api/v1/api-clients", new { name = "Integration", scopes = new[] { "licenses.read", "subscriptions.read", "customers.read" } })).OkJsonAsync(HttpStatusCode.Created);
        var other = await _app.ClientTokenAsync(created.GetProperty("client").GetProperty("clientId").GetString()!, created.GetProperty("clientSecret").GetString()!);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/integration/customers/{customerId}/entitlements")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/integration/licenses/{issued.LicenseId}/activations")).StatusCode);
        var feed = await (await other.GetAsync("/api/v1/integration/changes?take=500")).OkJsonAsync();
        Assert.DoesNotContain(feed.GetProperty("items").EnumerateArray(), i => i.GetProperty("customerId").ToString() == customerId.ToString());
    }

    [Fact]
    public async Task Integration_endpoints_need_a_client_token_with_the_scope()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/integration/customers")).StatusCode);

        var created = await (await admin.PostJsonAsync("/api/v1/api-clients", new { name = "Validate only", scopes = new[] { "licenses.validate" } })).OkJsonAsync(HttpStatusCode.Created);
        var limited = await _app.ClientTokenAsync(created.GetProperty("client").GetProperty("clientId").GetString()!, created.GetProperty("clientSecret").GetString()!);

        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync("/api/v1/integration/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync("/api/v1/integration/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync("/api/v1/integration/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Anonymous().GetAsync("/api/v1/integration/customers")).StatusCode);
    }

    [Fact]
    public async Task New_scopes_can_be_granted_to_api_clients()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);

        var response = await admin.PostJsonAsync("/api/v1/api-clients", new { name = "Monitor Cloud", scopes = new[] { "licenses.activate", "licenses.validate", "licenses.read", "customers.read", "subscriptions.read", "catalog.read" } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Outbox_rows_of_customer_owned_aggregates_carry_the_customer_id()
    {
        var (issued, customerId, _) = await ActivatedAsync("INTEG-DEVICE-0006");

        var rows = await _app.WithDbAsync(db => Task.FromResult(db.OutboxMessages.Where(m => m.Type == "LicenseIssuedV1" && m.CustomerId == customerId).Count()));

        Assert.Equal(1, rows);
        Assert.NotEqual(Guid.Empty, issued.LicenseId);
    }
}
