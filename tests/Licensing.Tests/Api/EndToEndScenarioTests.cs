using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>Critical scenario 11 (plan section 48): the whole platform journey as one test, step by step.</summary>
public sealed class EndToEndScenarioTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public EndToEndScenarioTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task Section_48_journey()
    {
        // 1. Platform admin signs in.
        var platform = await _app.LoginAsync(TestUsers.PlatformAdmin, TestUsers.AdminPassword);

        // 2. Creates a tenant.
        var tenant = await (await platform.PostJsonAsync("/api/v1/tenants", new { name = "شركة السيناريو", code = "E2E", contactEmail = "e2e@e2e.test" }))
            .OkJsonAsync(HttpStatusCode.Created);
        var tenantId = tenant.GetProperty("id").GetGuid();

        // 3. Creates the tenant's admin user.
        await (await platform.PostJsonAsync("/api/v1/users", new { email = "admin@e2e.test", fullName = "مدير السيناريو", password = "E2eAdmin1", role = "TenantAdmin", tenantId }))
            .OkJsonAsync(HttpStatusCode.Created);

        // 4. Tenant admin signs in.
        var admin = await _app.LoginAsync("admin@e2e.test", "E2eAdmin1");

        // 5. Creates a product.
        var product = await (await admin.PostJsonAsync("/api/v1/products", new { code = "E2EAPP", name = "تطبيق السيناريو" })).OkJsonAsync(HttpStatusCode.Created);

        // 6. Creates a plan with entitlements and 7. publishes it.
        var plan = await (await admin.PostJsonAsync("/api/v1/plans", new
        {
            productId = product.GetProperty("id").GetGuid(), code = "GOLD", name = "ذهبي", price = 999.5, currency = "EGP",
            durationDays = 365, maxActivations = 2, heartbeatIntervalHours = 12, offlineGraceDays = 5, features = new[] { "sync", "reports" },
        })).OkJsonAsync(HttpStatusCode.Created);
        var planId = plan.GetProperty("id").GetGuid();
        await (await admin.PostAsync($"/api/v1/plans/{planId}/publish", null)).OkJsonAsync();
        var entitlements = await (await admin.GetAsync($"/api/v1/plans/{planId}/entitlements")).OkJsonAsync();
        Assert.Equal(2, entitlements.GetProperty("maxActivations").GetInt32());

        // 8. Creates a customer with a contact.
        var customer = await (await admin.PostJsonAsync("/api/v1/customers", new { name = "عميل السيناريو", email = "c@e2e.test" })).OkJsonAsync(HttpStatusCode.Created);
        var customerId = customer.GetProperty("customer").GetProperty("id").GetGuid();
        await (await admin.PostJsonAsync($"/api/v1/customers/{customerId}/contacts", new { name = "جهة اتصال", isPrimary = true })).OkJsonAsync();

        // 9. Starts a subscription.
        var sub = await (await admin.PostJsonAsync("/api/v1/subscriptions", new { customerId, planId })).OkJsonAsync(HttpStatusCode.Created);
        var subId = sub.GetProperty("subscription").GetProperty("id").GetGuid();
        Assert.Equal("Active", sub.GetProperty("subscription").GetProperty("status").GetString());

        // 10. Issues a license (key shown once).
        var license = await (await admin.PostJsonAsync("/api/v1/licenses", new { subscriptionId = subId })).OkJsonAsync(HttpStatusCode.Created);
        var key = license.GetProperty("productKey").GetString()!;
        var licenseId = license.GetProperty("license").GetProperty("id").GetGuid();

        // 11. Creates an API client with activation and validation scopes.
        var apiClient = await (await admin.PostJsonAsync("/api/v1/api-clients", new { name = "E2E app", scopes = new[] { "licenses.activate", "licenses.validate" } }))
            .OkJsonAsync(HttpStatusCode.Created);

        // 12. The software gets a client token.
        var device = await _app.ClientTokenAsync(apiClient.GetProperty("client").GetProperty("clientId").GetString()!, apiClient.GetProperty("clientSecret").GetString()!);

        // 13. Activates on a device.
        var activated = await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = key, deviceId = "E2E-DEVICE-0001", productCode = "E2EAPP" })).OkJsonAsync();
        Assert.Equal(new[] { "reports", "sync" }, activated.GetProperty("features").EnumerateArray().Select(f => f.GetString()).ToArray());

        // 14. Validates and sends a heartbeat.
        await (await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = key, deviceId = "E2E-DEVICE-0001" })).OkJsonAsync();
        await (await device.PostJsonAsync("/api/v1/licensing/heartbeat", new { productKey = key, deviceId = "E2E-DEVICE-0001" })).OkJsonAsync();

        // 15. Admin revokes the license.
        await (await admin.PostJsonAsync($"/api/v1/licenses/{licenseId}/revoke", new { reason = "انتهاء التعاقد" })).OkJsonAsync();

        // 16. The software is rejected and the dashboard reflects it.
        var rejected = await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = key, deviceId = "E2E-DEVICE-0001" });
        Assert.Equal("LIC_REVOKED", await rejected.ErrorCodeAsync());
        var dashboard = await (await admin.GetAsync("/api/v1/reports/dashboard")).OkJsonAsync();
        Assert.Equal(1, dashboard.GetProperty("revokedLicenses").GetInt32());

        // Tenant suspension blocks the tenant immediately.
        await (await platform.PostJsonAsync($"/api/v1/tenants/{tenantId}/suspend", new { reason = "test" })).OkJsonAsync();
        var login = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = "admin@e2e.test", password = "E2eAdmin1" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Subscription_actions_follow_the_state_machine_over_http()
    {
        var setup = await new LicenseSetup(_app).IssueAsync();
        var admin = setup.Admin;
        var url = $"/api/v1/subscriptions/{setup.SubscriptionId}";

        var resumeActive = await admin.PostJsonAsync($"{url}/resume", new { });
        Assert.Equal(HttpStatusCode.Conflict, resumeActive.StatusCode);
        Assert.Equal("SUBSCRIPTION_INVALID_TRANSITION", await resumeActive.ErrorCodeAsync());

        var details = await (await admin.GetAsync(url)).OkJsonAsync();
        var version = details.GetProperty("subscription").GetProperty("version").GetInt32();
        var stale = await admin.PostJsonAsync($"{url}/suspend", new { reason = "x", expectedVersion = version - 1 });
        Assert.Equal("CONCURRENCY_CONFLICT", await stale.ErrorCodeAsync());

        var suspended = await (await admin.PostJsonAsync($"{url}/suspend", new { reason = "x", expectedVersion = version })).OkJsonAsync();
        Assert.Equal("Suspended", suspended.GetProperty("subscription").GetProperty("status").GetString());
        Assert.Equal(new[] { "Resume", "Cancel", "Expire" },
            suspended.GetProperty("subscription").GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()).ToArray());
        Assert.True(suspended.GetProperty("history").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Draft_plans_cannot_be_subscribed_and_published_plans_are_versioned()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var product = await (await admin.PostJsonAsync("/api/v1/products", new { code = "VERS", name = "Versioned" })).OkJsonAsync(HttpStatusCode.Created);
        var plan = await (await admin.PostJsonAsync("/api/v1/plans", new
        {
            productId = product.GetProperty("id").GetGuid(), code = "V", name = "V", price = 1, currency = "EGP",
            durationDays = (int?)null, maxActivations = (int?)null, heartbeatIntervalHours = 24, offlineGraceDays = 0, features = Array.Empty<string>(),
        })).OkJsonAsync(HttpStatusCode.Created);
        var planId = plan.GetProperty("id").GetGuid();
        var customer = await (await admin.PostJsonAsync("/api/v1/customers", new { name = "Versions customer" })).OkJsonAsync(HttpStatusCode.Created);

        var draftSub = await admin.PostJsonAsync("/api/v1/subscriptions", new { customerId = customer.GetProperty("customer").GetProperty("id").GetGuid(), planId });
        Assert.Equal("PLAN_NOT_PUBLISHED", await draftSub.ErrorCodeAsync());

        await (await admin.PostAsync($"/api/v1/plans/{planId}/publish", null)).OkJsonAsync();
        var edit = await admin.PutJsonAsync($"/api/v1/plans/{planId}", new
        {
            productId = product.GetProperty("id").GetGuid(), code = "V", name = "V2", price = 2, currency = "EGP",
            heartbeatIntervalHours = 24, offlineGraceDays = 0, features = Array.Empty<string>(),
        });
        Assert.Equal("PLAN_NOT_DRAFT", await edit.ErrorCodeAsync());

        var v2 = await (await admin.PostAsync($"/api/v1/plans/{planId}/new-version", null)).OkJsonAsync(HttpStatusCode.Created);
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal("Draft", v2.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reports_endpoints_return_data()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var dashboard = await (await admin.GetAsync("/api/v1/reports/dashboard")).OkJsonAsync();
        Assert.True(dashboard.GetProperty("activeLicenses").GetInt32() > 0);
        Assert.Equal(14, dashboard.GetProperty("activationsTrend").GetArrayLength());

        var expiring = await (await admin.GetAsync("/api/v1/reports/expiring-licenses?days=30")).OkJsonAsync();
        Assert.NotEmpty(expiring.EnumerateArray());
        var failed = await (await admin.GetAsync("/api/v1/reports/failed-activations?days=14")).OkJsonAsync();
        Assert.NotEmpty(failed.GetProperty("summary").EnumerateArray());

        await _app.Get<Licensing.Infrastructure.Jobs.JobRunner>().AggregateUsageAsync(default);
        var usage = await (await admin.GetAsync("/api/v1/reports/usage?days=7")).OkJsonAsync();
        Assert.NotEmpty(usage.EnumerateArray());
    }

    [Fact]
    public async Task Tests_run_on_the_configured_database_provider()
    {
        var provider = await _app.WithDbAsync(db => Task.FromResult(db.Database.ProviderName));
        Assert.Equal(TestApp.UseSqlServer ? "Microsoft.EntityFrameworkCore.SqlServer" : "Microsoft.EntityFrameworkCore.Sqlite", provider);
    }

    [Fact]
    public async Task Health_endpoints_and_openapi_are_available()
    {
        var client = _app.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var openapi = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/licensing/activate", openapi);
        var response = await client.GetAsync("/health/live");
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
    }
}
