using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Licensing;
using Licensing.Domain.Tenants;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Tenants;

public sealed record TenantDto(
    Guid Id, string Name, string Code, string? ContactEmail, TenantStatus Status, DateTimeOffset CreatedAt,
    string? SuspensionReason, int Customers, int Users, int ActiveLicenses);

public sealed record CreateTenantRequest(string Name, string Code, string? ContactEmail);
public sealed record UpdateTenantRequest(string Name, string? ContactEmail);
public sealed record SuspendRequest(string? Reason);

/// <summary>Tenants are managed by platform admins only (enforced by the endpoint policy).</summary>
public sealed class TenantService(IAppDbContext db, ITenantContext scope, IAuditLogger audit, ILicenseCache cache, TimeProvider clock)
{
    private IQueryable<Tenant> Scoped() => scope.IsUnrestricted || scope.TenantId is null
        ? db.Tenants
        : db.Tenants.Where(t => t.Id == scope.TenantId);

    private IQueryable<TenantDto> Project(IQueryable<Tenant> q) => q.Select(t => new TenantDto(
        t.Id, t.Name, t.Code, t.ContactEmail, t.Status, t.CreatedAt, t.SuspensionReason,
        db.Customers.IgnoreQueryFilters().Count(c => c.TenantId == t.Id),
        db.Users.Count(u => u.TenantId == t.Id),
        db.Licenses.IgnoreQueryFilters().Count(l => l.TenantId == t.Id && l.Status == LicenseStatus.Active)));

    public Task<PagedResult<TenantDto>> ListAsync(PageQuery page, TenantStatus? status, CancellationToken ct)
    {
        var q = Scoped();
        if (!string.IsNullOrWhiteSpace(page.Search))
            q = q.Where(t => t.Name.Contains(page.Search) || t.Code.Contains(page.Search));
        if (status is not null) q = q.Where(t => t.Status == status);
        return Project(q.OrderBy(t => t.Name)).ToPagedAsync(page, ct);
    }

    public async Task<Result<TenantDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await Project(Scoped().Where(t => t.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? AppErrors.NotFound("Tenant") : dto;
    }

    public async Task<Result<TenantDto>> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        var tenant = Tenant.Create(request.Name, request.Code, request.ContactEmail, clock.GetUtcNow());
        if (await db.Tenants.AnyAsync(t => t.Code == tenant.Code, ct)) return AppErrors.Duplicate("A tenant with this code");
        db.Tenants.Add(tenant);
        audit.Add("tenant.created", "Tenant", tenant.Id.ToString(), tenantId: tenant.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(tenant.Id, ct);
    }

    public async Task<Result<TenantDto>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return AppErrors.NotFound("Tenant");
        tenant.Rename(request.Name, request.ContactEmail);
        audit.Add("tenant.updated", "Tenant", id.ToString(), tenantId: id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Suspension takes effect immediately: the tenant's users cannot sign in or refresh, its API clients cannot get tokens,
    /// and its licenses fail validation (checked on every licensing call).
    /// </summary>
    public async Task<Result<TenantDto>> SuspendAsync(Guid id, SuspendRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return AppErrors.NotFound("Tenant");
        var now = clock.GetUtcNow();
        tenant.Suspend(request.Reason, now);

        var userIds = await db.Users.Where(u => u.TenantId == id).Select(u => u.Id).ToListAsync(ct);
        var tokens = await db.RefreshTokens.Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.Revoke(now));

        audit.Add("tenant.suspended", "Tenant", id.ToString(), details: request.Reason, tenantId: id);
        await db.SaveChangesAsync(ct);
        await Licensing.LicenseCacheInvalidation.ForTenantAsync(db, cache, id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<TenantDto>> ResumeAsync(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return AppErrors.NotFound("Tenant");
        tenant.Resume();
        audit.Add("tenant.resumed", "Tenant", id.ToString(), tenantId: id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
