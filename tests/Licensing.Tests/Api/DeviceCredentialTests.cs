using System.Net;
using System.Text.Json;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

/// <summary>INT-002 (ADR-001): an Agent with no API client activates with its product key and gets a device credential.</summary>
public sealed class DeviceCredentialTests : IClassFixture<TestAppFixture>
{
    private const string Header = "X-Device-Credential";
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public DeviceCredentialTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? credential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(body) };
        if (credential is not null) request.Headers.Add(Header, credential);
        return await client.SendAsync(request);
    }

    private static StringContent JsonContent(object body) => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private async Task<(JsonElement License, string Credential)> ActivateAsync(HttpClient anonymous, LicenseSetup.Issued issued, string device)
    {
        var answer = await (await anonymous.PostJsonAsync("/api/v1/licensing/device/activate",
            new { productKey = issued.Key, deviceId = device, productCode = issued.ProductCode, appVersion = "1.0.0", os = "Windows" })).OkJsonAsync();
        return (answer.GetProperty("license").Clone(), answer.GetProperty("deviceCredential").GetString()!);
    }

    [Fact]
    public async Task A_product_key_activates_without_an_API_client_and_the_credential_renews_the_license()
    {
        var issued = await _setup.IssueAsync();
        var anonymous = _app.Anonymous();

        var (license, credential) = await ActivateAsync(anonymous, issued, "CRED-DEVICE-0001");
        var check = new { licenseId = issued.LicenseId, deviceId = "CRED-DEVICE-0001", appVersion = "1.0.1" };
        var validated = await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", check, credential)).OkJsonAsync();
        var beat = await (await PostAsync(anonymous, "/api/v1/licensing/device/heartbeat", check, credential)).OkJsonAsync();

        Assert.StartsWith("lcd_", credential, StringComparison.Ordinal);
        Assert.Equal("active", license.GetProperty("status").GetString());
        Assert.Equal(issued.LicenseId, license.GetProperty("licenseId").GetGuid());
        Assert.Equal(1, license.GetProperty("activeActivations").GetInt32());
        Assert.Equal("active", validated.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(beat.GetProperty("token").GetString()));
        var stored = await _app.WithDbAsync(db => db.LicenseActivations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.LicenseId == issued.LicenseId && a.DeviceId == "CRED-DEVICE-0001"));
        Assert.NotNull(stored.CredentialHash);
        Assert.DoesNotContain(credential, stored.CredentialHash!, StringComparison.Ordinal);
        Assert.Equal("1.0.1", stored.AppVersion);
        var leaked = await _app.WithDbAsync(async db =>
            await db.AuditRecords.IgnoreQueryFilters().AnyAsync(a => a.Details != null && a.Details.Contains(credential)) ||
            await db.OutboxMessages.AnyAsync(m => m.Payload.Contains(credential)));
        Assert.False(leaked);
    }

    [Fact]
    public async Task Wrong_missing_or_rotated_credentials_all_answer_LIC_DEVICE_NOT_ACTIVATED()
    {
        var issued = await _setup.IssueAsync();
        var anonymous = _app.Anonymous();
        var (_, first) = await ActivateAsync(anonymous, issued, "CRED-DEVICE-0002");
        var (again, second) = await ActivateAsync(anonymous, issued, "CRED-DEVICE-0002");
        var check = new { licenseId = issued.LicenseId, deviceId = "CRED-DEVICE-0002" };

        Assert.True(again.GetProperty("alreadyActivated").GetBoolean());
        Assert.NotEqual(first, second);
        foreach (var credential in new[] { first, null, "lcd_" + new string('A', 43), second + "x" })
            Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", check, credential)).ErrorCodeAsync());
        var otherDevice = new { licenseId = issued.LicenseId, deviceId = "CRED-DEVICE-0099" };
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", otherDevice, second)).ErrorCodeAsync());
        var otherLicense = new { licenseId = Guid.CreateVersion7(), deviceId = "CRED-DEVICE-0002" };
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", otherLicense, second)).ErrorCodeAsync());
        await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", check, second)).OkJsonAsync();
    }

    [Fact]
    public async Task Activation_follows_the_license_rules_and_the_device_limit()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var anonymous = _app.Anonymous();
        await ActivateAsync(anonymous, issued, "CRED-LIMIT-DEV-01");

        var full = await anonymous.PostJsonAsync("/api/v1/licensing/device/activate", new { productKey = issued.Key, deviceId = "CRED-LIMIT-DEV-02" });
        var unknown = await anonymous.PostJsonAsync("/api/v1/licensing/device/activate", new { productKey = "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE", deviceId = "CRED-LIMIT-DEV-03" });
        var wrongProduct = await anonymous.PostJsonAsync("/api/v1/licensing/device/activate", new { productKey = issued.Key, deviceId = "CRED-LIMIT-DEV-01", productCode = "OTHER" });

        Assert.Equal("LIC_ACTIVATION_LIMIT_REACHED", await full.ErrorCodeAsync());
        Assert.Equal("LIC_INVALID_LICENSE", await unknown.ErrorCodeAsync());
        Assert.Equal("LIC_INVALID_LICENSE", await wrongProduct.ErrorCodeAsync());
    }

    [Fact]
    public async Task A_suspended_license_stops_the_credential_and_deactivate_frees_the_slot()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var anonymous = _app.Anonymous();
        var (_, credential) = await ActivateAsync(anonymous, issued, "CRED-SUSP-DEV-001");
        var check = new { licenseId = issued.LicenseId, deviceId = "CRED-SUSP-DEV-001" };

        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { reason = "test" })).OkJsonAsync();
        Assert.Equal("LIC_SUSPENDED", await (await PostAsync(anonymous, "/api/v1/licensing/device/heartbeat", check, credential)).ErrorCodeAsync());
        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/resume", new { })).OkJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(anonymous, "/api/v1/licensing/device/deactivate", check, credential)).StatusCode);
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await PostAsync(anonymous, "/api/v1/licensing/device/validate", check, credential)).ErrorCodeAsync());
        Assert.Equal(0, await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.Id == issued.LicenseId).Select(l => l.ActiveActivations).FirstAsync()));
        await ActivateAsync(anonymous, issued, "CRED-SUSP-DEV-002");
    }

    [Fact]
    public async Task Claim_gives_an_API_client_seat_a_credential_without_taking_a_slot()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var client = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        await (await client.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "CRED-CLAIM-DEV-01" })).OkJsonAsync();
        var anonymous = _app.Anonymous();

        var claimed = await (await anonymous.PostJsonAsync("/api/v1/licensing/device/claim", new { productKey = issued.Key, deviceId = "CRED-CLAIM-DEV-01" })).OkJsonAsync();
        var notActive = await anonymous.PostJsonAsync("/api/v1/licensing/device/claim", new { productKey = issued.Key, deviceId = "CRED-CLAIM-DEV-02" });
        var credential = claimed.GetProperty("deviceCredential").GetString()!;

        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await notActive.ErrorCodeAsync());
        Assert.Equal(1, claimed.GetProperty("license").GetProperty("activeActivations").GetInt32());
        await (await PostAsync(anonymous, "/api/v1/licensing/device/heartbeat", new { licenseId = issued.LicenseId, deviceId = "CRED-CLAIM-DEV-01" }, credential)).OkJsonAsync();
        Assert.Equal(1, await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.Id == issued.LicenseId).Select(l => l.ActiveActivations).FirstAsync()));
    }

    [Fact]
    public async Task The_existing_API_client_routes_still_work_and_their_seat_has_no_credential()
    {
        var issued = await _setup.IssueAsync();
        var client = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

        await (await client.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "CRED-LEGACY-DEV-1" })).OkJsonAsync();
        var check = new { licenseId = issued.LicenseId, deviceId = "CRED-LEGACY-DEV-1" };

        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await (await PostAsync(_app.Anonymous(), "/api/v1/licensing/device/validate", check, "lcd_" + new string('B', 43))).ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Anonymous().PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "CRED-LEGACY-DEV-1" })).StatusCode);
    }
}

public sealed class DeviceRateLimitFixture : IDisposable
{
    public TestApp App { get; } = new(new Dictionary<string, string?> { ["RateLimits:DeviceLicensingPerMinute"] = "3" });
    public void Dispose() => App.Dispose();
}

/// <summary>INT-002: the anonymous device routes are throttled per IP, so product keys cannot be brute-forced.</summary>
public sealed class DeviceCredentialRateLimitTests : IClassFixture<DeviceRateLimitFixture>
{
    private readonly TestApp _app;
    public DeviceCredentialRateLimitTests(DeviceRateLimitFixture fixture) => _app = fixture.App;

    [Fact]
    public async Task Guessing_keys_is_rate_limited()
    {
        var anonymous = _app.Anonymous();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await anonymous.PostJsonAsync("/api/v1/licensing/device/activate", new { productKey = $"GUESS{i}-BBBBBB-CCCCCC-DDDDDD-EEEEEE", deviceId = "GUESS-DEVICE-0001" })).StatusCode);

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.NotFound));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
