using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.BuildingBlocks.Application;
using Licensing.Domain.Catalog;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Modules.Catalog.Application.Products;

public sealed record ListProductsQuery(PageQuery Page) : IQuery<PagedResult<ProductDto>>;

public sealed record GetProductQuery(Guid Id) : IQuery<Result<ProductDto>>;

public sealed record CreateProductCommand(SaveProductRequest Request) : ICommand<Result<ProductDto>>;

public sealed record UpdateProductCommand(Guid Id, SaveProductRequest Request) : ICommand<Result<ProductDto>>;

internal sealed class ListProductsHandler(IAppDbContext db, ICatalogUsageReader usage) : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    public Task<PagedResult<ProductDto>> HandleAsync(ListProductsQuery query, CancellationToken ct)
    {
        var q = db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Page.Search))
            q = q.Where(p => p.Name.Contains(query.Page.Search) || p.Code.Contains(query.Page.Search));
        return CatalogReads.ListProductsAsync(db, usage, q.OrderBy(p => p.Name), query.Page, ct);
    }
}

internal sealed class GetProductHandler(IAppDbContext db, ICatalogUsageReader usage) : IQueryHandler<GetProductQuery, Result<ProductDto>>
{
    public Task<Result<ProductDto>> HandleAsync(GetProductQuery query, CancellationToken ct) =>
        CatalogReads.GetProductAsync(db, usage, query.Id, ct);
}

internal sealed class CreateProductHandler(
    IAppDbContext db, ICatalogUsageReader usage, ICurrentUser me, ITenantContext scope, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<CreateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> HandleAsync(CreateProductCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var tenant = me.ResolveWriteTenant(scope, request.TenantId);
        if (tenant.IsFailure) return tenant.Error!;
        var product = Product.Create(tenant.Value, request.Code, request.Name, request.Description, clock.GetUtcNow(), request.Platforms);
        product.SetIcon(request.Icon);
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant.Value && p.Code == product.Code, ct))
            return AppErrors.Duplicate("A product with this code");
        db.Products.Add(product);
        audit.Add("product.created", "Product", product.Id.ToString(), tenantId: product.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetProductAsync(db, usage, product.Id, ct);
    }
}

internal sealed class UpdateProductHandler(IAppDbContext db, ICatalogUsageReader usage, IAuditLogger audit)
    : ICommandHandler<UpdateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> HandleAsync(UpdateProductCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (product is null) return AppErrors.NotFound("Product");
        product.Update(request.Name, request.Description, request.IsActive, request.Platforms);
        if (request.Icon is not null) product.SetIcon(request.Icon == "" ? null : request.Icon);
        audit.Add("product.updated", "Product", command.Id.ToString(), tenantId: product.TenantId);
        await db.SaveChangesAsync(ct);
        return await CatalogReads.GetProductAsync(db, usage, command.Id, ct);
    }
}
