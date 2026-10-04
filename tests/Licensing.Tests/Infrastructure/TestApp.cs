using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Licensing.Tests.Infrastructure;

/// <summary>A clock tests can move forward (subscription expiry, lockout windows, token lifetimes).</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>
/// The real API host on an isolated database. SQLite by default; set LICENSING_TEST_SQLSERVER=1 to run the same
/// tests against the local SQL Server (database LicensingPlatform_Tests_*).
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public static readonly bool UseSqlServer = Environment.GetEnvironmentVariable("LICENSING_TEST_SQLSERVER") == "1";

    private readonly string _dbName = $"lic_test_{Guid.NewGuid():N}";
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "licensing-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _overrides;

    public TestClock Clock { get; } = new();

    public TestApp(Dictionary<string, string?>? overrides = null)
    {
        Directory.CreateDirectory(_workDir);
        _overrides = overrides ?? [];
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Provider"] = UseSqlServer ? "SqlServer" : "Sqlite",
                ["ConnectionStrings:Default"] = UseSqlServer
                    ? $"Server=.;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True"
                    : $"Data Source={Path.Combine(_workDir, "test.db")};Default Timeout=60",
                ["ConnectionStrings:Redis"] = "",
                ["Jwt:SigningKey"] = "TEST-ONLY-jwt-signing-key-0123456789abcdef0123",
                ["Security:SecretPepper"] = "TEST-ONLY-pepper",
                ["LicenseSigning:KeyStorePath"] = Path.Combine(_workDir, "keys"),
                ["DataProtection:KeyRingPath"] = Path.Combine(_workDir, "dp"),
                ["Seed:ApplyMigrations"] = "true",
                ["Seed:AdminEmail"] = TestUsers.PlatformAdmin,
                ["Seed:AdminPassword"] = TestUsers.AdminPassword,
                ["Seed:DemoData"] = "true",
                ["Outbox:Enabled"] = "false",
                ["Jobs:Enabled"] = "false",
                ["RateLimits:LoginPerMinute"] = "1000",
                ["RateLimits:ClientTokenPerMinute"] = "1000",
                ["RateLimits:LicensingPerMinute"] = "100000",
            };
            foreach (var (k, v) in _overrides) settings[k] = v;
            config.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Anonymous() => CreateClient();

    public async Task<HttpClient> LoginAsync(string email, string password = TestUsers.DemoPassword)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    public async Task<HttpClient> ClientTokenAsync(string clientId, string secret)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/client-token", new { clientId, clientSecret = secret });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Runs code against the database with the system scope (no tenant filter).</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    protected override void Dispose(bool disposing)
    {
        if (disposing && UseSqlServer)
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeleted();
            }
            catch { /* best effort */ }
        }
        base.Dispose(disposing);
        if (disposing)
            try { Directory.Delete(_workDir, true); } catch { /* sqlite file may still be held briefly */ }
    }
}

public static class TestUsers
{
    public const string PlatformAdmin = "admin@licensing.local";
    public const string AdminPassword = "Admin@12345";
    public const string DemoPassword = DemoAccounts.Password;
}

public static class HttpExtensions
{
    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestApp.Json);

    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var json = await response.JsonAsync();
        return json.TryGetProperty("code", out var code) ? code.GetString()! : "";
    }

    public static async Task<JsonElement> OkJsonAsync(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        if (response.StatusCode != expected)
            throw new Xunit.Sdk.XunitException($"Expected {(int)expected} but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, TestApp.Json);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) =>
        client.PutAsJsonAsync(url, body, TestApp.Json);
}
