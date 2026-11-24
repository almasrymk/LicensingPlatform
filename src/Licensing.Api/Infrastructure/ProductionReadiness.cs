using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Licensing.Api.Infrastructure;

/// <summary>
/// E7: settings that are fine on a developer machine but unsafe on the production server. In the Production environment
/// the API refuses to start while any <see cref="Problems"/> remain (set <c>Readiness:Enforce=false</c> only to bring a
/// broken server up long enough to fix it; it is then reported on <c>/health/ready</c>). Messages name the setting, never
/// its value.
/// </summary>
public static class ProductionReadiness
{
    public const string EnforceKey = "Readiness:Enforce";

    private static readonly string[] PlaceholderMarkers = ["DEV-ONLY", "TEST-ONLY", "CHANGE-ME", "CHANGEME", "EXAMPLE"];

    /// <summary>Settings that must be fixed before production use.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();

        if (!string.Equals(configuration["Database:Provider"] ?? "SqlServer", "SqlServer", StringComparison.OrdinalIgnoreCase))
            problems.Add("Database:Provider must be SqlServer in production.");
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
            problems.Add("ConnectionStrings:Default is empty.");

        var jwt = configuration["Jwt:SigningKey"] ?? "";
        if (Encoding.UTF8.GetByteCount(jwt) < 32) problems.Add("Jwt:SigningKey must be at least 32 bytes.");
        else if (IsPlaceholder(jwt)) problems.Add("Jwt:SigningKey is a development or example value.");

        var pepper = configuration["Security:SecretPepper"] ?? "";
        if (pepper.Length < 16) problems.Add("Security:SecretPepper must be set (at least 16 characters).");
        else if (IsPlaceholder(pepper)) problems.Add("Security:SecretPepper is a development or example value.");

        if (configuration.GetValue("Seed:DemoData", false)) problems.Add("Seed:DemoData must be false: demo accounts have published passwords.");

        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        if (origins.Any(o => o.Trim() == "*")) problems.Add("Cors:Origins must list the portal origins, not '*'.");
        if (origins.Any(o => Uri.TryCreate(o, UriKind.Absolute, out var u) && (u.IsLoopback || u.Scheme != Uri.UriSchemeHttps)))
            problems.Add("Cors:Origins must be https and not localhost.");

        if (configuration.GetValue("RateLimits:DeviceLicensingPerMinute", 30) <= 0 || configuration.GetValue("RateLimits:LoginPerMinute", 10) <= 0)
            problems.Add("RateLimits must stay on: anonymous device routes and sign-in are brute-force targets.");

        if (!configuration.GetValue("Outbox:Enabled", true)) problems.Add("Outbox:Enabled must be true, or plan changes never reach Monitor Cloud.");
        return problems;
    }

    /// <summary>Worth a look but not unsafe by themselves.</summary>
    public static IReadOnlyList<string> Warnings(IConfiguration configuration)
    {
        var warnings = new List<string>();
        if ((configuration["AllowedHosts"] ?? "*") == "*") warnings.Add("AllowedHosts is '*': list the public host names.");
        if ((configuration.GetConnectionString("Default") ?? "").Contains("TrustServerCertificate=True", StringComparison.OrdinalIgnoreCase))
            warnings.Add("ConnectionStrings:Default trusts any SQL Server certificate.");
        if (!string.IsNullOrEmpty(configuration["Seed:AdminPassword"]))
            warnings.Add("Seed:AdminPassword is set: remove it after the first sign-in.");
        return warnings;
    }

    private static bool IsPlaceholder(string value) =>
        PlaceholderMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <summary>Called once at startup. Throws in Production while problems remain, unless enforcement is turned off.</summary>
    public static void Verify(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        if (!environment.IsProduction()) return;
        foreach (var warning in Warnings(configuration)) logger.LogWarning("Production readiness: {Warning}", warning);
        var problems = Problems(configuration);
        if (problems.Count == 0) return;
        foreach (var problem in problems) logger.LogError("Production readiness: {Problem}", problem);
        if (configuration.GetValue(EnforceKey, true))
            throw new InvalidOperationException($"The Licensing API is not configured for production ({problems.Count} problem(s)): " + string.Join(" ", problems));
    }
}

/// <summary>E7: <c>/health/ready</c> is unhealthy in production while a readiness problem remains (when enforcement was turned off).</summary>
public sealed class ProductionReadinessHealthCheck(IConfiguration configuration, IHostEnvironment environment) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!environment.IsProduction()) return Task.FromResult(HealthCheckResult.Healthy("Not production."));
        var problems = ProductionReadiness.Problems(configuration);
        return Task.FromResult(problems.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"{problems.Count} production configuration problem(s); see the startup log."));
    }
}
