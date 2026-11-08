using System.Net;
using System.Net.Http.Json;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>
/// B5 (product owner 2026-11-08): a new install starts its trial with a single-use code from the website, with no shared
/// secret in the installer. One trial per device still holds, the code is device-bound once used, and nothing of another
/// customer's subscription is reachable.
/// </summary>
public sealed class TrialGrantTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;

    public TrialGrantTests(TestAppFixture f) => _app = f.App;

    private Task<HttpClient> Website() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    private async Task<string> ProductAsync(int? trialDays = 14)
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var code = "G" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var product = await (await admin.PostJsonAsync("/api/v1/products", new { code, name = "Trial grant " + code })).OkJsonAsync(HttpStatusCode.Created);
        var plan = await (await admin.PostJsonAsync("/api/v1/plans", new
        {
            productId = product.GetProperty("id").GetGuid(), code = "STD", name = "Standard", price = 100, currency = "EGP",
            durationDays = 365, trialDays, maxActivations = 3, heartbeatIntervalHours = 24, offlineGraceDays = 7, features = new[] { "reports" },
        })).OkJsonAsync(HttpStatusCode.Created);
        await (await admin.PostAsync($"/api/v1/plans/{plan.GetProperty("id").GetGuid()}/publish", null)).OkJsonAsync();
        return code;
    }

    private static async Task<string> GrantAsync(HttpClient website, string productCode, string reference) =>
        (await (await website.PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode, reference })).OkJsonAsync())
            .GetProperty("trialCode").GetString()!;

    private static object Redeem(string code, string device, string productCode) =>
        new { trialCode = code, deviceId = device, deviceName = "New install", productCode, appVersion = "1.0.0", os = "Windows" };

    private static string Run() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    [Fact]
    public async Task A_code_from_the_website_starts_one_device_trial_without_an_API_client_and_gives_a_credential()
    {
        var product = await ProductAsync(trialDays: 14);
        var website = await Website();
        var issued = await (await website.PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode = product, reference = "website-user:" + Run() })).OkJsonAsync();
        var code = issued.GetProperty("trialCode").GetString()!;
        Assert.StartsWith("ltg_", code, StringComparison.Ordinal);
        Assert.Equal(14, issued.GetProperty("trialDays").GetInt32());
        Assert.InRange(issued.GetProperty("expiresAt").GetDateTimeOffset(), _app.Clock.GetUtcNow().AddHours(23), _app.Clock.GetUtcNow().AddHours(25));

        var anonymous = _app.Anonymous();
        var device = "GRANT-DEV-" + Run();
        var trial = await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(code, device, product))).OkJsonAsync();

        var credential = trial.GetProperty("deviceCredential").GetString()!;
        Assert.StartsWith("lcd_", credential, StringComparison.Ordinal);
        Assert.Equal(14, trial.GetProperty("trialDays").GetInt32());
        var license = trial.GetProperty("license");
        Assert.Equal("active", license.GetProperty("status").GetString());
        Assert.Equal(1, license.GetProperty("maxActivations").GetInt32());
        var check = new { licenseId = license.GetProperty("licenseId").GetGuid(), deviceId = device };
        using var validate = new HttpRequestMessage(HttpMethod.Post, "/api/v1/licensing/device/validate") { Content = JsonContent.Create(check) };
        validate.Headers.Add("X-Device-Credential", credential);
        Assert.Equal("active", (await (await anonymous.SendAsync(validate)).OkJsonAsync()).GetProperty("status").GetString());

        var leaked = await _app.WithDbAsync(async db =>
            await db.AuditRecords.IgnoreQueryFilters().AnyAsync(a => a.Details != null && (a.Details.Contains(code) || a.Details.Contains(credential))) ||
            await db.TrialGrants.IgnoreQueryFilters().AnyAsync(g => g.CodeHash == code));
        Assert.False(leaked);
    }

    [Fact]
    public async Task A_code_works_once_and_a_second_device_cannot_use_it()
    {
        var product = await ProductAsync();
        var code = await GrantAsync(await Website(), product, "website-user:" + Run());
        var anonymous = _app.Anonymous();

        await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(code, "GRANT-ONCE-" + Run(), product))).OkJsonAsync();
        var second = await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(code, "GRANT-ONCE-" + Run(), product));

        Assert.Equal("LIC_TRIAL_GRANT_INVALID", await second.ErrorCodeAsync());
    }

    [Fact]
    public async Task One_trial_per_device_holds_and_the_refused_code_stays_usable_on_another_device()
    {
        var product = await ProductAsync();
        var website = await Website();
        var anonymous = _app.Anonymous();
        var device = "GRANT-REINSTALL-" + Run();
        await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(await GrantAsync(website, product, "website-user:" + Run()), device, product))).OkJsonAsync();

        // The same computer, reinstalled, with a code from a second website account.
        var other = await GrantAsync(website, product, "website-user:" + Run());
        var again = await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(other, device, product));
        Assert.Equal("LIC_TRIAL_USED", await again.ErrorCodeAsync());

        await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(other, "GRANT-FRESH-" + Run(), product))).OkJsonAsync();
    }

    [Fact]
    public async Task Unknown_expired_wrong_product_and_replaced_codes_get_the_same_answer()
    {
        var product = await ProductAsync();
        var otherProduct = await ProductAsync();
        var website = await Website();
        var anonymous = _app.Anonymous();
        var reference = "website-user:" + Run();

        var replaced = await GrantAsync(website, product, reference);
        var current = await GrantAsync(website, product, reference);
        Assert.NotEqual(replaced, current);

        foreach (var (code, productCode) in new[] { ("ltg_" + new string('A', 43), product), ("not-a-code", product), (replaced, product), (current, otherProduct) })
            Assert.Equal("LIC_TRIAL_GRANT_INVALID", await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(code, "GRANT-BAD-" + Run(), productCode))).ErrorCodeAsync());

        _app.Clock.Advance(TimeSpan.FromHours(25));
        try
        {
            Assert.Equal("LIC_TRIAL_GRANT_INVALID", await (await anonymous.PostJsonAsync("/api/v1/licensing/device/trial", Redeem(current, "GRANT-LATE-" + Run(), product))).ErrorCodeAsync());
        }
        finally
        {
            _app.Clock.Advance(TimeSpan.FromHours(-25));
        }
    }

    [Fact]
    public async Task One_website_account_gets_one_trial_per_product()
    {
        var product = await ProductAsync();
        var website = await Website();
        var reference = "website-user:" + Run();
        await (await _app.Anonymous().PostJsonAsync("/api/v1/licensing/device/trial", Redeem(await GrantAsync(website, product, reference), "GRANT-ACCOUNT-" + Run(), product))).OkJsonAsync();

        var again = await website.PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode = product, reference });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("LIC_TRIAL_GRANT_USED", await again.ErrorCodeAsync());
    }

    [Fact]
    public async Task Issuing_codes_needs_the_trials_grant_scope_and_a_product_with_trial_days()
    {
        var withoutTrial = await ProductAsync(trialDays: null);
        var website = await Website();
        Assert.Equal("LIC_TRIAL_UNAVAILABLE", await (await website.PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode = withoutTrial, reference = "website-user:x" })).ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Anonymous().PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode = withoutTrial, reference = "r" })).StatusCode);

        // The other demo tenant's client holds only licenses.activate and licenses.validate.
        var limited = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.PostJsonAsync("/api/v1/licensing/trial-grants", new { productCode = withoutTrial, reference = "r" })).StatusCode);
    }

    [Fact]
    public async Task A_code_of_one_tenant_never_reaches_another_tenants_product()
    {
        var product = await ProductAsync();
        var code = await GrantAsync(await Website(), product, "website-user:" + Run());
        var ofoq = await _app.LoginAsync(DemoAccounts.OfoqAdmin);
        var ofoqProduct = await (await ofoq.PostJsonAsync("/api/v1/products", new { code = product, name = "Same code elsewhere" })).OkJsonAsync(HttpStatusCode.Created);
        Assert.NotEqual(Guid.Empty, ofoqProduct.GetProperty("id").GetGuid());

        var trial = await (await _app.Anonymous().PostJsonAsync("/api/v1/licensing/device/trial", Redeem(code, "GRANT-TENANT-" + Run(), product))).OkJsonAsync();

        var licenseId = trial.GetProperty("license").GetProperty("licenseId").GetGuid();
        var tenant = await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.Id == licenseId).Select(l => l.TenantId).SingleAsync());
        var nour = await _app.WithDbAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Email == DemoAccounts.NourAdmin).Select(u => u.TenantId).SingleAsync());
        Assert.Equal(nour, tenant);
    }
}
