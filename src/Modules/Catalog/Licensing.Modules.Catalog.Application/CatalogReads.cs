using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Catalog;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Modules.Catalog.Application;

/// <summary>Read projections shared by the Catalog query handlers and by commands that return the saved record.</summary>
internal static class CatalogReads
{
    // The platform list is split on the client in this final projection. License counts come from the usage reader.
    private static IQueryable<ProductDto> ProjectProducts(IAppDbContext db, IQueryable<Product> q) => q.Select(p => new ProductDto(
        p.Id, p.TenantId, p.Code, p.Name, p.Description, p.IsActive, p.CreatedAt,
        db.Plans.Count(pl => pl.ProductId == p.Id && pl.Status == PlanStatus.Published),
        db.Plans.Count(pl => pl.ProductId == p.Id && pl.Status != PlanStatus.Archived),
        0,
        p.PlatformsValue == "" ? new List<string>() : p.PlatformsValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
        p.Icon, p.ImageId));

    public static async Task<PagedResult<ProductDto>> ListProductsAsync(IAppDbContext db, ICatalogUsageReader usage, IQueryable<Product> q,
        PageQuery page, CancellationToken ct)
    {
        var paged = await ProjectProducts(db, q).ToPagedAsync(page, ct);
        return paged with { Items = await WithLicenseCountsAsync(paged.Items, usage, ct) };
    }

    public static async Task<Result<ProductDto>> GetProductAsync(IAppDbContext db, ICatalogUsageReader usage, Guid id, CancellationToken ct)
    {
        var dto = await ProjectProducts(db, db.Products.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);
        return dto is null ? AppErrors.NotFound("Product") : (await WithLicenseCountsAsync([dto], usage, ct))[0];
    }

    private static async Task<IReadOnlyList<ProductDto>> WithLicenseCountsAsync(IReadOnlyList<ProductDto> items, ICatalogUsageReader usage,
        CancellationToken ct)
    {
        if (items.Count == 0) return items;
        var counts = await usage.CountLicensesByProductAsync(items.Select(i => i.Id).ToList(), ct);
        return items.Select(i => i with { Licenses = counts.GetValueOrDefault(i.Id) }).ToList();
    }

    public static async Task<Result<PlanDto>> GetPlanAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var x = await db.Plans.Where(p => p.Id == id)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code, p.Name })
            .FirstOrDefaultAsync(ct);
        return x is null ? AppErrors.NotFound("Plan") : ToDto(x.pl, x.Code, x.Name);
    }

    public static PlanDto ToDto(Plan pl, string productCode, string productName) => new(
        pl.Id, pl.TenantId, pl.ProductId, productCode, productName, pl.Code, pl.Name, pl.Version, pl.Status,
        pl.Price.Amount, pl.Price.Currency, pl.DurationDays, pl.TrialDays, pl.MaxActivations, pl.HeartbeatIntervalHours,
        pl.OfflineGraceDays, pl.Features, pl.CreatedAt, pl.PublishedAt, pl.Kind, pl.IsCurrent);
}
