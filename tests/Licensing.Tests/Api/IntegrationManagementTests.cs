using System.Net;
using System.Text.Json;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>Monitor Cloud's client token manages the same subscriptions and licenses the portal does.</summary>
public sealed class IntegrationManagementTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public IntegrationManagementTests(TestAppFixture fixture) => _app = fixture.App;

    [Fact]
    public async Task A_client_can_read_entitlements_and_change_suspend_issue_and_regenerate()
    {
        var user = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var client = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        var limited = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/integration/plans?productCode=ACCOUNTING")).StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await (await limited.GetAsync("/api/v1/integration/plans?productCode=ACCOUNTING")).ErrorCodeAsync());

        var plans = await (await client.GetAsync("/api/v1/integration/plans?productCode=ACCOUNTING")).OkJsonAsync();
        var codes = plans.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToHashSet();
        Assert.Contains("BASIC", codes);
        Assert.Contains("PRO", codes);
        Assert.DoesNotContain("ENTERPRISE", codes);
        var proId = plans.EnumerateArray().First(p => p.GetProperty("code").GetString() == "PRO").GetProperty("id").GetGuid();

        var customers = await (await client.GetAsync("/api/v1/integration/customers?pageSize=50")).OkJsonAsync();
        var shifaId = customers.GetProperty("items").EnumerateArray().First(c => c.GetProperty("name").GetString() == "صيدليات الشفاء").GetProperty("id").GetGuid();

        var before = await (await client.GetAsync($"/api/v1/integration/customers/{shifaId}/entitlements?productCode=ACCOUNTING")).OkJsonAsync();
        var subscription = before.GetProperty("subscriptions").EnumerateArray()
            .First(s => s.GetProperty("status").GetString() == "Active" && s.GetProperty("planCode").GetString() == "BASIC");
        var subscriptionId = subscription.GetProperty("id").GetGuid();

        var ofoq = await _app.LoginAsync(DemoAccounts.OfoqAdmin);
        var foreignId = (await (await ofoq.GetAsync("/api/v1/customers?pageSize=20")).OkJsonAsync()).GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/integration/customers/{foreignId}/entitlements")).StatusCode);

        var changed = await (await client.PostJsonAsync($"/api/v1/integration/subscriptions/{subscriptionId}/change-plan", new { planId = proId })).OkJsonAsync();
        Assert.Equal(proId, changed.GetProperty("subscription").GetProperty("planId").GetGuid());

        var issued = await (await client.PostJsonAsync("/api/v1/integration/licenses", new { subscriptionId })).OkJsonAsync(HttpStatusCode.Created);
        var licenseId = issued.GetProperty("license").GetProperty("id").GetGuid();
        var firstKey = issued.GetProperty("productKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstKey));

        var regenerated = await (await client.PostJsonAsync($"/api/v1/integration/licenses/{licenseId}/regenerate-key", new { })).OkJsonAsync();
        Assert.NotEqual(firstKey, regenerated.GetProperty("productKey").GetString());

        await (await client.PostJsonAsync($"/api/v1/integration/subscriptions/{subscriptionId}/suspend", new { reason = "billing hold" })).OkJsonAsync();
        await (await client.PostJsonAsync($"/api/v1/integration/subscriptions/{subscriptionId}/resume", new { })).OkJsonAsync();
        var cancelled = await (await client.PostJsonAsync($"/api/v1/integration/subscriptions/{subscriptionId}/cancel", new { reason = "customer asked" })).OkJsonAsync();
        Assert.Equal("Cancelled", cancelled.GetProperty("subscription").GetProperty("status").GetString());
    }
}
