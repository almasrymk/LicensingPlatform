using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Catalog;
using Licensing.Domain.Common;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Catalog;

public sealed record ProductDto(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, DateTimeOffset CreatedAt,
    int PublishedPlans, int Plans, int Licenses, IReadOnlyList<string> Platforms);
public sealed record SaveProductRequest(string Code, string Name, string? Description, bool IsActive = true, Guid? TenantId = null,
    IReadOnlyList<string>? Platforms = null);

public sealed record PlanDto(
    Guid Id, Guid TenantId, Guid ProductId, string ProductCode, string ProductName, string Code, string Name, int Version,
    PlanStatus Status, decimal Price, string Currency, int? DurationDays, int? TrialDays, int? MaxActivations,
    int HeartbeatIntervalHours, int OfflineGraceDays, IReadOnlyList<string> Features, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);

public sealed record SavePlanRequest(
    Guid ProductId, string Code, string Name, decimal Price, string Currency, int? DurationDays, int? TrialDays,
    int? MaxActivations, int HeartbeatIntervalHours, int OfflineGraceDays, IReadOnlyList<string>? Features);

public sealed class CatalogService(IAppDbContext db, ICurrentUser me, ITenantContext scope, IAuditLogger audit, TimeProvider clock)
{
    // ---------- Products ----------

    // The platform list is split on the client in this final projection.
    private IQueryable<ProductDto> ProjectProducts(IQueryable<Product> q) => q.Select(p => new ProductDto(
        p.Id, p.TenantId, p.Code, p.Name, p.Description, p.IsActive, p.CreatedAt,
        db.Plans.Count(pl => pl.ProductId == p.Id && pl.Status == PlanStatus.Published),
        db.Plans.Count(pl => pl.ProductId == p.Id && pl.Status != PlanStatus.Archived),
        db.Licenses.Count(l => l.ProductId == p.Id),
        p.PlatformsValue == "" ? new List<string>() : p.PlatformsValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()));

    public Task<PagedResult<ProductDto>> ListProductsAsync(PageQuery page, CancellationToken ct)
    {
        var q = db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(page.Search))
            q = q.Where(p => p.Name.Contains(page.Search) || p.Code.Contains(page.Search));
        return ProjectProducts(q.OrderBy(p => p.Name)).ToPagedAsync(page, ct);
    }

    public async Task<Result<ProductDto>> GetProductAsync(Guid id, CancellationToken ct)
    {
        var dto = await ProjectProducts(db.Products.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? AppErrors.NotFound("Product") : dto;
    }

    public async Task<Result<ProductDto>> CreateProductAsync(SaveProductRequest request, CancellationToken ct)
    {
        var tenant = me.ResolveWriteTenant(scope, request.TenantId);
        if (tenant.IsFailure) return tenant.Error!;
        var product = Product.Create(tenant.Value, request.Code, request.Name, request.Description, clock.GetUtcNow(), request.Platforms);
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant.Value && p.Code == product.Code, ct))
            return AppErrors.Duplicate("A product with this code");
        db.Products.Add(product);
        audit.Add("product.created", "Product", product.Id.ToString(), tenantId: product.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetProductAsync(product.Id, ct);
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(Guid id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return AppErrors.NotFound("Product");
        product.Update(request.Name, request.Description, request.IsActive, request.Platforms);
        audit.Add("product.updated", "Product", id.ToString(), tenantId: product.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetProductAsync(id, ct);
    }

    // ---------- Plans ----------

    public async Task<PagedResult<PlanDto>> ListPlansAsync(PageQuery page, Guid? productId, PlanStatus? status, CancellationToken ct)
    {
        var q = db.Plans.AsQueryable();
        if (productId is not null) q = q.Where(p => p.ProductId == productId);
        if (status is not null) q = q.Where(p => p.Status == status);
        // Customer users only see what is on sale.
        if (scope.CustomerId is not null) q = q.Where(p => p.Status == PlanStatus.Published);
        if (!string.IsNullOrWhiteSpace(page.Search))
            q = q.Where(p => p.Name.Contains(page.Search) || p.Code.Contains(page.Search));

        // Split() on the feature list is not translatable, so project in memory after paging.
        var total = await q.CountAsync(ct);
        var plans = await q.OrderBy(p => p.Code).ThenByDescending(p => p.Version)
            .Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code, p.Name })
            .ToListAsync(ct);
        return new PagedResult<PlanDto>(plans.Select(x => ToDto(x.pl, x.Code, x.Name)).ToList(), total, page.SafePage, page.SafePageSize);
    }

    public async Task<Result<PlanDto>> GetPlanAsync(Guid id, CancellationToken ct)
    {
        var x = await db.Plans.Where(p => p.Id == id)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code, p.Name })
            .FirstOrDefaultAsync(ct);
        return x is null ? AppErrors.NotFound("Plan") : ToDto(x.pl, x.Code, x.Name);
    }

    public async Task<Result<PlanDto>> CreatePlanAsync(SavePlanRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null) return AppErrors.NotFound("Product");
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        if (await db.Plans.AnyAsync(p => p.ProductId == product.Id && p.Code == code, ct))
            return AppErrors.Duplicate("A plan with this code (use 'new version' to change a published plan)");

        var plan = Plan.Create(product.TenantId, product.Id, request.Code!, request.Name, new Money(request.Price, request.Currency),
            request.DurationDays, request.TrialDays, request.MaxActivations, request.HeartbeatIntervalHours,
            request.OfflineGraceDays, request.Features ?? [], clock.GetUtcNow());
        db.Plans.Add(plan);
        audit.Add("plan.created", "Plan", plan.Id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetPlanAsync(plan.Id, ct);
    }

    public async Task<Result<PlanDto>> UpdatePlanAsync(Guid id, SavePlanRequest request, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Update(request.Name, new Money(request.Price, request.Currency), request.DurationDays, request.TrialDays,
            request.MaxActivations, request.HeartbeatIntervalHours, request.OfflineGraceDays, request.Features ?? []);
        audit.Add("plan.updated", "Plan", id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetPlanAsync(id, ct);
    }

    public async Task<Result<PlanDto>> PublishPlanAsync(Guid id, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Publish(clock.GetUtcNow());
        audit.Add("plan.published", "Plan", id.ToString(), details: $"{plan.Code} v{plan.Version}", tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetPlanAsync(id, ct);
    }

    public async Task<Result<PlanDto>> ArchivePlanAsync(Guid id, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Archive();
        audit.Add("plan.archived", "Plan", id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetPlanAsync(id, ct);
    }

    public async Task<Result<PlanDto>> NewPlanVersionAsync(Guid id, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        if (await db.Plans.AnyAsync(p => p.ProductId == plan.ProductId && p.Code == plan.Code && p.Status == PlanStatus.Draft, ct))
            return Error.Conflict("PLAN_DRAFT_EXISTS", "A draft version of this plan already exists.");
        var max = await db.Plans.Where(p => p.ProductId == plan.ProductId && p.Code == plan.Code).MaxAsync(p => p.Version, ct);
        var draft = plan.NewVersion(max + 1, clock.GetUtcNow());
        db.Plans.Add(draft);
        audit.Add("plan.versioned", "Plan", draft.Id.ToString(), details: $"{draft.Code} v{draft.Version}", tenantId: draft.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetPlanAsync(draft.Id, ct);
    }

    public async Task<Result<Entitlements>> GetEntitlementsAsync(Guid planId, CancellationToken ct)
    {
        var x = await db.Plans.Where(p => p.Id == planId)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code })
            .FirstOrDefaultAsync(ct);
        return x is null ? AppErrors.NotFound("Plan") : x.pl.ToEntitlements(x.Code);
    }

    private static PlanDto ToDto(Plan pl, string productCode, string productName) => new(
        pl.Id, pl.TenantId, pl.ProductId, productCode, productName, pl.Code, pl.Name, pl.Version, pl.Status,
        pl.Price.Amount, pl.Price.Currency, pl.DurationDays, pl.TrialDays, pl.MaxActivations, pl.HeartbeatIntervalHours,
        pl.OfflineGraceDays, pl.Features, pl.CreatedAt, pl.PublishedAt);
}
