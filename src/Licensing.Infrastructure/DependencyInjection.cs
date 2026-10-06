using Licensing.Application.Abstractions;
using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Jobs;
using Licensing.Infrastructure.Messaging;
using Licensing.Infrastructure.Persistence;
using Licensing.Infrastructure.Security;
using Licensing.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);

        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<LicenseSigningOptions>(config.GetSection(LicenseSigningOptions.Section));
        services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));
        services.Configure<OutboxOptions>(config.GetSection(OutboxOptions.Section));
        services.Configure<JobsOptions>(config.GetSection(JobsOptions.Section));

        // ---- Persistence: SQL Server (ADR-001 amended: the platform runs on the customer's SQL Server). ----
        // Resolved when the context is built, so configuration layered on later (environment, secrets, tests) applies.
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var provider = cfg["Database:Provider"] ?? "SqlServer";
            var connectionString = cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo"));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<Licensing.Application.Integrations.IChangeFeed, Messaging.ChangeFeed>();

        // ---- Request context ----
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        // ---- Security ----
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecretHasher, HmacSecretHasher>();
        services.AddSingleton<IProductKeyGenerator, ProductKeyGenerator>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ILicenseTokenSigner, EcdsaLicenseTokenSigner>();
        services.AddSingleton<ISecretStore, FileSecretStore>();
        services.AddDataProtection().SetApplicationName("LicensingPlatform");
        services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((o, cfg, logs) =>
        {
            var keyRing = cfg["DataProtection:KeyRingPath"];
            if (!string.IsNullOrWhiteSpace(keyRing))
                o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keyRing), logs);
        });

        // ---- Cache: Redis when configured, otherwise in-memory (single instance / development). ----
        var redis = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "licensing:"; });
        else
            services.AddDistributedMemoryCache();
        services.AddSingleton<ILicenseCache, DistributedLicenseCache>();

        // ---- Cross-cutting ----
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<DbInitializer>();

        // ---- Background work (the "Worker" role; runs in the API host for now). ----
        services.AddSingleton<OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddSingleton<JobRunner>();
        services.AddHostedService<ScheduledJobsService>();

        return services;
    }
}
