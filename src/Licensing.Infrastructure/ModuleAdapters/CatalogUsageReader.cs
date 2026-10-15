using Licensing.Infrastructure.Persistence;
using Licensing.Modules.Catalog.Application;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Infrastructure.ModuleAdapters;

/// <summary>
/// Answers the Catalog module's questions about licenses and subscriptions. It goes through the same scoped context,
/// so the caller's tenant and customer filters apply exactly as before.
/// </summary>
public sealed class CatalogUsageReader(AppDbContext db) : ICatalogUsageReader
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountLicensesByProductAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        await db.Licenses.Where(l => productIds.Contains(l.ProductId))
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, ct);

    public async Task<bool> IsPlanInUseAsync(Guid planId, CancellationToken ct) =>
        await db.Subscriptions.AnyAsync(s => s.PlanId == planId, ct) || await db.Licenses.AnyAsync(l => l.PlanId == planId, ct);
}
