// Reference client (plan Phase 5): simulates the licensing agent embedded in a customer's software.
//
//   license-agent activate  --key XXXXXX-... [--device ID]
//   license-agent check                      (offline verification of the cached token, no network)
//   license-agent heartbeat                  (online check-in; refreshes the token)
//   license-agent run [--minutes N]          (loop: verify offline, heartbeat when check_after passes, with jitter)
//   license-agent deactivate
//
// Settings: --api http://localhost:5200 --client-id lc_demo_nour --client-secret ... --state ./agent-state.json
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

var options = Args.Parse(args);
var agent = new Agent(options);
return await agent.RunAsync();

sealed record AgentState(string ProductKey, string DeviceId, string Token, Dictionary<string, string> PublicKeys);

sealed class Args
{
    public string Command { get; private init; } = "help";
    public string Api { get; private init; } = "http://localhost:5200";
    public string ClientId { get; private init; } = "lc_demo_nour";
    public string ClientSecret { get; private init; } = "lcs_demo_nour_secret_change_me";
    public string StatePath { get; private init; } = "agent-state.json";
    public string? Key { get; private init; }
    public string Device { get; private init; } = "AGENT-" + Environment.MachineName.ToUpperInvariant();
    public int Minutes { get; private init; } = 5;

    public static Args Parse(string[] a)
    {
        string? Get(string name) { var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
        return new Args
        {
            Command = a.Length > 0 && !a[0].StartsWith("--") ? a[0] : "help",
            Api = Get("--api") ?? "http://localhost:5200",
            ClientId = Get("--client-id") ?? "lc_demo_nour",
            ClientSecret = Get("--client-secret") ?? "lcs_demo_nour_secret_change_me",
            StatePath = Get("--state") ?? "agent-state.json",
            Key = Get("--key"),
            Device = Get("--device") ?? "AGENT-" + new string(Environment.MachineName.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant().PadRight(8, '0'),
            Minutes = int.TryParse(Get("--minutes"), out var m) ? m : 5,
        };
    }
}

sealed class Agent(Args o)
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(o.Api), Timeout = TimeSpan.FromSeconds(15) };

    public async Task<int> RunAsync() => o.Command switch
    {
        "activate" => await ActivateAsync(),
        "check" => Check(),
        "heartbeat" => await HeartbeatAsync(),
        "run" => await LoopAsync(),
        "deactivate" => await DeactivateAsync(),
        _ => Help(),
    };

    private static int Help()
    {
        Console.WriteLine("license-agent activate --key KEY [--device ID] | check | heartbeat | run [--minutes N] | deactivate");
        return 1;
    }

    private async Task AuthenticateAsync()
    {
        var response = await _http.PostAsJsonAsync("/api/v1/auth/client-token", new { clientId = o.ClientId, clientSecret = o.ClientSecret });
        var body = await Read(response);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
    }

    private async Task<Dictionary<string, string>> FetchPublicKeysAsync()
    {
        var body = await Read(await _http.GetAsync("/api/v1/signing-keys"));
        return body.GetProperty("pem").EnumerateArray()
            .ToDictionary(k => k.GetProperty("kid").GetString()!, k => k.GetProperty("publicKeyPem").GetString()!);
    }

    private async Task<int> ActivateAsync()
    {
        if (o.Key is null) return Help();
        await AuthenticateAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/licensing/activate")
        {
            Content = JsonContent.Create(new { productKey = o.Key, deviceId = o.Device, deviceName = Environment.MachineName, appVersion = "1.0.0" }),
        };
        // Same key on retries: a timeout followed by a retry never consumes a second activation.
        request.Headers.Add("Idempotency-Key", $"activate-{o.Device}-{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(o.Key)))[..16]}");
        var body = await Read(await _http.SendAsync(request));
        var state = new AgentState(o.Key, o.Device, body.GetProperty("token").GetString()!, await FetchPublicKeysAsync());
        Save(state);
        Console.WriteLine($"Activated {body.GetProperty("licenseNumber")} on {o.Device}; features: {string.Join(", ", body.GetProperty("features").EnumerateArray())}");
        return Check();
    }

    /// <summary>Offline verification: signature (by kid), issuer, device binding and the offline window. No network.</summary>
    private int Check()
    {
        var state = Load();
        if (state is null) { Console.WriteLine("Not activated."); return 2; }
        var (ok, message, _) = Verify(state);
        Console.WriteLine(ok ? $"VALID (offline): {message}" : $"INVALID: {message}");
        return ok ? 0 : 3;
    }

    private static (bool Ok, string Message, DateTimeOffset CheckAfter) Verify(AgentState state)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(state.Token, new TokenValidationParameters
            {
                ValidIssuer = "licensing-platform",
                ValidateAudience = false,
                ValidAlgorithms = ["ES256"],
                ClockSkew = TimeSpan.FromMinutes(5),
                IssuerSigningKeyResolver = (_, _, kid, _) =>
                {
                    if (!state.PublicKeys.TryGetValue(kid, out var pem)) return [];
                    var ec = ECDsa.Create();
                    ec.ImportFromPem(pem);
                    return [new ECDsaSecurityKey(ec) { KeyId = kid }];
                },
            }, out var validated);
            var jwt = (JwtSecurityToken)validated;
            if (jwt.Subject != state.DeviceId) return (false, "token belongs to another device", default);
            var checkAfter = DateTimeOffset.FromUnixTimeSeconds(long.Parse(jwt.Claims.First(c => c.Type == "check_after").Value));
            return (true, $"{jwt.Claims.First(c => c.Type == "lic").Value}, offline until {jwt.ValidTo:u}, next check after {checkAfter:u}", checkAfter);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return (false, ex.GetType().Name.Replace("SecurityToken", "").Replace("Exception", ""), default);
        }
    }

    private async Task<int> HeartbeatAsync()
    {
        var state = Load();
        if (state is null) { Console.WriteLine("Not activated."); return 2; }
        await AuthenticateAsync();
        var response = await _http.PostAsJsonAsync("/api/v1/licensing/heartbeat", new { productKey = state.ProductKey, deviceId = state.DeviceId, appVersion = "1.0.0" });
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine($"Rejected by server: {problem.GetProperty("code")}. Removing local license.");
            File.Delete(o.StatePath); // a revoked or expired license stops working at the first contact
            return 3;
        }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Save(state with { Token = body.GetProperty("token").GetString()!, PublicKeys = await FetchPublicKeysAsync() });
        Console.WriteLine($"Heartbeat OK; next check after {body.GetProperty("checkAfter")}");
        return 0;
    }

    private async Task<int> LoopAsync()
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(o.Minutes);
        while (DateTimeOffset.UtcNow < until)
        {
            var state = Load();
            if (state is null) { Console.WriteLine("Not activated."); return 2; }
            var (ok, message, checkAfter) = Verify(state);
            Console.WriteLine($"[{DateTimeOffset.Now:T}] {(ok ? "valid" : "INVALID")}: {message}");
            if (!ok || DateTimeOffset.UtcNow >= checkAfter)
            {
                try { if (await HeartbeatAsync() == 3) return 3; }
                catch (HttpRequestException) { Console.WriteLine("Server unreachable; continuing offline while the token is valid."); }
            }
            await Task.Delay(TimeSpan.FromSeconds(20 + Random.Shared.Next(0, 10)));
        }
        return 0;
    }

    private async Task<int> DeactivateAsync()
    {
        var state = Load();
        if (state is null) { Console.WriteLine("Not activated."); return 2; }
        await AuthenticateAsync();
        var response = await _http.PostAsJsonAsync("/api/v1/licensing/deactivate", new { productKey = state.ProductKey, deviceId = state.DeviceId });
        File.Delete(o.StatePath);
        Console.WriteLine(response.IsSuccessStatusCode ? "Deactivated; the activation slot is free." : $"Server said {(int)response.StatusCode}; local state removed.");
        return 0;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {body.GetProperty("code")}: {body.GetProperty("title")}");
        return body;
    }

    private void Save(AgentState state) => File.WriteAllText(o.StatePath, JsonSerializer.Serialize(state));
    private AgentState? Load() => File.Exists(o.StatePath) ? JsonSerializer.Deserialize<AgentState>(File.ReadAllText(o.StatePath)) : null;
}
