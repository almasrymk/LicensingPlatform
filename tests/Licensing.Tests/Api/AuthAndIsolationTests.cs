using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.Tests.Api;

public sealed class AuthTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public AuthTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task Login_returns_tokens_and_profile_with_permissions()
    {
        var response = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = DemoAccounts.NourAdmin, password = TestUsers.DemoPassword });
        var body = await response.OkJsonAsync();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("refreshToken").GetString()));
        var user = body.GetProperty("user");
        Assert.Equal("TenantAdmin", user.GetProperty("role").GetString());
        Assert.Equal("شركة النور للبرمجيات", user.GetProperty("tenantName").GetString());
        Assert.Contains("users.manage", user.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_error()
    {
        var a = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = DemoAccounts.NourOperator, password = "Wrong123" });
        var b = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = "nobody@nowhere.test", password = "Wrong123" });
        Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, b.StatusCode);
        Assert.Equal("AUTH_INVALID_CREDENTIALS", await a.ErrorCodeAsync());
        Assert.Equal("AUTH_INVALID_CREDENTIALS", await b.ErrorCodeAsync());
    }

    [Fact]
    public async Task Account_locks_after_five_failures_and_failures_are_audited()
    {
        var admin = await _app.LoginAsync(TestUsers.PlatformAdmin, TestUsers.AdminPassword);
        var created = await admin.PostJsonAsync("/api/v1/users", new
        {
            email = "lockme@nour.test", fullName = "Lock Me", password = "Strong123", role = "TenantOperator",
            tenantId = await TenantIdAsync("NOUR"),
        });
        await created.OkJsonAsync(HttpStatusCode.Created);

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = "lockme@nour.test", password = "Wrong123" });
        Assert.Equal(HttpStatusCode.Locked, last.StatusCode);

        var correct = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = "lockme@nour.test", password = "Strong123" });
        Assert.Equal("AUTH_LOCKED", await correct.ErrorCodeAsync());

        var failures = await _app.WithDbAsync(db => db.AuditRecords.CountAsync(a => a.Action == "auth.login" && !a.Success));
        Assert.True(failures >= 5);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_session()
    {
        var login = await (await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = DemoAccounts.OfoqAdmin, password = TestUsers.DemoPassword })).OkJsonAsync();
        var first = login.GetProperty("refreshToken").GetString()!;

        var rotated = await (await _app.Anonymous().PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first })).OkJsonAsync();
        var second = rotated.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);

        // Presenting the old token again is treated as theft: it fails and the new one is revoked too.
        var reuse = await _app.Anonymous().PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var afterReuse = await _app.Anonymous().PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = second });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);

        var stored = await _app.WithDbAsync(db => db.RefreshTokens.AnyAsync(t => t.TokenHash == first || t.TokenHash == second));
        Assert.False(stored); // only hashes are stored
    }

    [Fact]
    public async Task Users_of_a_suspended_tenant_cannot_sign_in()
    {
        var response = await _app.Anonymous().PostJsonAsync("/api/v1/auth/login", new { email = DemoAccounts.StoppedAdmin, password = TestUsers.DemoPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_token_is_401_problem_details()
    {
        var response = await _app.Anonymous().GetAsync("/api/v1/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("AUTH_UNAUTHORIZED", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Validation_errors_are_problem_details_with_field_errors()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var response = await admin.PostJsonAsync("/api/v1/customers", new { name = "", email = "bad" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.JsonAsync();
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("Name", out _));
    }

    private Task<Guid> TenantIdAsync(string code) => _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == code).Select(t => t.Id).FirstAsync());
}

/// <summary>
/// Critical scenario 1: tenant A never reads or changes tenant B's data, on every tenant-owned endpoint, even when it sends
/// tenant B's id in the body. Customer users only see their own customer.
/// </summary>
public sealed class TenantIsolationTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public TenantIsolationTests(TestAppFixture f) => _app = f.App;

    private Task<Guid> IdAsync(Func<AppDbContext, IQueryable<Guid>> query) => _app.WithDbAsync(db => query(db).FirstAsync());

    [Theory]
    [InlineData("customers")]
    [InlineData("products")]
    [InlineData("plans")]
    [InlineData("subscriptions")]
    [InlineData("licenses")]
    public async Task Lists_only_contain_the_callers_tenant(string resource)
    {
        var ofoqId = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "OFOQ").Select(t => t.Id).FirstAsync());
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var body = await (await nour.GetAsync($"/api/v1/{resource}?pageSize=200")).OkJsonAsync();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.NotEqual(ofoqId, i.GetProperty("tenantId").GetGuid()));
    }

    public static TheoryData<string, string> OtherTenantUrls() => new()
    {
        { "customer", "/api/v1/customers/{0}" },
        { "product", "/api/v1/products/{0}" },
        { "plan", "/api/v1/plans/{0}" },
        { "subscription", "/api/v1/subscriptions/{0}" },
        { "license", "/api/v1/licenses/{0}" },
    };

    [Theory]
    [MemberData(nameof(OtherTenantUrls))]
    public async Task Reading_another_tenants_record_by_id_is_not_found(string kind, string urlFormat)
    {
        var id = kind switch
        {
            "customer" => await IdAsync(db => db.Customers.Where(c => c.Name == "مدارس المنار").Select(c => c.Id)),
            "product" => await IdAsync(db => db.Products.Where(p => p.Code == "SCHOOL").Select(p => p.Id)),
            "plan" => await IdAsync(db => db.Plans.Where(p => p.Code == "YEAR").Select(p => p.Id)),
            "subscription" => await IdAsync(db => db.Subscriptions.Where(s => db.Customers.Any(c => c.Id == s.CustomerId && c.Name == "مدارس المنار")).Select(s => s.Id)),
            _ => await IdAsync(db => db.Licenses.Where(l => l.ProductCode == "SCHOOL").Select(l => l.Id)),
        };
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var response = await nour.GetAsync(string.Format(urlFormat, id));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Updating_or_acting_on_another_tenants_records_fails()
    {
        var school = await IdAsync(db => db.Customers.Where(c => c.Name == "مدارس المنار").Select(c => c.Id));
        var schoolLicense = await IdAsync(db => db.Licenses.Where(l => l.ProductCode == "SCHOOL").Select(l => l.Id));
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);

        Assert.Equal(HttpStatusCode.NotFound, (await nour.PutJsonAsync($"/api/v1/customers/{school}", new { name = "hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await nour.PostJsonAsync($"/api/v1/licenses/{schoolLicense}/revoke", new { reason = "x" })).StatusCode);

        var unchanged = await _app.WithDbAsync(db => db.Customers.Where(c => c.Id == school).Select(c => c.Name).FirstAsync());
        Assert.Equal("مدارس المنار", unchanged);
    }

    [Fact]
    public async Task Tenant_id_in_the_body_is_ignored_for_tenant_users()
    {
        var ofoqId = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "OFOQ").Select(t => t.Id).FirstAsync());
        var nourId = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "NOUR").Select(t => t.Id).FirstAsync());
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);

        var created = await (await nour.PostJsonAsync("/api/v1/customers", new { name = "عميل بمعرف مزيف", tenantId = ofoqId }))
            .OkJsonAsync(HttpStatusCode.Created);
        Assert.Equal(nourId, created.GetProperty("customer").GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task Platform_admin_sees_every_tenant_and_can_narrow_with_header()
    {
        var admin = await _app.LoginAsync(TestUsers.PlatformAdmin, TestUsers.AdminPassword);
        var all = await (await admin.GetAsync("/api/v1/customers?pageSize=200")).OkJsonAsync();
        var tenantIds = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tenantId").GetGuid()).Distinct().Count();
        Assert.True(tenantIds >= 2);

        var ofoqId = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "OFOQ").Select(t => t.Id).FirstAsync());
        admin.DefaultRequestHeaders.Add("X-Tenant-Id", ofoqId.ToString());
        var narrowed = await (await admin.GetAsync("/api/v1/customers?pageSize=200")).OkJsonAsync();
        Assert.All(narrowed.GetProperty("items").EnumerateArray(), i => Assert.Equal(ofoqId, i.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task Customer_user_only_sees_its_own_customer_subscriptions_licenses_and_reports()
    {
        var alamalId = await IdAsync(db => db.Customers.Where(c => c.Name == "مؤسسة الأمل التجارية").Select(c => c.Id));
        var shifaLicense = await IdAsync(db => db.Licenses.Where(l => db.Customers.Any(c => c.Id == l.CustomerId && c.Name == "صيدليات الشفاء")).Select(l => l.Id));
        var user = await _app.LoginAsync(DemoAccounts.AlamalUser);

        var customers = await (await user.GetAsync("/api/v1/customers")).OkJsonAsync();
        Assert.Equal(1, customers.GetProperty("total").GetInt32());

        foreach (var resource in new[] { "subscriptions", "licenses" })
        {
            var list = await (await user.GetAsync($"/api/v1/{resource}?pageSize=200")).OkJsonAsync();
            Assert.NotEmpty(list.GetProperty("items").EnumerateArray());
            Assert.All(list.GetProperty("items").EnumerateArray(), i => Assert.Equal(alamalId, i.GetProperty("customerId").GetGuid()));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/licenses/{shifaLicense}")).StatusCode);

        var devices = await (await user.GetAsync("/api/v1/reports/active-devices")).OkJsonAsync();
        Assert.All(devices.EnumerateArray(), d => Assert.Equal("مؤسسة الأمل التجارية", d.GetProperty("customerName").GetString()));

        var expiring = await (await user.GetAsync("/api/v1/reports/expiring-licenses?days=60")).OkJsonAsync();
        Assert.All(expiring.EnumerateArray(), d => Assert.Equal(alamalId, d.GetProperty("customerId").GetGuid()));

        var dashboard = await (await user.GetAsync("/api/v1/reports/dashboard")).OkJsonAsync();
        Assert.Equal(1, dashboard.GetProperty("customers").GetInt32());
    }

    [Fact]
    public async Task Every_tenant_owned_table_is_filtered_for_a_tenant_scope()
    {
        // Direct check of the global query filters on all tenant-owned tables, independent of endpoints.
        var nourId = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "NOUR").Select(t => t.Id).FirstAsync());
        using var scope = _app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<Licensing.Infrastructure.Identity.TenantContext>().RunAsSystem(nourId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // RunAsSystem(tenant) is unrestricted but narrowed to one tenant, exactly like a platform admin with X-Tenant-Id.
        Assert.All(await db.Customers.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.CustomerContacts.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.Products.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.Plans.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.Subscriptions.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.SubscriptionHistory.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.Licenses.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.LicenseActivations.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.ActivationAttempts.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.ApiClients.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
        Assert.All(await db.UsageDaily.ToListAsync(), x => Assert.Equal(nourId, x.TenantId));
    }
}

/// <summary>Critical scenario 10: every admin endpoint rejects a user without the permission (403), whatever the UI shows.</summary>
public sealed class PermissionTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public PermissionTests(TestAppFixture f) => _app = f.App;

    public static TheoryData<string, string, string> Forbidden() => new()
    {
        // user, method, url
        { DemoAccounts.AlamalUser, "POST", "/api/v1/customers" },
        { DemoAccounts.AlamalUser, "POST", "/api/v1/licenses" },
        { DemoAccounts.AlamalUser, "POST", "/api/v1/subscriptions" },
        { DemoAccounts.AlamalUser, "POST", "/api/v1/products" },
        { DemoAccounts.AlamalUser, "GET", "/api/v1/users" },
        { DemoAccounts.AlamalUser, "GET", "/api/v1/audit" },
        { DemoAccounts.AlamalUser, "GET", "/api/v1/api-clients" },
        { DemoAccounts.NourOperator, "GET", "/api/v1/users" },
        { DemoAccounts.NourOperator, "POST", "/api/v1/plans" },
        { DemoAccounts.NourOperator, "GET", "/api/v1/api-clients" },
        { DemoAccounts.NourAdmin, "GET", "/api/v1/tenants" },
        { DemoAccounts.NourAdmin, "POST", "/api/v1/tenants" },
        { DemoAccounts.NourAdmin, "POST", "/api/v1/signing-keys/rotate" },
    };

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task Endpoint_returns_403_without_permission(string user, string method, string url)
    {
        var client = await _app.LoginAsync(user);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Api_client_tokens_cannot_call_portal_endpoints()
    {
        var client = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/customers")).StatusCode);
    }

    [Fact]
    public async Task Tenant_admin_creates_a_customer_user_who_sees_only_that_customer()
    {
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var sultan = await _app.WithDbAsync(db => db.Customers.Where(c => c.Name == "مطاعم السلطان").Select(c => c.Id).FirstAsync());
        var created = await nour.PostJsonAsync("/api/v1/users", new
        {
            email = "owner@sultan.test", fullName = "صاحب مطاعم السلطان", password = "Sultan123", role = "CustomerUser", customerId = sultan,
        });
        await created.OkJsonAsync(HttpStatusCode.Created);

        var user = await _app.LoginAsync("owner@sultan.test", "Sultan123");
        var licenses = await (await user.GetAsync("/api/v1/licenses")).OkJsonAsync();
        Assert.All(licenses.GetProperty("items").EnumerateArray(), l => Assert.Equal(sultan, l.GetProperty("customerId").GetGuid()));
    }

    [Fact]
    public async Task Tenant_admin_cannot_attach_a_user_to_another_tenants_customer()
    {
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var school = await _app.WithDbAsync(db => db.Customers.Where(c => c.Name == "مدارس المنار").Select(c => c.Id).FirstAsync());
        var response = await nour.PostJsonAsync("/api/v1/users", new
        {
            email = "spy@nour.test", fullName = "Spy", password = "Spy12345", role = "CustomerUser", customerId = school,
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class TestAppFixture : IDisposable
{
    public TestApp App { get; } = new();
    public void Dispose() => App.Dispose();
}
