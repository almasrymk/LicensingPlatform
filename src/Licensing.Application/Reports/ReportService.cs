using Licensing.Application.Abstractions;
using Licensing.Domain.Licensing;
using Licensing.Domain.Reporting;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Reports;

public sealed record DashboardDto(
    int Tenants, int ActiveTenants, int Customers, int Products, int PublishedPlans,
    int ActiveSubscriptions, int TrialSubscriptions, int ExpiredSubscriptions,
    int ActiveLicenses, int SuspendedLicenses, int RevokedLicenses, int ActiveDevices,
    int ExpiringIn7Days, int ExpiringIn30Days, int FailedActivations7Days, int SuccessfulActivations7Days,
    IReadOnlyList<DailyPointDto> ActivationsTrend);

public sealed record DailyPointDto(DateOnly Day, int Successful, int Failed);

public sealed record ExpiringLicenseDto(Guid LicenseId, string LicenseNumber, Guid CustomerId, string CustomerName, string ProductCode,
    string PlanCode, DateTimeOffset ExpiresAt, int DaysLeft, int ActiveActivations);

public sealed record ActiveDeviceDto(Guid ActivationId, Guid LicenseId, string LicenseNumber, string CustomerName, string DeviceId,
    string? DeviceName, string? AppVersion, DateTimeOffset ActivatedAt, DateTimeOffset? LastHeartbeatAt, bool Stale);

public sealed record FailedActivationDto(DateTimeOffset At, string? LicenseNumber, string? CustomerName, string? ProductKeyPrefix,
    string DeviceId, string? ErrorCode, string? IpAddress);

public sealed record FailedActivationSummaryDto(string ErrorCode, int Count);

public sealed record UsageDto(DateOnly Day, string CustomerName, int ActiveLicenses, int ActiveDevices, int SuccessfulActivations, int FailedActivations);

/// <summary>
/// Read-only queries for the dashboard and reports. Tenant and customer isolation come from the global query filters,
/// so a customer user automatically gets reports about its own data only.
/// </summary>
public sealed class ReportService(IAppDbContext db, ITenantContext scope, TimeProvider clock)
{
    public async Task<DashboardDto> DashboardAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var in7 = now.AddDays(7);
        var in30 = now.AddDays(30);
        var since7 = now.AddDays(-7);
        var since14 = now.AddDays(-14);

        var tenants = scope.IsUnrestricted && scope.TenantId is null ? db.Tenants : db.Tenants.Where(t => t.Id == scope.TenantId);
        var customers = scope.CustomerId is { } cid ? db.Customers.Where(c => c.Id == cid) : db.Customers;

        var attempts = await db.ActivationAttempts.Where(a => a.At >= since14 &&
                (scope.CustomerId == null || a.CustomerId == scope.CustomerId))
            .Select(a => new { a.At, a.Success }).ToListAsync(ct);
        var trend = Enumerable.Range(0, 14).Select(i => DateOnly.FromDateTime(now.AddDays(-13 + i).UtcDateTime))
            .Select(day => new DailyPointDto(day,
                attempts.Count(a => a.Success && DateOnly.FromDateTime(a.At.UtcDateTime) == day),
                attempts.Count(a => !a.Success && DateOnly.FromDateTime(a.At.UtcDateTime) == day)))
            .ToList();

        return new DashboardDto(
            await tenants.CountAsync(ct),
            await tenants.CountAsync(t => t.Status == TenantStatus.Active, ct),
            await customers.CountAsync(ct),
            await db.Products.CountAsync(ct),
            await db.Plans.CountAsync(p => p.Status == Domain.Catalog.PlanStatus.Published, ct),
            await db.Subscriptions.CountAsync(s => s.Status == SubscriptionStatus.Active, ct),
            await db.Subscriptions.CountAsync(s => s.Status == SubscriptionStatus.Trial, ct),
            await db.Subscriptions.CountAsync(s => s.Status == SubscriptionStatus.Expired, ct),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active, ct),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Suspended, ct),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Revoked, ct),
            await db.LicenseActivations.CountAsync(a => a.Status == ActivationStatus.Active, ct),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt > now && l.ExpiresAt <= in7, ct),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt > now && l.ExpiresAt <= in30, ct),
            attempts.Count(a => !a.Success && a.At >= since7),
            attempts.Count(a => a.Success && a.At >= since7),
            trend);
    }

    public async Task<IReadOnlyList<ExpiringLicenseDto>> ExpiringLicensesAsync(int days, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var until = now.AddDays(Math.Clamp(days, 1, 365));
        var rows = await (
            from l in db.Licenses
            join c in db.Customers on l.CustomerId equals c.Id
            where l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt > now && l.ExpiresAt <= until
            orderby l.ExpiresAt
            select new { l.Id, l.LicenseNumber, l.CustomerId, c.Name, l.ProductCode, l.PlanCode, l.ExpiresAt, l.ActiveActivations })
            .Take(500).ToListAsync(ct);
        return rows.Select(r => new ExpiringLicenseDto(r.Id, r.LicenseNumber, r.CustomerId, r.Name, r.ProductCode, r.PlanCode,
            r.ExpiresAt!.Value, (int)Math.Ceiling((r.ExpiresAt.Value - now).TotalDays), r.ActiveActivations)).ToList();
    }

    public async Task<IReadOnlyList<ActiveDeviceDto>> ActiveDevicesAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await (
            from a in db.LicenseActivations
            join l in db.Licenses on a.LicenseId equals l.Id
            join c in db.Customers on a.CustomerId equals c.Id
            where a.Status == ActivationStatus.Active
            orderby a.LastHeartbeatAt descending
            select new { a.Id, a.LicenseId, l.LicenseNumber, c.Name, a.DeviceId, a.DeviceName, a.AppVersion, a.ActivatedAt, a.LastHeartbeatAt, l.HeartbeatIntervalHours })
            .Take(1000).ToListAsync(ct);
        return rows.Select(r => new ActiveDeviceDto(r.Id, r.LicenseId, r.LicenseNumber, r.Name, r.DeviceId, r.DeviceName, r.AppVersion,
            r.ActivatedAt, r.LastHeartbeatAt,
            Stale: r.LastHeartbeatAt is null || r.LastHeartbeatAt < now.AddHours(-2 * r.HeartbeatIntervalHours))).ToList();
    }

    public async Task<(IReadOnlyList<FailedActivationDto> Items, IReadOnlyList<FailedActivationSummaryDto> Summary)> FailedActivationsAsync(int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 90));
        var q = db.ActivationAttempts.Where(a => !a.Success && a.At >= since);
        if (scope.CustomerId is { } cid) q = q.Where(a => a.CustomerId == cid);

        var items = await (
            from a in q
            join l in db.Licenses on a.LicenseId equals l.Id into lj
            from l in lj.DefaultIfEmpty()
            join c in db.Customers on a.CustomerId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            orderby a.At descending
            select new FailedActivationDto(a.At, l != null ? l.LicenseNumber : null, c != null ? c.Name : null,
                a.ProductKeyPrefix, a.DeviceId, a.ErrorCode, a.IpAddress))
            .Take(500).ToListAsync(ct);

        var grouped = await q.GroupBy(a => a.ErrorCode ?? "UNKNOWN").Select(g => new { Code = g.Key, Count = g.Count() }).ToListAsync(ct);
        var summary = grouped.OrderByDescending(x => x.Count).Select(x => new FailedActivationSummaryDto(x.Code, x.Count)).ToList();
        return (items, summary);
    }

    public async Task<IReadOnlyList<UsageDto>> UsageAsync(int days, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 365)).UtcDateTime);
        return await (
            from u in db.UsageDaily
            join c in db.Customers on u.CustomerId equals c.Id
            where u.Day >= since
            orderby u.Day descending, c.Name
            select new UsageDto(u.Day, c.Name, u.ActiveLicenses, u.ActiveDevices, u.SuccessfulActivations, u.FailedActivations))
            .Take(2000).ToListAsync(ct);
    }

    /// <summary>Daily job: (re)computes today's usage row for each customer. Idempotent.</summary>
    public async Task<int> AggregateUsageAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var licenses = await db.Licenses.Where(l => l.Status == LicenseStatus.Active)
            .GroupBy(l => new { l.TenantId, l.CustomerId }).Select(g => new { g.Key.TenantId, g.Key.CustomerId, Count = g.Count() }).ToListAsync(ct);
        var devices = await db.LicenseActivations.Where(a => a.Status == ActivationStatus.Active)
            .GroupBy(a => a.CustomerId).Select(g => new { CustomerId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var attempts = await db.ActivationAttempts.Where(a => a.At >= dayStart && a.CustomerId != null)
            .GroupBy(a => new { a.CustomerId, a.Success }).Select(g => new { g.Key.CustomerId, g.Key.Success, Count = g.Count() }).ToListAsync(ct);
        var existing = await db.UsageDaily.Where(u => u.Day == day).ToListAsync(ct);

        foreach (var l in licenses)
        {
            var row = existing.FirstOrDefault(u => u.CustomerId == l.CustomerId);
            if (row is null)
            {
                row = UsageDaily.Create(l.TenantId, l.CustomerId, day);
                db.UsageDaily.Add(row);
            }
            row.Set(l.Count,
                devices.FirstOrDefault(d => d.CustomerId == l.CustomerId)?.Count ?? 0,
                attempts.FirstOrDefault(a => a.CustomerId == l.CustomerId && a.Success)?.Count ?? 0,
                attempts.FirstOrDefault(a => a.CustomerId == l.CustomerId && !a.Success)?.Count ?? 0);
        }
        await db.SaveChangesAsync(ct);
        return licenses.Count;
    }
}
