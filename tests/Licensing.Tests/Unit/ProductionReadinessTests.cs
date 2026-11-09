using Licensing.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Licensing.Tests.Unit;

/// <summary>E7: production refuses development settings, and the messages never contain the secret values.</summary>
public sealed class ProductionReadinessTests
{
    // Sample values only: obviously not real secrets, but long and free of placeholder markers.
    private static Dictionary<string, string?> Good() => new()
    {
        ["Database:Provider"] = "SqlServer",
        ["ConnectionStrings:Default"] = "Server=sql.internal.test;Database=Licensing;Encrypt=True",
        ["Jwt:SigningKey"] = new string('k', 48),
        ["Security:SecretPepper"] = new string('p', 24),
        ["Seed:DemoData"] = "false",
        ["Cors:Origins:0"] = "https://licensing.example.test",
        ["AllowedHosts"] = "licensing.example.test",
    };

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Licensing.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void A_complete_production_configuration_has_no_problems_or_warnings()
    {
        Assert.Empty(ProductionReadiness.Problems(Config(Good())));
        Assert.Empty(ProductionReadiness.Warnings(Config(Good())));
        ProductionReadiness.Verify(Config(Good()), new Env("Production"), NullLogger.Instance);
    }

    [Theory]
    [InlineData("Jwt:SigningKey", "DEV-ONLY-jwt-signing-key-change-me-0123456789abcdef", "Jwt:SigningKey")]
    [InlineData("Jwt:SigningKey", "short", "Jwt:SigningKey")]
    [InlineData("Security:SecretPepper", "DEV-ONLY-pepper-change-me", "Security:SecretPepper")]
    [InlineData("Security:SecretPepper", "", "Security:SecretPepper")]
    [InlineData("Seed:DemoData", "true", "Seed:DemoData")]
    [InlineData("Database:Provider", "Sqlite", "Database:Provider")]
    [InlineData("Cors:Origins:0", "http://localhost:4200", "Cors:Origins")]
    [InlineData("Cors:Origins:0", "*", "Cors:Origins")]
    [InlineData("RateLimits:DeviceLicensingPerMinute", "0", "RateLimits")]
    [InlineData("Outbox:Enabled", "false", "Outbox:Enabled")]
    public void Each_unsafe_setting_is_named_without_its_value(string key, string value, string named)
    {
        var settings = Good();
        settings[key] = value;

        var problem = Assert.Single(ProductionReadiness.Problems(Config(settings)));

        Assert.Contains(named, problem, StringComparison.Ordinal);
        if (value.Length > 4) Assert.DoesNotContain(value, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_refuses_to_start_with_the_development_settings_unless_enforcement_is_off()
    {
        var settings = Good();
        settings["Jwt:SigningKey"] = "DEV-ONLY-jwt-signing-key-change-me-0123456789abcdef";

        var error = Assert.Throws<InvalidOperationException>(() => ProductionReadiness.Verify(Config(settings), new Env("Production"), NullLogger.Instance));
        Assert.DoesNotContain("DEV-ONLY-jwt", error.Message, StringComparison.Ordinal);

        settings[ProductionReadiness.EnforceKey] = "false";
        ProductionReadiness.Verify(Config(settings), new Env("Production"), NullLogger.Instance);
        ProductionReadiness.Verify(Config(new Dictionary<string, string?>()), new Env("Development"), NullLogger.Instance);
    }

    [Fact]
    public async Task The_readiness_probe_reports_problems_in_production_only()
    {
        var bad = Config(new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

        var production = await new ProductionReadinessHealthCheck(bad, new Env("Production")).CheckHealthAsync(new());
        var development = await new ProductionReadinessHealthCheck(bad, new Env("Development")).CheckHealthAsync(new());

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, production.Status);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, development.Status);
    }
}
