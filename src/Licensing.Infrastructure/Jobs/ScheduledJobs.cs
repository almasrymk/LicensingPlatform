using Licensing.Application.Audit;
using Licensing.Application.Licensing;
using Licensing.Application.Reports;
using Licensing.Application.Subscriptions;
using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Licensing.Infrastructure.Jobs;

public sealed class JobsOptions
{
    public const string Section = "Jobs";

    public bool Enabled { get; set; } = true;
    public int ExpiryIntervalMinutes { get; set; } = 60;
    public int MaintenanceIntervalMinutes { get; set; } = 24 * 60;
    public int BatchSize { get; set; } = 500;
    public int AuditRetentionDays { get; set; } = 365;
    public int IdempotencyRetentionHours { get; set; } = 48;
}

/// <summary>The recurring jobs of the plan. Each one is idempotent and batch-based, so overlapping or repeated runs are safe.</summary>
public sealed class JobRunner(IServiceScopeFactory scopes, IOptions<JobsOptions> options, TimeProvider clock, ILogger<JobRunner> logger)
{
    private async Task<T> RunAsync<T>(string name, Func<IServiceProvider, Task<T>> job, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
        var result = await job(scope.ServiceProvider);
        logger.LogInformation("Job {Job} finished: {Result}", name, result);
        return result;
    }

    public Task<int> ExpireSubscriptionsAsync(CancellationToken ct) =>
        RunAsync("expire-subscriptions", sp => sp.GetRequiredService<SubscriptionService>().ExpireDueAsync(options.Value.BatchSize, ct), ct);

    public Task<int> ExpireLicensesAsync(CancellationToken ct) =>
        RunAsync("expire-licenses", sp => sp.GetRequiredService<LicenseService>().ExpireDueAsync(options.Value.BatchSize, ct), ct);

    public Task<int> CleanupRefreshTokensAsync(CancellationToken ct) =>
        RunAsync("refresh-token-cleanup", sp =>
        {
            var cutoff = clock.GetUtcNow().AddDays(-1);
            return sp.GetRequiredService<AppDbContext>().RefreshTokens
                .Where(t => t.ExpiresAt < cutoff || (t.RevokedAt != null && t.RevokedAt < cutoff)).ExecuteDeleteAsync(ct);
        }, ct);

    public Task<int> CleanupIdempotencyAsync(CancellationToken ct) =>
        RunAsync("idempotency-cleanup", sp =>
        {
            var cutoff = clock.GetUtcNow().AddHours(-options.Value.IdempotencyRetentionHours);
            return sp.GetRequiredService<AppDbContext>().IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        }, ct);

    public Task<int> AuditRetentionAsync(CancellationToken ct) =>
        RunAsync("audit-retention", sp => sp.GetRequiredService<AuditQueryService>()
            .PurgeOlderThanAsync(TimeSpan.FromDays(options.Value.AuditRetentionDays), ct), ct);

    public Task<int> AggregateUsageAsync(CancellationToken ct) =>
        RunAsync("usage-aggregation", sp => sp.GetRequiredService<ReportService>().AggregateUsageAsync(ct), ct);
}

public sealed class ScheduledJobsService(JobRunner jobs, IOptions<JobsOptions> options, ILogger<ScheduledJobsService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var o = options.Value;
        var lastMaintenance = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Safe(jobs.ExpireSubscriptionsAsync, stoppingToken);
            await Safe(jobs.ExpireLicensesAsync, stoppingToken);
            await Safe(jobs.AggregateUsageAsync, stoppingToken);

            if (DateTimeOffset.UtcNow - lastMaintenance >= TimeSpan.FromMinutes(o.MaintenanceIntervalMinutes))
            {
                await Safe(jobs.CleanupRefreshTokensAsync, stoppingToken);
                await Safe(jobs.CleanupIdempotencyAsync, stoppingToken);
                await Safe(jobs.AuditRetentionAsync, stoppingToken);
                lastMaintenance = DateTimeOffset.UtcNow;
            }

            await Task.Delay(TimeSpan.FromMinutes(o.ExpiryIntervalMinutes), stoppingToken);
        }
    }

    private async Task Safe(Func<CancellationToken, Task<int>> job, CancellationToken ct)
    {
        try { await job(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Scheduled job failed"); }
    }
}
