using Licensing.Api.Infrastructure;
using Licensing.Application.Abstractions;
using Licensing.Application.Media;
using Licensing.Domain.Identity;
using Licensing.Domain.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Licensing.Api.Controllers;

/// <summary>Logos and photos. Uploads need the same permission as editing the record; images are served by unguessable id.</summary>
[ApiController]
[Tags("Media")]
public sealed class MediaController(MediaService media, ICurrentUser me) : ControllerBase
{
    private const long RequestLimit = MediaFile.MaxBytes + 64 * 1024;

    /// <summary>Serves an uploaded image. Ids are random, so the URL can be used directly in &lt;img&gt;.</summary>
    [HttpGet("/api/v1/media/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var file = await media.GetAsync(id, ct);
        if (file is null) return NotFound();
        Response.Headers.CacheControl = "public, max-age=86400, immutable";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'";
        return File(file.Data, file.ContentType);
    }

    [HttpPut("/api/v1/tenants/{id:guid}/image")]
    [HasPermission(Permissions.TenantsManage)]
    [RequestSizeLimit(RequestLimit)]
    public Task<IActionResult> Tenant(Guid id, IFormFile file, CancellationToken ct) => Upload("tenant", id, file, ct);

    [HttpPut("/api/v1/customers/{id:guid}/image")]
    [HasPermission(Permissions.CustomersManage)]
    [RequestSizeLimit(RequestLimit)]
    public Task<IActionResult> Customer(Guid id, IFormFile file, CancellationToken ct) => Upload("customer", id, file, ct);

    [HttpPut("/api/v1/products/{id:guid}/image")]
    [HasPermission(Permissions.CatalogManage)]
    [RequestSizeLimit(RequestLimit)]
    public Task<IActionResult> Product(Guid id, IFormFile file, CancellationToken ct) => Upload("product", id, file, ct);

    [HttpPut("/api/v1/users/{id:guid}/image")]
    [HasPermission(Permissions.UsersManage)]
    [RequestSizeLimit(RequestLimit)]
    public Task<IActionResult> User(Guid id, IFormFile file, CancellationToken ct) => Upload("user", id, file, ct);

    /// <summary>Any signed-in user can set their own photo.</summary>
    [HttpPut("/api/v1/auth/me/image")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    [RequestSizeLimit(RequestLimit)]
    public Task<IActionResult> Me(IFormFile file, CancellationToken ct) => Upload("user", me.UserId!.Value, file, ct);

    [HttpDelete("/api/v1/tenants/{id:guid}/image")]
    [HasPermission(Permissions.TenantsManage)]
    public async Task<IActionResult> RemoveTenant(Guid id, CancellationToken ct) => this.ToActionResult(await media.RemoveImageAsync("tenant", id, ct));

    [HttpDelete("/api/v1/customers/{id:guid}/image")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> RemoveCustomer(Guid id, CancellationToken ct) => this.ToActionResult(await media.RemoveImageAsync("customer", id, ct));

    [HttpDelete("/api/v1/products/{id:guid}/image")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> RemoveProduct(Guid id, CancellationToken ct) => this.ToActionResult(await media.RemoveImageAsync("product", id, ct));

    [HttpDelete("/api/v1/users/{id:guid}/image")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> RemoveUser(Guid id, CancellationToken ct) => this.ToActionResult(await media.RemoveImageAsync("user", id, ct));

    [HttpDelete("/api/v1/auth/me/image")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    public async Task<IActionResult> RemoveMe(CancellationToken ct) => this.ToActionResult(await media.RemoveImageAsync("user", me.UserId!.Value, ct));

    private async Task<IActionResult> Upload(string ownerType, Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return this.ToProblem(Licensing.SharedKernel.Error.Validation("IMAGE_INVALID", "Choose an image file."));
        await using var stream = file.OpenReadStream();
        return this.ToActionResult(await media.SetImageAsync(ownerType, id, stream, file.FileName, ct));
    }
}
