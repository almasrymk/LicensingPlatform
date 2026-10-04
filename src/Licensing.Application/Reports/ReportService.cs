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

/// <summary>A KPI with its change against the same figure 30 days ago (null when there is nothing to compare).</summary>
public sealed record KpiDto(decimal Value, decimal? ChangePercent);

public sealed record ProductShareDto(string ProductCode, string ProductName, int Licenses, decimal Percent);

public sealed record RecentActivationDto(Guid LicenseId, string CustomerName, string ProductName, string DeviceId, string? DeviceName,
    string? IpAddress, DateTimeOffset At, bool Online);

public sealed record AttentionDto(int ExpiredLicenses, int RenewalsDue, int LimitReached, int SuspiciousActivations);

/// <summary>Everything the dashboard shows in one call.</summary>
public sealed record OverviewDto(
    KpiDto Tenants, KpiDto Customers, KpiDto ActiveLicenses, KpiDto Subscriptions, KpiDto Revenue, string Currency, int ExpiringSoon,
    int TotalLicenses, IReadOnlyList<ProductShareDto> LicensesByProduct, IReadOnlyList<RecentActivationDto> RecentActivations, AttentionDto Attention);

public sealed record TrendPointDto(string Label, decimal Revenue, int Subscriptions);

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

    private static KpiDto Kpi(decimal now, decimal before) =>
        new(now, before == 0 ? (now == 0 ? 0 : null) : Math.Round((now - before) / before * 100, 1));

    private sealed record SubRow(SubscriptionStatus Status, DateTimeOffset StartDate, DateTimeOffset CreatedAt, DateTimeOffset? EndDate, decimal Price, string Currency);

    private async Task<List<SubRow>> SubscriptionRowsAsync(CancellationToken ct) =>
        (await (from s in db.Subscriptions
                join p in db.Plans on s.PlanId equals p.Id
                select new { s.Status, s.StartDate, s.CreatedAt, s.EndDate, p.Price.Amount, p.Price.Currency }).ToListAsync(ct))
        .Select(x => new SubRow(x.Status, x.StartDate, x.CreatedAt, x.EndDate, x.Amount, x.Currency)).ToList();

    /// <summary>
    /// Dashboard overview. Revenue is the contract value of the subscriptions that are currently active (plan price);
    /// billing is a later phase, so this is booked value, not collected money.
    /// </summary>
    public async Task<OverviewDto> OverviewAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthAgo = now.AddDays(-30);
        var tenants = scope.IsUnrestricted && scope.TenantId is null ? db.Tenants : db.Tenants.Where(t => t.Id == scope.TenantId);
        var customers = scope.CustomerId is { } cid ? db.Customers.Where(c => c.Id == cid) : db.Customers;

        var subs = await SubscriptionRowsAsync(ct);
        var running = subs.Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial).ToList();
        var runningBefore = running.Where(s => s.CreatedAt <= monthAgo).ToList();
        var revenueNow = running.Where(s => s.Status == SubscriptionStatus.Active).Sum(s => s.Price);
        var revenueBefore = runningBefore.Where(s => s.Status == SubscriptionStatus.Active).Sum(s => s.Price);
        var currency = subs.GroupBy(s => s.Currency).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "USD";

        var activeLicenses = await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active, ct);
        var activeLicensesBefore = await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.IssuedAt <= monthAgo, ct);

        var byProduct = await (from l in db.Licenses
                               join p in db.Products on l.ProductId equals p.Id
                               group l by new { l.ProductCode, p.Name } into g
                               select new { g.Key.ProductCode, g.Key.Name, Count = g.Count() }).ToListAsync(ct);
        var totalLicenses = byProduct.Sum(x => x.Count);
        var shares = byProduct.OrderByDescending(x => x.Count)
            .Select(x => new ProductShareDto(x.ProductCode, x.Name, x.Count, totalLicenses == 0 ? 0 : Math.Round(x.Count * 100m / totalLicenses, 0)))
            .ToList();

        var recentRaw = await (from a in db.LicenseActivations
                               join l in db.Licenses on a.LicenseId equals l.Id
                               join c in db.Customers on a.CustomerId equals c.Id
                               join p in db.Products on l.ProductId equals p.Id
                               orderby a.ActivatedAt descending
                               select new { a.LicenseId, CustomerName = c.Name, ProductName = p.Name, a.DeviceId, a.DeviceName, a.LastIpAddress,
                                   a.ActivatedAt, a.LastHeartbeatAt, a.Status, l.HeartbeatIntervalHours })
            .Take(5).ToListAsync(ct);
        var recent = recentRaw.Select(r => new RecentActivationDto(r.LicenseId, r.CustomerName, r.ProductName, r.DeviceId, r.DeviceName,
            r.LastIpAddress, r.ActivatedAt,
            r.Status == ActivationStatus.Active && r.LastHeartbeatAt is { } hb && hb >= now.AddHours(-2 * r.HeartbeatIntervalHours))).ToList();

        var attention = new AttentionDto(
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Expired, ct),
            running.Count(s => s.EndDate is { } end && end > now && end <= now.AddDays(30)),
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.MaxActivations != null && l.ActiveActivations >= l.MaxActivations, ct),
            await db.ActivationAttempts.CountAsync(a => !a.Success && a.At >= now.AddDays(-7) &&
                (scope.CustomerId == null || a.CustomerId == scope.CustomerId), ct));

        return new OverviewDto(
            Kpi(await tenants.CountAsync(t => t.Status == TenantStatus.Active, ct),
                await tenants.CountAsync(t => t.Status == TenantStatus.Active && t.CreatedAt <= monthAgo, ct)),
            Kpi(await customers.CountAsync(ct), await customers.CountAsync(c => c.CreatedAt <= monthAgo, ct)),
            Kpi(activeLicenses, activeLicensesBefore),
            Kpi(running.Count, runningBefore.Count),
            Kpi(revenueNow, revenueBefore),
            currency,
            await db.Licenses.CountAsync(l => l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt > now && l.ExpiresAt <= now.AddDays(30), ct),
            totalLicenses, shares, recent, attention);
    }

    /// <summary>
    /// Revenue (non-trial plan value) and running subscriptions at the end of each month of a year ("monthly"),
    /// or at the end of each of the last five years ("yearly"). Future months are empty.
    /// </summary>
    public async Task<IReadOnlyList<TrendPointDto>> TrendAsync(string granularity, int year, CancellationToken ct)
    {
        var subs = await SubscriptionRowsAsync(ct);
        var now = clock.GetUtcNow();
        var points = granularity == "yearly"
            ? Enumerable.Range(now.Year - 4, 5).Select(y => (Label: y.ToString(), At: new DateTimeOffset(y, 12, 31, 23, 59, 59, TimeSpan.Zero)))
            : Enumerable.Range(1, year == now.Year ? now.Month : 12)
                .Select(m => (Label: m.ToString("00"), At: new DateTimeOffset(year, m, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1).AddSeconds(-1)));

        // Months after the current one are not returned, so the chart ends at today.
        return points.Select(p =>
        {
            var at = p.At > now ? now : p.At;
            var running = subs.Where(s => s.StartDate <= at && (s.EndDate is null || s.EndDate > at) && s.Status != SubscriptionStatus.Cancelled).ToList();
            return new TrendPointDto(p.Label, running.Where(s => s.Status != SubscriptionStatus.Trial).Sum(s => s.Price), running.Count);
        }).ToList();
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
