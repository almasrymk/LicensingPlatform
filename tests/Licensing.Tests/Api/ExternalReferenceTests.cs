using System.Net;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>
/// INT-016: the website retries customer and subscription creation after a lost answer. The same
/// <c>externalReference</c> returns the first record, so a retry never creates a second customer or subscription.
/// </summary>
public sealed class ExternalReferenceTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public ExternalReferenceTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private static string NewReference() => "website-test:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_repeated_customer_create_with_the_same_reference_returns_the_first_customer()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var reference = NewReference();

        var first = await (await admin.PostJsonAsync("/api/v1/customers", new { name = "Sample Retail One", externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);
        var again = await (await admin.PostJsonAsync("/api/v1/customers", new { name = "Sample Retail One", externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);

        var id = first.GetProperty("customer").GetProperty("id").GetGuid();
        Assert.Equal(id, again.GetProperty("customer").GetProperty("id").GetGuid());
        Assert.Equal(1, await _app.WithDbAsync(db => db.Customers.IgnoreQueryFilters().CountAsync(c => c.ExternalReference == reference)));
    }

    [Fact]
    public async Task Without_a_reference_every_create_is_a_new_customer()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var name = "Sample Retail " + Guid.NewGuid().ToString("N")[..6];

        var a = await (await admin.PostJsonAsync("/api/v1/customers", new { name })).OkJsonAsync(HttpStatusCode.Created);
        var b = await (await admin.PostJsonAsync("/api/v1/customers", new { name })).OkJsonAsync(HttpStatusCode.Created);

        Assert.NotEqual(a.GetProperty("customer").GetProperty("id").GetGuid(), b.GetProperty("customer").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_repeated_subscription_start_with_the_same_reference_returns_the_first_subscription()
    {
        var issued = await _setup.IssueAsync();
        var customerId = await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).Select(s => s.CustomerId).FirstAsync());
        var reference = NewReference();

        var first = await (await issued.Admin.PostJsonAsync("/api/v1/subscriptions", new { customerId, planId = issued.PlanId, externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);
        var again = await (await issued.Admin.PostJsonAsync("/api/v1/subscriptions", new { customerId, planId = issued.PlanId, externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);

        Assert.Equal(first.GetProperty("subscription").GetProperty("id").GetGuid(), again.GetProperty("subscription").GetProperty("id").GetGuid());
        Assert.Equal(1, await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().CountAsync(s => s.ExternalReference == reference)));
    }

    [Fact]
    public async Task A_reference_used_for_another_customer_is_SUBSCRIPTION_REFERENCE_CONFLICT()
    {
        var issued = await _setup.IssueAsync();
        var customerId = await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).Select(s => s.CustomerId).FirstAsync());
        var other = await (await issued.Admin.PostJsonAsync("/api/v1/customers", new { name = "Sample Clinic Two" })).OkJsonAsync(HttpStatusCode.Created);
        var otherId = other.GetProperty("customer").GetProperty("id").GetGuid();
        var reference = NewReference();
        await (await issued.Admin.PostJsonAsync("/api/v1/subscriptions", new { customerId, planId = issued.PlanId, externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);

        var clash = await issued.Admin.PostJsonAsync("/api/v1/subscriptions", new { customerId = otherId, planId = issued.PlanId, externalReference = reference });

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains("SUBSCRIPTION_REFERENCE_CONFLICT", await clash.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_same_reference_in_another_tenant_is_a_separate_customer()
    {
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var ofoq = await _app.LoginAsync(DemoAccounts.OfoqAdmin);
        var reference = NewReference();

        var a = await (await nour.PostJsonAsync("/api/v1/customers", new { name = "Sample Office A", externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);
        var b = await (await ofoq.PostJsonAsync("/api/v1/customers", new { name = "Sample Office B", externalReference = reference })).OkJsonAsync(HttpStatusCode.Created);

        Assert.NotEqual(a.GetProperty("customer").GetProperty("id").GetGuid(), b.GetProperty("customer").GetProperty("id").GetGuid());
        Assert.Equal("Sample Office B", b.GetProperty("customer").GetProperty("name").GetString());
    }
}
