using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Application.Licensing;
using Licensing.Domain.Catalog;
using Licensing.Domain.Customers;
using Licensing.Domain.Subscriptions;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Subscriptions;

public sealed record SubscriptionDto(
    Guid Id, Guid TenantId, Guid CustomerId, string CustomerName, Guid ProductId, string ProductName, Guid PlanId, string PlanName,
    int PlanVersion, SubscriptionStatus Status, DateTimeOffset StartDate, DateTimeOffset? EndDate, DateTimeOffset? TrialEndsAt,
    bool IsLifetime, int Version, IReadOnlyList<SubscriptionAction> AllowedActions, int Licenses, DateTimeOffset CreatedAt);

public sealed record SubscriptionHistoryDto(SubscriptionAction Action, SubscriptionStatus? FromStatus, SubscriptionStatus ToStatus, DateTimeOffset At, string? Details);
public sealed record SubscriptionDetailsDto(SubscriptionDto Subscription, IReadOnlyList<SubscriptionHistoryDto> History);

public sealed record SubscriptionFilter(Guid? CustomerId = null, SubscriptionStatus? Status = null, Guid? ProductId = null, Guid? PlanId = null,
    Guid? TenantId = null, DateTimeOffset? EndFrom = null, DateTimeOffset? EndTo = null);

public sealed record StartSubscriptionRequest(Guid CustomerId, Guid PlanId, DateTimeOffset? StartDate, string? Notes);
public sealed record RenewSubscriptionRequest(int? DurationDays, int? ExpectedVersion);
public sealed record ChangePlanRequest(Guid PlanId, int? ExpectedVersion);
public sealed record SubscriptionActionRequest(string? Reason, int? ExpectedVersion);

public sealed class SubscriptionService(IAppDbContext db, IAuditLogger audit, ILicenseCache cache, ITenantContext scope, TimeProvider clock)
{
    private sealed class Row
    {
        public required Subscription S { get; init; }
        public required string CustomerName { get; init; }
        public required string ProductName { get; init; }
        public required string PlanName { get; init; }
        public int PlanVersion { get; init; }
        public int Licenses { get; init; }
    }

    private IQueryable<Row> Rows(IQueryable<Subscription> q) =>
        from s in q
        join c in db.Customers on s.CustomerId equals c.Id
        join p in db.Products on s.ProductId equals p.Id
        join pl in db.Plans on s.PlanId equals pl.Id
        select new Row { S = s, CustomerName = c.Name, ProductName = p.Name, PlanName = pl.Name, PlanVersion = pl.Version, Licenses = db.Licenses.Count(l => l.SubscriptionId == s.Id) };

    private static SubscriptionDto ToDto(Row r) => new(
        r.S.Id, r.S.TenantId, r.S.CustomerId, r.CustomerName, r.S.ProductId, r.ProductName, r.S.PlanId, r.PlanName, r.PlanVersion,
        r.S.Status, r.S.StartDate, r.S.EndDate, r.S.TrialEndsAt, r.S.IsLifetime, r.S.Version,
        Subscription.AllowedActions(r.S.Status), r.Licenses, r.S.CreatedAt);

    public async Task<PagedResult<SubscriptionDto>> ListAsync(PageQuery page, SubscriptionFilter f, CancellationToken ct)
    {
        var q = db.Subscriptions.AsQueryable();
        if (f.CustomerId is not null) q = q.Where(s => s.CustomerId == f.CustomerId);
        if (f.Status is not null) q = q.Where(s => s.Status == f.Status);
        if (f.ProductId is not null) q = q.Where(s => s.ProductId == f.ProductId);
        if (f.PlanId is not null) q = q.Where(s => s.PlanId == f.PlanId);
        if (f.TenantId is { } tid && scope.IsUnrestricted) q = q.Where(s => s.TenantId == tid);
        if (f.EndFrom is not null) q = q.Where(s => s.EndDate != null && s.EndDate >= f.EndFrom);
        if (f.EndTo is not null) q = q.Where(s => s.EndDate != null && s.EndDate <= f.EndTo);
        var rows = Rows(q);
        if (!string.IsNullOrWhiteSpace(page.Search))
            rows = rows.Where(r => r.CustomerName.Contains(page.Search) || r.PlanName.Contains(page.Search) || r.ProductName.Contains(page.Search));

        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(r => r.S.CreatedAt)
            .Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<SubscriptionDto>(items.Select(ToDto).ToList(), total, page.SafePage, page.SafePageSize);
    }

    public async Task<Result<SubscriptionDetailsDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Rows(db.Subscriptions.Where(s => s.Id == id)).FirstOrDefaultAsync(ct);
        if (row is null) return AppErrors.NotFound("Subscription");
        var history = await db.SubscriptionHistory.Where(h => h.SubscriptionId == id).OrderByDescending(h => h.At)
            .Select(h => new SubscriptionHistoryDto(h.Action, h.FromStatus, h.ToStatus, h.At, h.Details)).ToListAsync(ct);
        return new SubscriptionDetailsDto(ToDto(row), history);
    }

    public async Task<Result<SubscriptionDetailsDto>> StartAsync(StartSubscriptionRequest request, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);
        if (customer is null) return AppErrors.NotFound("Customer");
        if (customer.Status != CustomerStatus.Active)
            return Error.Conflict("CUSTOMER_INACTIVE", "Subscriptions cannot be started for an inactive customer.");

        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (plan is null || plan.TenantId != customer.TenantId) return AppErrors.NotFound("Plan");
        if (plan.Status != PlanStatus.Published)
            return Error.Conflict("PLAN_NOT_PUBLISHED", "Only published plans can be subscribed to.");

        var now = clock.GetUtcNow();
        var sub = Subscription.Start(customer.TenantId, customer.Id, plan.ProductId, plan.Id, request.StartDate ?? now,
            plan.DurationDays, plan.TrialDays, now, request.Notes);
        db.Subscriptions.Add(sub);
        audit.Add("subscription.started", "Subscription", sub.Id.ToString(), details: $"plan={plan.Code} v{plan.Version}", tenantId: sub.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(sub.Id, ct);
    }

    public async Task<Result<SubscriptionDetailsDto>> RenewAsync(Guid id, RenewSubscriptionRequest request, CancellationToken ct)
    {
        return await MutateAsync(id, request.ExpectedVersion, "subscription.renewed", async (sub, now) =>
        {
            var plan = await db.Plans.FirstAsync(p => p.Id == sub.PlanId, ct);
            sub.Renew(request.DurationDays ?? plan.DurationDays, now);
            // Licenses follow the subscription's new end date (and come back if they expired with it).
            var licenses = await db.Licenses.Where(l => l.SubscriptionId == sub.Id).ToListAsync(ct);
            licenses.ForEach(l => { if (l.Status != Domain.Licensing.LicenseStatus.Revoked) l.Reinstate(sub.EndDate); });
            return null;
        }, ct);
    }

    public async Task<Result<SubscriptionDetailsDto>> ChangePlanAsync(Guid id, ChangePlanRequest request, CancellationToken ct)
    {
        return await MutateAsync(id, request.ExpectedVersion, "subscription.plan_changed", async (sub, now) =>
        {
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
            if (plan is null || plan.ProductId != sub.ProductId) return AppErrors.NotFound("Plan");
            if (plan.Status != PlanStatus.Published) return Error.Conflict("PLAN_NOT_PUBLISHED", "Only published plans can be used.");
            sub.ChangePlan(plan.Id, plan.DurationDays, now);
            return null;
        }, ct);
    }

    public Task<Result<SubscriptionDetailsDto>> SuspendAsync(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        MutateAsync(id, request.ExpectedVersion, "subscription.suspended", (sub, now) => { sub.Suspend(request.Reason, now); return Task.FromResult<Error?>(null); }, ct);

    public Task<Result<SubscriptionDetailsDto>> ResumeAsync(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        MutateAsync(id, request.ExpectedVersion, "subscription.resumed", (sub, now) => { sub.Resume(now); return Task.FromResult<Error?>(null); }, ct);

    public Task<Result<SubscriptionDetailsDto>> CancelAsync(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        MutateAsync(id, request.ExpectedVersion, "subscription.cancelled", (sub, now) => { sub.Cancel(request.Reason, now); return Task.FromResult<Error?>(null); }, ct);

    private async Task<Result<SubscriptionDetailsDto>> MutateAsync(
        Guid id, int? expectedVersion, string auditAction, Func<Subscription, DateTimeOffset, Task<Error?>> change, CancellationToken ct)
    {
        var sub = await db.Subscriptions.Include(s => s.History).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null) return AppErrors.NotFound("Subscription");
        if (expectedVersion is not null && expectedVersion != sub.Version) return AppErrors.ConcurrencyConflict;

        var error = await change(sub, clock.GetUtcNow());
        if (error is not null) return error;
        audit.Add(auditAction, "Subscription", id.ToString(), tenantId: sub.TenantId);
        await db.SaveChangesAsync(ct);
        await LicenseCacheInvalidation.ForSubscriptionAsync(db, cache, id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Hourly job: expires due subscriptions in batches. Safe to run repeatedly or concurrently with itself.</summary>
    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = 0;
        while (!ct.IsCancellationRequested)
        {
            var due = await db.Subscriptions.Include(s => s.History)
                .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Suspended)
                            && s.EndDate != null && s.EndDate <= now)
                .OrderBy(s => s.EndDate).Take(batchSize).ToListAsync(ct);
            if (due.Count == 0) break;

            foreach (var sub in due)
                if (sub.TryExpire(now))
                {
                    expired++;
                    audit.Add("subscription.expired", "Subscription", sub.Id.ToString(), tenantId: sub.TenantId);
                }
            await db.SaveChangesAsync(ct);
            foreach (var sub in due) await LicenseCacheInvalidation.ForSubscriptionAsync(db, cache, sub.Id, ct);
            if (due.Count < batchSize) break;
        }
        return expired;
    }
}
