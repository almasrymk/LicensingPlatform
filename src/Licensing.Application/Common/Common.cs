using Licensing.Application.Abstractions;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record PageQuery(int Page = 1, int PageSize = 20, string? Search = null)
{
    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, 200);
}

public static class AppErrors
{
    public static Error NotFound(string entity) => Error.NotFound("NOT_FOUND", $"{entity} was not found.");
    public static readonly Error Forbidden = Error.Forbidden("AUTH_FORBIDDEN", "You do not have permission to perform this action.");
    public static readonly Error TenantRequired = Error.Validation("TENANT_REQUIRED",
        "A tenant must be selected for this action. Platform admins pass tenantId or the X-Tenant-Id header.");
    public static Error Duplicate(string what) => Error.Conflict("DUPLICATE", $"{what} already exists.");
    public static readonly Error ConcurrencyConflict = Error.Conflict("CONCURRENCY_CONFLICT", "The record was changed by someone else. Reload and try again.");
}

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page.SafePage, page.SafePageSize);
    }
}

public static class TenantResolution
{
    /// <summary>
    /// Resolves the tenant a write applies to. Tenant users always write to their own tenant, whatever the request says;
    /// only platform admins may choose a tenant, explicitly.
    /// </summary>
    public static Result<Guid> ResolveWriteTenant(this ICurrentUser user, ITenantContext scope, Guid? requested)
    {
        if (!user.IsPlatformAdmin)
            return user.TenantId is { } own ? own : AppErrors.Forbidden;
        if (requested is { } r && r != Guid.Empty) return r;
        return scope.TenantId is { } scoped ? scoped : AppErrors.TenantRequired;
    }
}
