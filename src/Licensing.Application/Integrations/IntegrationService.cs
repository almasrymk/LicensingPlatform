using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Application.Licensing;
using Licensing.Domain.Catalog;
using Licensing.Domain.Licensing;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Integrations;

public sealed record IntegrationCustomer(Guid Id, string Name, string? Email, string? Phone, string? Country, string Status, DateTimeOffset CreatedAt);

public sealed record EntitlementSubscription(
    Guid Id, string Status, string PlanCode, string PlanName, int PlanVersion, IReadOnlyList<string> Features,
    DateTimeOffset StartDate, DateTimeOffset? EndDate, DateTimeOffset? TrialEndsAt);

public sealed record EntitlementLicense(
    Guid Id, string LicenseNumber, Guid SubscriptionId, string Status, DateTimeOffset? ExpiresAt, int? MaxActivations,
    int ActiveActivations, IReadOnlyList<string> Features);

public sealed record CustomerEntitlements(
    Guid CustomerId, string CustomerStatus, IReadOnlyList<EntitlementSubscription> Subscriptions, IReadOnlyList<EntitlementLicense> Licenses);

public sealed record PlanPrice(decimal Amount, string Currency);

public sealed record IntegrationPlan(
    Guid Id, string Code, string Name, int Version, IReadOnlyList<string> Features, int? MaxActivations, int? DurationDays, PlanPrice Price);

public sealed record IntegrationActivation(
    string DeviceId, string? DeviceName, string Status, DateTimeOffset ActivatedAt, DateTimeOffset? LastSeenAt, string? AppVersion, string? Os);

public sealed record ChangeItem(Guid Id, string Type, DateTimeOffset OccurredAt, Guid? CustomerId, Guid? SubscriptionId, Guid? LicenseId);

public sealed record ChangeFeedPage(IReadOnlyList<ChangeItem> Items, string? NextCursor, bool HasMore);

public sealed record DeviceCheckRequest(string? AppVersion, string? Os);

/// <summary>The integration events of the outbox for one tenant, in order (LP-4).</summary>
public interface IChangeFeed
{
    Task<Result<ChangeFeedPage>> ReadAsync(Guid tenantId, string? cursor, int take, CancellationToken ct);
}

/// <summary>
/// Read and seat operations for an integrated platform (Monitor Cloud) authenticated with an API client token (LP-3/4/5).
/// Every query is limited to the client's tenant by the global query filters.
/// </summary>
public sealed class IntegrationService(IAppDbContext db, ICurrentUser caller, DeviceLicensingService licensing, IChangeFeed changes)
{
    public const int MaxChangesPerPage = 500;

    public async Task<Result<PagedResult<IntegrationCustomer>>> ListCustomersAsync(PageQuery page, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        // Customers carry no modification time yet; creation time is the closest available signal.
        if (updatedSince is { } since)
            query = query.Where(c => c.CreatedAt >= since);
        return await query
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new IntegrationCustomer(c.Id, c.Name, c.Email, c.Phone, c.Country, c.Status.ToString(), c.CreatedAt))
            .ToPagedAsync(page, ct);
    }

    public async Task<Result<CustomerEntitlements>> GetEntitlementsAsync(Guid customerId, string? productCode, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
            return AppErrors.NotFound("Customer");

        var products = db.Products.AsNoTracking().Where(p => string.IsNullOrEmpty(productCode) || p.Code == productCode).Select(p => p.Id);

        var subscriptions = await (
            from s in db.Subscriptions.AsNoTracking()
            join p in db.Plans.AsNoTracking() on s.PlanId equals p.Id
            where s.CustomerId == customerId && products.Contains(s.ProductId)
            orderby s.StartDate
            select new { s.Id, s.Status, p.Code, p.Name, p.Version, p.FeaturesValue, s.StartDate, s.EndDate, s.TrialEndsAt })
            .ToListAsync(ct);

        var licenses = await db.Licenses.AsNoTracking()
            .Where(l => l.CustomerId == customerId && products.Contains(l.ProductId))
            .OrderBy(l => l.IssuedAt)
            .ToListAsync(ct);

        return new CustomerEntitlements(
            customer.Id,
            customer.Status.ToString(),
            subscriptions.Select(s => new EntitlementSubscription(
                s.Id, s.Status.ToString(), s.Code, s.Name, s.Version, SplitFeatures(s.FeaturesValue), s.StartDate, s.EndDate, s.TrialEndsAt)).ToList(),
            licenses.Select(l => new EntitlementLicense(
                l.Id, l.LicenseNumber, l.SubscriptionId, l.Status.ToString(), l.ExpiresAt, l.MaxActivations, l.ActiveActivations, l.Features)).ToList());
    }

    public async Task<Result<IReadOnlyList<IntegrationPlan>>> ListPlansAsync(string? productCode, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking().Where(p => string.IsNullOrEmpty(productCode) || p.Code == productCode).Select(p => p.Id);
        var plans = await db.Plans.AsNoTracking()
            .Where(p => p.Status == PlanStatus.Published && products.Contains(p.ProductId))
            .OrderBy(p => p.Code).ThenBy(p => p.Version)
            .ToListAsync(ct);
        return plans
            .Select(p => new IntegrationPlan(p.Id, p.Code, p.Name, p.Version, p.Features, p.MaxActivations, p.DurationDays, new PlanPrice(p.Price.Amount, p.Price.Currency)))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<IntegrationActivation>>> ListActivationsAsync(Guid licenseId, CancellationToken ct)
    {
        if (!await db.Licenses.AnyAsync(l => l.Id == licenseId, ct))
            return AppErrors.NotFound("License");
        var activations = await db.LicenseActivations.AsNoTracking()
            .Where(a => a.LicenseId == licenseId)
            .OrderBy(a => a.ActivatedAt)
            .Select(a => new IntegrationActivation(a.DeviceId, a.DeviceName, a.Status.ToString(), a.ActivatedAt, a.LastHeartbeatAt, a.AppVersion, a.OperatingSystem))
            .ToListAsync(ct);
        return activations;
    }

    public Task<Result<LicenseCheckResponse>> HeartbeatAsync(Guid licenseId, string deviceId, DeviceCheckRequest request, CancellationToken ct) =>
        licensing.HeartbeatByLicenseAsync(licenseId, deviceId, request.AppVersion, request.Os, ct);

    public Task<Result> ReleaseAsync(Guid licenseId, string deviceId, CancellationToken ct) =>
        licensing.ReleaseByLicenseAsync(licenseId, deviceId, ct);

    public Task<Result<ChangeFeedPage>> GetChangesAsync(string? cursor, int take, CancellationToken ct) =>
        changes.ReadAsync(caller.TenantId!.Value, cursor, Math.Clamp(take, 1, MaxChangesPerPage), ct);

    private static IReadOnlyList<string> SplitFeatures(string value) =>
        value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
