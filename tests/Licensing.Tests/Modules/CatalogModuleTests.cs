using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Persistence;
using Licensing.Modules.Catalog.Application;
using Licensing.Tests.Api;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.Tests.Modules;

/// <summary>
/// Catalog now gets license and subscription facts through <see cref="ICatalogUsageReader"/> instead of querying those
/// tables itself. These pin that the answers are the same as before, including the tenant and customer filters.
/// </summary>
public sealed class CatalogModuleTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public CatalogModuleTests(TestAppFixture f) => _app = f.App;

    [Theory]
    [InlineData(DemoAccounts.NourAdmin)]
    [InlineData(DemoAccounts.AlamalUser)]
    public async Task Product_license_counts_match_the_licenses_the_caller_can_see(string account)
    {
        var client = await _app.LoginAsync(account);
        var products = await (await client.GetAsync("/api/v1/products?pageSize=200")).OkJsonAsync();
        var visible = await (await client.GetAsync("/api/v1/licenses?pageSize=200")).OkJsonAsync();

        var expected = visible.GetProperty("items").EnumerateArray()
            .GroupBy(l => l.GetProperty("productCode").GetString()!)
            .ToDictionary(g => g.Key, g => g.Count());
        var items = products.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, p => p.GetProperty("licenses").GetInt32() > 0);
        foreach (var product in items)
            Assert.Equal(expected.GetValueOrDefault(product.GetProperty("code").GetString()!), product.GetProperty("licenses").GetInt32());
    }

    [Fact]
    public async Task Plan_in_use_is_reported_for_plans_with_licenses_only()
    {
        var (usedPlan, unusedPlan) = await _app.WithDbAsync(async db =>
        {
            var used = await db.Licenses.Select(l => l.PlanId).FirstAsync();
            var usedPlans = db.Licenses.Select(l => l.PlanId).Concat(db.Subscriptions.Select(s => s.PlanId));
            var unused = await db.Plans.Where(p => !usedPlans.Contains(p.Id)).Select(p => p.Id).FirstAsync();
            return (used, unused);
        });

        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
        var reader = scope.ServiceProvider.GetRequiredService<ICatalogUsageReader>();
        Assert.True(await reader.IsPlanInUseAsync(usedPlan, CancellationToken.None));
        Assert.False(await reader.IsPlanInUseAsync(unusedPlan, CancellationToken.None));
    }
}
