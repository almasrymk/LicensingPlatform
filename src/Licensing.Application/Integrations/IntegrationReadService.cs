using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Catalog;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Integrations;

/// <summary>
/// Read models for Monitor Cloud's API client. Shapes match the integration contract: enum names, camelCase,
/// and only the caller's tenant (the query filter on <see cref="IAppDbContext"/>).
/// </summary>
public sealed record IntegrationCustomer(Guid Id, string Name, string? Email, string? Phone, string? Country, string Status, DateTimeOffset CreatedAt);

public sealed record IntegrationSubscription(
    Guid Id, string Status, Guid PlanId, string PlanCode, string PlanName, int PlanVersion, IReadOnlyList<string> Features,
    DateTimeOffset StartDate, DateTimeOffset? EndDate, DateTimeOffset? TrialEndsAt);

public sealed record IntegrationLicense(
    Guid Id, string LicenseNumber, Guid SubscriptionId, string Status, DateTimeOffset? ExpiresAt,
    int? MaxActivations, int ActiveActivations, IReadOnlyList<string> Features);

public sealed record IntegrationEntitlements(
    Guid CustomerId, string CustomerStatus, IReadOnlyList<IntegrationSubscription> Subscriptions, IReadOnlyList<IntegrationLicense> Licenses);

public sealed record IntegrationPrice(decimal Amount, string Currency);

public sealed record IntegrationPlan(
    Guid Id, string Code, string Name, int Version, IReadOnlyList<string> Features, int? MaxActivations, int? DurationDays, IntegrationPrice Price);

public sealed class IntegrationReadService(IAppDbContext db)
{
    public async Task<PagedResult<IntegrationCustomer>> CustomersAsync(PageQuery page, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var query = db.Customers.AsQueryable();
        if (updatedSince is not null)
            query = query.Where(c => c.CreatedAt >= updatedSince);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize)
            .Select(c => new IntegrationCustomer(c.Id, c.Name, c.Email, c.Phone, c.Country, c.Status.ToString(), c.CreatedAt))
            .ToListAsync(ct);
        return new PagedResult<IntegrationCustomer>(items, total, page.SafePage, page.SafePageSize);
    }

    /// <summary>Published plans of one product. A missing product code returns every published plan in the tenant.</summary>
    public async Task<IReadOnlyList<IntegrationPlan>> PlansAsync(string? productCode, CancellationToken ct)
    {
        var plans = db.Plans.Where(p => p.Status == PlanStatus.Published);
        if (!string.IsNullOrWhiteSpace(productCode))
        {
            var code = productCode.Trim();
            plans = plans.Where(p => db.Products.Any(product => product.Id == p.ProductId && product.Code == code));
        }

        var rows = await plans.OrderBy(p => p.Code).ThenByDescending(p => p.Version).ToListAsync(ct);
        return rows.Select(p => new IntegrationPlan(
            p.Id, p.Code, p.Name, p.Version, p.Features, p.MaxActivations, p.DurationDays, new IntegrationPrice(p.Price.Amount, p.Price.Currency))).ToList();
    }

    public async Task<Result<IntegrationEntitlements>> EntitlementsAsync(Guid customerId, string? productCode, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return AppErrors.NotFound("Customer");

        Guid? productId = null;
        if (!string.IsNullOrWhiteSpace(productCode))
        {
            var code = productCode.Trim();
            productId = await db.Products.Where(p => p.Code == code).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (productId is null)
                return new IntegrationEntitlements(customer.Id, customer.Status.ToString(), [], []);
        }

        var subscriptions = await db.Subscriptions
            .Where(s => s.CustomerId == customerId && (productId == null || s.ProductId == productId))
            .ToListAsync(ct);
        var planIds = subscriptions.Select(s => s.PlanId).Distinct().ToList();
        var plans = await db.Plans.Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var licenses = await db.Licenses
            .Where(l => l.CustomerId == customerId && (productId == null || l.ProductId == productId))
            .ToListAsync(ct);

        return new IntegrationEntitlements(
            customer.Id,
            customer.Status.ToString(),
            subscriptions.Select(s =>
            {
                var plan = plans[s.PlanId];
                return new IntegrationSubscription(
                    s.Id, s.Status.ToString(), plan.Id, plan.Code, plan.Name, plan.Version, plan.Features, s.StartDate, s.EndDate, s.TrialEndsAt);
            }).ToList(),
            licenses.Select(l => new IntegrationLicense(
                l.Id, l.LicenseNumber, l.SubscriptionId, l.Status.ToString(), l.ExpiresAt, l.MaxActivations, l.ActiveActivations, l.Features)).ToList());
    }
}
