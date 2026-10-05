using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Licensing.Infrastructure.Messaging;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Licensing.Tests.Api;

/// <summary>Creates catalog → customer → subscription → license through the API, as a tenant admin would.</summary>
public sealed class LicenseSetup(TestApp app)
{
    public sealed record Issued(Guid LicenseId, Guid SubscriptionId, Guid PlanId, Guid ProductId, string Key, string ProductCode, HttpClient Admin);

    public async Task<Issued> IssueAsync(int? maxActivations = 3, int? durationDays = 365, int offlineGraceDays = 7, string admin = DemoAccounts.NourAdmin)
    {
        var client = await app.LoginAsync(admin);
        var code = "P" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var product = await (await client.PostJsonAsync("/api/v1/products", new { code, name = "منتج اختبار " + code })).OkJsonAsync(HttpStatusCode.Created);
        var productId = product.GetProperty("id").GetGuid();

        var plan = await (await client.PostJsonAsync("/api/v1/plans", new
        {
            productId, code = "STD", name = "قياسي", price = 100, currency = "EGP", durationDays, maxActivations,
            heartbeatIntervalHours = 24, offlineGraceDays, features = new[] { "reports", "export" },
        })).OkJsonAsync(HttpStatusCode.Created);
        var planId = plan.GetProperty("id").GetGuid();
        await (await client.PostAsync($"/api/v1/plans/{planId}/publish", null)).OkJsonAsync();

        var customer = await (await client.PostJsonAsync("/api/v1/customers", new { name = "عميل " + code })).OkJsonAsync(HttpStatusCode.Created);
        var customerId = customer.GetProperty("customer").GetProperty("id").GetGuid();

        var sub = await (await client.PostJsonAsync("/api/v1/subscriptions", new { customerId, planId })).OkJsonAsync(HttpStatusCode.Created);
        var subId = sub.GetProperty("subscription").GetProperty("id").GetGuid();

        var license = await (await client.PostJsonAsync("/api/v1/licenses", new { subscriptionId = subId })).OkJsonAsync(HttpStatusCode.Created);
        return new Issued(license.GetProperty("license").GetProperty("id").GetGuid(), subId, planId, productId,
            license.GetProperty("productKey").GetString()!, code, client);
    }
}

public sealed class LicensingFlowTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public LicensingFlowTests(TestAppFixture f)
    {
        _app = f.App;
        _setup = new LicenseSetup(_app);
    }

    private Task<HttpClient> Device() => _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);

    [Fact]
    public async Task Product_key_is_shown_once_and_only_its_hash_is_stored()
    {
        var issued = await _setup.IssueAsync();
        Assert.Matches("^[A-Z2-9]{6}(-[A-Z2-9]{6}){4}$", issued.Key);

        var details = await (await issued.Admin.GetAsync($"/api/v1/licenses/{issued.LicenseId}")).OkJsonAsync();
        Assert.DoesNotContain(issued.Key, details.GetRawText());
        Assert.False(details.GetProperty("license").TryGetProperty("productKey", out _));

        var normalized = issued.Key.Replace("-", "");
        var leaked = await _app.WithDbAsync(async db =>
            await db.Licenses.AnyAsync(l => l.ProductKeyHash.Contains(normalized) || l.LicenseNumber.Contains(normalized)) ||
            await db.AuditRecords.AnyAsync(a => a.Details != null && (a.Details.Contains(issued.Key) || a.Details.Contains(normalized))) ||
            await db.OutboxMessages.AnyAsync(m => m.Payload.Contains(issued.Key) || m.Payload.Contains(normalized)));
        Assert.False(leaked);
    }

    [Fact]
    public async Task Activate_validate_heartbeat_deactivate_happy_path()
    {
        var issued = await _setup.IssueAsync();
        var device = await Device();

        var activated = await (await device.PostJsonAsync("/api/v1/licensing/activate", new
        {
            productKey = issued.Key, deviceId = "TEST-DEVICE-0001", deviceName = "جهاز اختبار", productCode = issued.ProductCode,
        })).OkJsonAsync();
        Assert.Equal("active", activated.GetProperty("status").GetString());
        Assert.Equal(1, activated.GetProperty("activeActivations").GetInt32());
        Assert.Contains("reports", activated.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.True(activated.TryGetProperty("expiresAt", out _));

        var validated = await (await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "TEST-DEVICE-0001" })).OkJsonAsync();
        Assert.Equal("active", validated.GetProperty("status").GetString());

        var beat = await (await device.PostJsonAsync("/api/v1/licensing/heartbeat", new { productKey = issued.Key, deviceId = "TEST-DEVICE-0001", appVersion = "3.0" })).OkJsonAsync();
        Assert.True(beat.GetProperty("checkAfter").GetDateTimeOffset() > _app.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.NoContent, (await device.PostJsonAsync("/api/v1/licensing/deactivate", new { productKey = issued.Key, deviceId = "TEST-DEVICE-0001" })).StatusCode);
        var after = await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "TEST-DEVICE-0001" });
        Assert.Equal("LIC_DEVICE_NOT_ACTIVATED", await after.ErrorCodeAsync());

        var count = await _app.WithDbAsync(db => db.Licenses.Where(l => l.Id == issued.LicenseId).Select(l => l.ActiveActivations).FirstAsync());
        Assert.Equal(0, count);
    }

    /// <summary>Critical scenario 2: 50 concurrent activations of a license with MaxActivations 5 produce exactly 5.</summary>
    [Fact]
    public async Task Concurrent_activations_never_exceed_the_limit()
    {
        var issued = await _setup.IssueAsync(maxActivations: 5);
        var device = await Device();

        var tasks = Enumerable.Range(0, 50).Select(i => device.PostJsonAsync("/api/v1/licensing/activate",
            new { productKey = issued.Key, deviceId = $"PARALLEL-DEVICE-{i:D3}" }));
        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(45, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        foreach (var r in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            Assert.Equal("LIC_ACTIVATION_LIMIT_REACHED", await r.ErrorCodeAsync());

        var (counter, rows) = await _app.WithDbAsync(async db => (
            await db.Licenses.Where(l => l.Id == issued.LicenseId).Select(l => l.ActiveActivations).FirstAsync(),
            await db.LicenseActivations.CountAsync(a => a.LicenseId == issued.LicenseId)));
        Assert.Equal(5, counter);
        Assert.Equal(5, rows);
    }

    [Fact]
    public async Task Same_device_activating_again_is_an_idempotent_success()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var device = await Device();
        var body = new { productKey = issued.Key, deviceId = "SAME-DEVICE-0001" };

        await (await device.PostJsonAsync("/api/v1/licensing/activate", body)).OkJsonAsync();
        var again = await (await device.PostJsonAsync("/api/v1/licensing/activate", body)).OkJsonAsync();
        Assert.True(again.GetProperty("alreadyActivated").GetBoolean());
        Assert.Equal(1, again.GetProperty("activeActivations").GetInt32());
    }

    /// <summary>Critical scenario 3: same Idempotency-Key returns the same result without a new activation.</summary>
    [Fact]
    public async Task Idempotency_key_replays_the_first_response()
    {
        var issued = await _setup.IssueAsync(maxActivations: 3);
        var device = await Device();

        async Task<HttpResponseMessage> Send(string deviceId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/licensing/activate")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { productKey = issued.Key, deviceId }),
            };
            request.Headers.Add("Idempotency-Key", "retry-key-123");
            return await device.SendAsync(request);
        }

        var first = await Send("IDEMP-DEVICE-001");
        var firstBody = await first.Content.ReadAsStringAsync();
        // A retry (even with a different body) returns the stored response and creates nothing.
        var second = await Send("IDEMP-DEVICE-002");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());

        var rows = await _app.WithDbAsync(db => db.LicenseActivations.CountAsync(a => a.LicenseId == issued.LicenseId));
        Assert.Equal(1, rows);
    }

    /// <summary>Critical scenario 4: a revoked license fails its next validate even if the result was cached.</summary>
    [Fact]
    public async Task Revoked_license_is_rejected_immediately_even_when_cached()
    {
        var issued = await _setup.IssueAsync();
        var device = await Device();
        var body = new { productKey = issued.Key, deviceId = "CACHED-DEVICE-01" };
        await (await device.PostJsonAsync("/api/v1/licensing/activate", body)).OkJsonAsync();
        await (await device.PostJsonAsync("/api/v1/licensing/validate", body)).OkJsonAsync(); // now cached

        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/revoke", new { reason = "test" })).OkJsonAsync();

        var validate = await device.PostJsonAsync("/api/v1/licensing/validate", body);
        Assert.Equal(HttpStatusCode.Forbidden, validate.StatusCode);
        Assert.Equal("LIC_REVOKED", await validate.ErrorCodeAsync());
        var heartbeat = await device.PostJsonAsync("/api/v1/licensing/heartbeat", body);
        Assert.Equal("LIC_REVOKED", await heartbeat.ErrorCodeAsync());
    }

    [Fact]
    public async Task Suspended_license_is_rejected_and_resume_restores_it()
    {
        var issued = await _setup.IssueAsync();
        var device = await Device();
        var body = new { productKey = issued.Key, deviceId = "SUSPEND-DEVICE-1" };
        await (await device.PostJsonAsync("/api/v1/licensing/activate", body)).OkJsonAsync();

        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/suspend", new { reason = "x" })).OkJsonAsync();
        Assert.Equal("LIC_SUSPENDED", await (await device.PostJsonAsync("/api/v1/licensing/validate", body)).ErrorCodeAsync());

        await (await issued.Admin.PostAsync($"/api/v1/licenses/{issued.LicenseId}/resume", null)).OkJsonAsync();
        await (await device.PostJsonAsync("/api/v1/licensing/validate", body)).OkJsonAsync();
    }

    /// <summary>Critical scenario 5: when the subscription expires, validate returns LIC_SUBSCRIPTION_EXPIRED (via job + outbox event).</summary>
    [Fact]
    public async Task Subscription_expiry_stops_validation()
    {
        var issued = await _setup.IssueAsync(durationDays: 30);
        var device = await Device();
        var body = new { productKey = issued.Key, deviceId = "EXPIRY-DEVICE-01" };
        await (await device.PostJsonAsync("/api/v1/licensing/activate", body)).OkJsonAsync();

        _app.Clock.Advance(TimeSpan.FromDays(31));
        try
        {
            var jobs = _app.Get<Licensing.Infrastructure.Jobs.JobRunner>();
            Assert.True(await jobs.ExpireSubscriptionsAsync(default) >= 1);
            while (await _app.Get<OutboxProcessor>().ProcessBatchAsync(default) > 0) { }

            var validate = await device.PostJsonAsync("/api/v1/licensing/validate", body);
            Assert.Equal(HttpStatusCode.Forbidden, validate.StatusCode);
            Assert.Equal("LIC_SUBSCRIPTION_EXPIRED", await validate.ErrorCodeAsync());

            var status = await _app.WithDbAsync(db => db.Licenses.Where(l => l.Id == issued.LicenseId).Select(l => l.Status).FirstAsync());
            Assert.Equal(Domain.Licensing.LicenseStatus.Expired, status);

            // Renewing the subscription brings the license back.
            await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/renew", new { })).OkJsonAsync();
            await (await device.PostJsonAsync("/api/v1/licensing/validate", body)).OkJsonAsync();
        }
        finally
        {
            _app.Clock.Advance(TimeSpan.FromDays(-31));
        }
    }

    /// <summary>Critical scenario 6: each rule returns the documented code and status.</summary>
    [Fact]
    public async Task Each_rule_returns_its_code_and_status()
    {
        var device = await Device();
        var issued = await _setup.IssueAsync(maxActivations: 1);

        async Task Expect(object body, string url, HttpStatusCode status, string code)
        {
            var r = await device.PostJsonAsync(url, body);
            Assert.Equal(status, r.StatusCode);
            Assert.Equal(code, await r.ErrorCodeAsync());
        }

        await Expect(new { productKey = "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE", deviceId = "RULE-DEVICE-001" }, "/api/v1/licensing/activate", HttpStatusCode.NotFound, "LIC_INVALID_LICENSE");
        await Expect(new { productKey = issued.Key, deviceId = "bad id" }, "/api/v1/licensing/activate", HttpStatusCode.BadRequest, "LIC_INVALID_DEVICE");
        await Expect(new { productKey = issued.Key, deviceId = "RULE-DEVICE-001", productCode = "OTHER" }, "/api/v1/licensing/activate", HttpStatusCode.NotFound, "LIC_INVALID_LICENSE");
        await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "RULE-DEVICE-001" })).OkJsonAsync();
        await Expect(new { productKey = issued.Key, deviceId = "RULE-DEVICE-002" }, "/api/v1/licensing/activate", HttpStatusCode.Conflict, "LIC_ACTIVATION_LIMIT_REACHED");
        await Expect(new { productKey = issued.Key, deviceId = "RULE-DEVICE-002" }, "/api/v1/licensing/validate", HttpStatusCode.NotFound, "LIC_DEVICE_NOT_ACTIVATED");

        await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/suspend", new { reason = "x" })).OkJsonAsync();
        await Expect(new { productKey = issued.Key, deviceId = "RULE-DEVICE-001" }, "/api/v1/licensing/validate", HttpStatusCode.Forbidden, "LIC_SUBSCRIPTION_INACTIVE");
        await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/resume", new { })).OkJsonAsync();

        // A license of another tenant looks exactly like a non-existent one.
        var ofoqDevice = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);
        var cross = await ofoqDevice.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "RULE-DEVICE-001" });
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Equal("LIC_INVALID_LICENSE", await cross.ErrorCodeAsync());

        // Failed attempts are recorded for the report.
        var failed = await _app.WithDbAsync(db => db.ActivationAttempts.CountAsync(a => a.LicenseId == issued.LicenseId && !a.Success));
        Assert.True(failed >= 2);
    }

    [Fact]
    public async Task Reset_device_from_the_portal_frees_the_slot()
    {
        var issued = await _setup.IssueAsync(maxActivations: 1);
        var device = await Device();
        await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "OLD-MACHINE-0001" })).OkJsonAsync();

        var details = await (await issued.Admin.GetAsync($"/api/v1/licenses/{issued.LicenseId}")).OkJsonAsync();
        var activationId = details.GetProperty("activations")[0].GetProperty("id").GetGuid();
        await (await issued.Admin.PostAsync($"/api/v1/licenses/{issued.LicenseId}/activations/{activationId}/reset", null)).OkJsonAsync();

        await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "NEW-MACHINE-0001" })).OkJsonAsync();
    }

    [Fact]
    public async Task Sensitive_actions_are_audited()
    {
        var issued = await _setup.IssueAsync();
        await (await issued.Admin.PostJsonAsync($"/api/v1/licenses/{issued.LicenseId}/revoke", new { reason = "audit me" })).OkJsonAsync();
        var actions = await _app.WithDbAsync(db => db.AuditRecords.Where(a => a.EntityId == issued.LicenseId.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("license.issued", actions);
        Assert.Contains("license.revoked", actions);

        var audit = await (await issued.Admin.GetAsync("/api/v1/audit?action=license.")).OkJsonAsync();
        Assert.True(audit.GetProperty("total").GetInt32() >= 2);
    }
}

public sealed class IntegrationsTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public IntegrationsTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task Client_secret_is_shown_once_rotates_and_old_secret_stops_working()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var created = await (await admin.PostJsonAsync("/api/v1/api-clients", new { name = "CI client", scopes = new[] { "licenses.validate" } }))
            .OkJsonAsync(HttpStatusCode.Created);
        var clientId = created.GetProperty("client").GetProperty("clientId").GetString()!;
        var secret = created.GetProperty("clientSecret").GetString()!;
        var id = created.GetProperty("client").GetProperty("id").GetGuid();

        var list = await (await admin.GetAsync("/api/v1/api-clients")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, list);

        var token = await _app.Anonymous().PostJsonAsync("/api/v1/auth/client-token", new { clientId, clientSecret = secret });
        var tokenBody = await token.OkJsonAsync();
        Assert.Equal("licenses.validate", tokenBody.GetProperty("scope").GetString());

        var rotated = await (await admin.PostAsync($"/api/v1/api-clients/{id}/rotate-secret", null)).OkJsonAsync();
        var newSecret = rotated.GetProperty("clientSecret").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Anonymous().PostJsonAsync("/api/v1/auth/client-token", new { clientId, clientSecret = secret })).StatusCode);
        await (await _app.Anonymous().PostJsonAsync("/api/v1/auth/client-token", new { clientId, clientSecret = newSecret })).OkJsonAsync();

        await (await admin.PostAsync($"/api/v1/api-clients/{id}/disable", null)).OkJsonAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Anonymous().PostJsonAsync("/api/v1/auth/client-token", new { clientId, clientSecret = newSecret })).StatusCode);
    }

    [Fact]
    public async Task Insufficient_scope_returns_AUTH_FORBIDDEN()
    {
        var device = await _app.ClientTokenAsync(DemoAccounts.OfoqClientId, DemoAccounts.OfoqClientSecret);
        var admin = await _app.LoginAsync(DemoAccounts.OfoqAdmin);
        var created = await (await admin.PostJsonAsync("/api/v1/api-clients", new { name = "validate only", scopes = new[] { "licenses.validate" } }))
            .OkJsonAsync(HttpStatusCode.Created);
        var limited = await _app.ClientTokenAsync(created.GetProperty("client").GetProperty("clientId").GetString()!, created.GetProperty("clientSecret").GetString()!);

        var response = await limited.PostJsonAsync("/api/v1/licensing/activate", new { productKey = DemoAccounts.SchoolKey, deviceId = "SCOPE-DEVICE-001" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", await response.ErrorCodeAsync());

        // The demo client of the same tenant has the scope and works with the demo key.
        await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = DemoAccounts.SchoolKey, deviceId = "SCOPE-DEVICE-001" })).OkJsonAsync();
    }

    [Fact]
    public async Task Webhook_endpoints_are_registered_but_require_https()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var bad = await admin.PostJsonAsync("/api/v1/webhooks", new { url = "http://example.test/hook", events = new[] { "license.revoked" } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await (await admin.PostJsonAsync("/api/v1/webhooks", new { url = "https://example.test/hook", events = new[] { "license.revoked" } }))
            .OkJsonAsync(HttpStatusCode.Created);
    }
}

public sealed class RateLimitTests : IDisposable
{
    private readonly TestApp _app = new(new() { ["RateLimits:LoginPerMinute"] = "3", ["RateLimits:LicensingPerMinute"] = "5" });

    [Fact]
    public async Task Login_over_the_limit_returns_429_with_code()
    {
        var client = _app.Anonymous();
        HttpResponseMessage last = null!;
        for (var i = 0; i < 4; i++)
            last = await client.PostJsonAsync("/api/v1/auth/login", new { email = "x@y.test", password = "Wrong123" });
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal("RATE_LIMITED", await last.ErrorCodeAsync());
    }

    [Fact]
    public async Task Licensing_calls_are_limited_per_client()
    {
        var device = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
            statuses.Add((await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = DemoAccounts.AlamalProKey, deviceId = "ALAMAL-PC-0001" })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    public void Dispose() => _app.Dispose();
}

/// <summary>Phase 5: signed license tokens verify offline, tampering is detected, and key rotation keeps old tokens valid.</summary>
public sealed class OfflineTokenTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public OfflineTokenTests(TestAppFixture f) => _app = f.App;

    private async Task<Dictionary<string, ECDsaSecurityKey>> PublicKeysAsync()
    {
        var body = await (await _app.Anonymous().GetAsync("/api/v1/signing-keys")).OkJsonAsync();
        return body.GetProperty("pem").EnumerateArray().ToDictionary(
            k => k.GetProperty("kid").GetString()!,
            k =>
            {
                var ec = ECDsa.Create();
                ec.ImportFromPem(k.GetProperty("publicKeyPem").GetString());
                return new ECDsaSecurityKey(ec) { KeyId = k.GetProperty("kid").GetString() };
            });
    }

    private static bool VerifyOffline(string token, IReadOnlyDictionary<string, ECDsaSecurityKey> keys, DateTime now, out JwtSecurityToken? parsed)
    {
        parsed = null;
        try
        {
            new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "licensing-platform",
                ValidateAudience = false,
                IssuerSigningKeyResolver = (_, _, kid, _) => keys.TryGetValue(kid, out var k) ? [k] : [],
                ValidAlgorithms = ["ES256"],
                LifetimeValidator = (nb, exp, _, _) => exp > now,
            }, out var validated);
            parsed = (JwtSecurityToken)validated;
            return true;
        }
        catch (SecurityTokenException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Token_verifies_offline_rejects_tampering_and_survives_rotation()
    {
        var device = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        var issued = await new LicenseSetup(_app).IssueAsync(offlineGraceDays: 7);
        var activated = await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "OFFLINE-DEVICE-01" })).OkJsonAsync();
        var token = activated.GetProperty("token").GetString()!;
        var keys = await PublicKeysAsync();

        Assert.True(VerifyOffline(token, keys, DateTime.UtcNow, out var parsed));
        Assert.Equal("OFFLINE-DEVICE-01", parsed!.Subject);
        Assert.Equal(issued.ProductCode, parsed.Claims.First(c => c.Type == "product").Value);
        Assert.Contains(parsed.Claims, c => c.Type == "check_after");

        // Inside the offline window it verifies; after the window it is rejected.
        Assert.True(VerifyOffline(token, keys, DateTime.UtcNow.AddDays(7), out _));
        Assert.False(VerifyOffline(token, keys, DateTime.UtcNow.AddDays(9), out _));

        // Tampering with the payload breaks the signature.
        var parts = token.Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(Base64UrlEncoder.Decode(parts[1]))!;
        payload["features"] = new[] { "everything" };
        var forged = $"{parts[0]}.{Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload))}.{parts[2]}";
        Assert.False(VerifyOffline(forged, keys, DateTime.UtcNow, out _));

        // Rotation: the old token still verifies with the published (retiring) key; new tokens use the new kid.
        var admin = await _app.LoginAsync(TestUsers.PlatformAdmin, TestUsers.AdminPassword);
        var newKid = (await (await admin.PostAsync("/api/v1/signing-keys/rotate", null)).OkJsonAsync()).GetProperty("kid").GetString();
        var keysAfter = await PublicKeysAsync();
        Assert.True(VerifyOffline(token, keysAfter, DateTime.UtcNow, out _));
        var fresh = await (await device.PostJsonAsync("/api/v1/licensing/heartbeat", new { productKey = issued.Key, deviceId = "OFFLINE-DEVICE-01" })).OkJsonAsync();
        Assert.Equal(newKid, fresh.GetProperty("kid").GetString());
        Assert.True(VerifyOffline(fresh.GetProperty("token").GetString()!, keysAfter, DateTime.UtcNow, out _));
    }
}

/// <summary>A database restored on another server names a signing key whose private file is not there: signing must recover.</summary>
public sealed class MissingSigningKeyTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public MissingSigningKeyTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task Activation_rotates_to_a_new_key_when_the_active_private_key_is_missing()
    {
        // An active key whose private half exists nowhere on this server (as after restoring a backup elsewhere).
        using var orphan = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string orphanKid = "lk-restored-elsewhere";
        await _app.WithDbAsync(async db =>
        {
            db.SigningKeys.Add(Licensing.Domain.Licensing.SigningKey.Create(orphanKid, "ES256", orphan.ExportSubjectPublicKeyInfoPem(), DateTimeOffset.UtcNow.AddMinutes(5)));
            await db.SaveChangesAsync();
            return 0;
        });

        var device = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        var issued = await new LicenseSetup(_app).IssueAsync();
        var activated = await (await device.PostJsonAsync("/api/v1/licensing/activate", new { productKey = issued.Key, deviceId = "RESTORED-DB-DEVICE" })).OkJsonAsync();

        var kid = activated.GetProperty("kid").GetString();
        Assert.NotEqual(orphanKid, kid);
        var validated = await (await device.PostJsonAsync("/api/v1/licensing/validate", new { productKey = issued.Key, deviceId = "RESTORED-DB-DEVICE" })).OkJsonAsync();
        Assert.Equal(kid, validated.GetProperty("kid").GetString());
    }
}

/// <summary>Critical scenario 8: an outbox event is published after a worker restart and never consumed twice.</summary>
public sealed class OutboxTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public OutboxTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task Events_are_stored_with_the_change_and_processed_exactly_once()
    {
        var issued = await new LicenseSetup(_app).IssueAsync();
        await (await issued.Admin.PostJsonAsync($"/api/v1/subscriptions/{issued.SubscriptionId}/cancel", new { reason = "x" })).OkJsonAsync();

        var pending = await _app.WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Type == "SubscriptionCancelledV1" && m.ProcessedAt == null));
        Assert.True(pending >= 1);

        // "Worker restart": a brand-new processor instance picks up the stored events.
        var processor = _app.Get<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(default) > 0) { }

        var status = await _app.WithDbAsync(db => db.Licenses.Where(l => l.Id == issued.LicenseId).Select(l => l.Status).FirstAsync());
        Assert.Equal(Domain.Licensing.LicenseStatus.Expired, status);

        // Re-delivering the same message does not run the handler again (inbox).
        var message = await _app.WithDbAsync(db => db.OutboxMessages.FirstAsync(m => m.Type == "SubscriptionCancelledV1"));
        await _app.WithDbAsync(async db =>
        {
            var m = await db.OutboxMessages.FirstAsync(x => x.Id == message.Id);
            m.ProcessedAt = null;
            return await db.SaveChangesAsync();
        });
        var inboxBefore = await _app.WithDbAsync(db => db.InboxMessages.CountAsync(i => i.MessageId == message.Id));
        await processor.ProcessBatchAsync(default);
        var inboxAfter = await _app.WithDbAsync(db => db.InboxMessages.CountAsync(i => i.MessageId == message.Id));
        Assert.Equal(inboxBefore, inboxAfter);
        Assert.Equal(0, await _app.WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && !m.DeadLettered)));
    }
}
