using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.BuildingBlocks.Application;
using Licensing.Domain.Catalog;
using Licensing.Domain.Common;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Modules.Catalog.Application.Plans;

// ---------- Queries ----------

public sealed record ListPlansQuery(PageQuery Page, Guid? ProductId, PlanStatus? Status) : IQuery<PagedResult<PlanDto>>;

public sealed record GetPlanQuery(Guid Id) : IQuery<Result<PlanDto>>;

/// <summary>The plan as machine-readable entitlements (what a license issued from it grants).</summary>
public sealed record GetPlanEntitlementsQuery(Guid PlanId) : IQuery<Result<Entitlements>>;

// ---------- Commands ----------

public sealed record CreatePlanCommand(SavePlanRequest Request) : ICommand<Result<PlanDto>>;

public sealed record UpdatePlanCommand(Guid Id, SavePlanRequest Request) : ICommand<Result<PlanDto>>;

public sealed record PublishPlanCommand(Guid Id) : ICommand<Result<PlanDto>>;

public sealed record ArchivePlanCommand(Guid Id) : ICommand<Result<PlanDto>>;

/// <summary>Makes a published trial or free plan the one currently offered for its product (replacing the previous one).</summary>
public sealed record SetCurrentPlanCommand(Guid Id) : ICommand<Result<PlanDto>>;

/// <summary>Stops offering a trial or free plan; devices then get no trial / free plan for the product until another is chosen.</summary>
public sealed record ClearCurrentPlanCommand(Guid Id) : ICommand<Result<PlanDto>>;

/// <summary>Deletes a draft plan that nothing was ever sold on. Published plans are archived instead, so history stays intact.</summary>
public sealed record DeletePlanCommand(Guid Id) : ICommand<Result>;

public sealed record CreatePlanVersionCommand(Guid Id) : ICommand<Result<PlanDto>>;

// ---------- Query handlers ----------

internal sealed class ListPlansHandler(IAppDbContext db, ITenantContext scope) : IQueryHandler<ListPlansQuery, PagedResult<PlanDto>>
{
    public async Task<PagedResult<PlanDto>> HandleAsync(ListPlansQuery query, CancellationToken ct)
    {
        var page = query.Page;
        var q = db.Plans.AsQueryable();
        if (query.ProductId is not null) q = q.Where(p => p.ProductId == query.ProductId);
        if (query.Status is not null) q = q.Where(p => p.Status == query.Status);
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
        return new PagedResult<PlanDto>(plans.Select(x => CatalogReads.ToDto(x.pl, x.Code, x.Name)).ToList(), total, page.SafePage, page.SafePageSize);
    }
}

internal sealed class GetPlanHandler(IAppDbContext db) : IQueryHandler<GetPlanQuery, Result<PlanDto>>
{
    public Task<Result<PlanDto>> HandleAsync(GetPlanQuery query, CancellationToken ct) => CatalogReads.GetPlanAsync(db, query.Id, ct);
}

internal sealed class GetPlanEntitlementsHandler(IAppDbContext db) : IQueryHandler<GetPlanEntitlementsQuery, Result<Entitlements>>
{
    public async Task<Result<Entitlements>> HandleAsync(GetPlanEntitlementsQuery query, CancellationToken ct)
    {
        var x = await db.Plans.Where(p => p.Id == query.PlanId)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code })
            .FirstOrDefaultAsync(ct);
        return x is null ? AppErrors.NotFound("Plan") : x.pl.ToEntitlements(x.Code);
    }
}

// ---------- Command handlers ----------

internal sealed class CreatePlanHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<CreatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(CreatePlanCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null) return AppErrors.NotFound("Product");
        var code = (request.Code ?? "").Trim().ToUpperInvariant();
        if (await db.Plans.AnyAsync(p => p.ProductId == product.Id && p.Code == code, ct))
            return AppErrors.Duplicate("A plan with this code (use 'new version' to change a published plan)");

        var plan = Plan.Create(product.TenantId, product.Id, request.Code!, request.Name, new Money(request.Price, request.Currency),
            request.DurationDays, request.TrialDays, request.MaxActivations, request.HeartbeatIntervalHours,
            request.OfflineGraceDays, request.Features ?? [], clock.GetUtcNow(), request.Kind);
        db.Plans.Add(plan);
        audit.Add("plan.created", "Plan", plan.Id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, plan.Id, ct);
    }
}

internal sealed class UpdatePlanHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<UpdatePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(UpdatePlanCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Update(request.Name, new Money(request.Price, request.Currency), request.DurationDays, request.TrialDays,
            request.MaxActivations, request.HeartbeatIntervalHours, request.OfflineGraceDays, request.Features ?? [], request.Kind);
        audit.Add("plan.updated", "Plan", command.Id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, command.Id, ct);
    }
}

internal sealed class PublishPlanHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<PublishPlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(PublishPlanCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Publish(clock.GetUtcNow());
        // A new version of the current trial/free plan takes over as the current offer.
        if (plan.Kind != PlanKind.Paid && await db.Plans.AnyAsync(p => p.ProductId == plan.ProductId && p.Code == plan.Code && p.Id != plan.Id && p.IsCurrent, ct))
            await CurrentPlan.MakeCurrentAsync(db, plan, ct);
        audit.Add("plan.published", "Plan", command.Id.ToString(), details: $"{plan.Code} v{plan.Version}", tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, command.Id, ct);
    }
}

internal sealed class ArchivePlanHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<ArchivePlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(ArchivePlanCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.Archive();
        audit.Add("plan.archived", "Plan", command.Id.ToString(), tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, command.Id, ct);
    }
}

internal sealed class SetCurrentPlanHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<SetCurrentPlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(SetCurrentPlanCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        await CurrentPlan.MakeCurrentAsync(db, plan, ct);
        audit.Add("plan.made_current", "Plan", command.Id.ToString(), details: $"{plan.Kind} {plan.Code} v{plan.Version}", tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, command.Id, ct);
    }
}

internal sealed class ClearCurrentPlanHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<ClearCurrentPlanCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(ClearCurrentPlanCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        plan.ClearCurrent();
        audit.Add("plan.current_cleared", "Plan", command.Id.ToString(), details: $"{plan.Kind} {plan.Code} v{plan.Version}", tenantId: plan.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, command.Id, ct);
    }
}

internal sealed class DeletePlanHandler(IAppDbContext db, ICatalogUsageReader usage, IAuditLogger audit) : ICommandHandler<DeletePlanCommand, Result>
{
    public async Task<Result> HandleAsync(DeletePlanCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        if (plan.Status != PlanStatus.Draft)
            return Error.Conflict("PLAN_NOT_DRAFT", "Only draft plans can be deleted; archive a published plan instead.");
        if (await usage.IsPlanInUseAsync(command.Id, ct))
            return Error.Conflict("PLAN_IN_USE", "The plan has subscriptions or licenses; archive it instead.");
        db.Plans.Remove(plan);
        audit.Add("plan.deleted", "Plan", command.Id.ToString(), tenantId: plan.TenantId, details: $"{plan.Code} v{plan.Version}");
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class CreatePlanVersionHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<CreatePlanVersionCommand, Result<PlanDto>>
{
    public async Task<Result<PlanDto>> HandleAsync(CreatePlanVersionCommand command, CancellationToken ct)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (plan is null) return AppErrors.NotFound("Plan");
        if (await db.Plans.AnyAsync(p => p.ProductId == plan.ProductId && p.Code == plan.Code && p.Status == PlanStatus.Draft, ct))
            return Error.Conflict("PLAN_DRAFT_EXISTS", "A draft version of this plan already exists.");
        var max = await db.Plans.Where(p => p.ProductId == plan.ProductId && p.Code == plan.Code).MaxAsync(p => p.Version, ct);
        var draft = plan.NewVersion(max + 1, clock.GetUtcNow());
        db.Plans.Add(draft);
        audit.Add("plan.versioned", "Plan", draft.Id.ToString(), details: $"{draft.Code} v{draft.Version}", tenantId: draft.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetPlanAsync(db, draft.Id, ct);
    }
}

internal static class CurrentPlan
{
    /// <summary>Makes the plan its product's current offer of its kind, clearing whichever plan held that place.</summary>
    public static async Task MakeCurrentAsync(IAppDbContext db, Plan plan, CancellationToken ct)
    {
        plan.MakeCurrent();
        foreach (var other in await db.Plans.Where(p => p.ProductId == plan.ProductId && p.Kind == plan.Kind && p.Id != plan.Id && p.IsCurrent).ToListAsync(ct))
            other.ClearCurrent();
    }
}
