using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Media;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Media;

public sealed record ImageDto(Guid Id, string Url);
public sealed record MediaContent(byte[] Data, string ContentType, string FileName);

/// <summary>
/// Logos and photos for tenants, customers, products and users. The owner is loaded through the normal tenant-scoped
/// queries, so nobody can attach an image to a record they cannot see. Only PNG, JPEG, GIF and WebP up to 2 MB.
/// </summary>
public sealed class MediaService(IAppDbContext db, ITenantContext scope, IAuditLogger audit, TimeProvider clock)
{
    public static string UrlFor(Guid? imageId) => imageId is { } id ? $"/api/v1/media/{id}" : "";

    public async Task<Result<ImageDto>> SetImageAsync(string ownerType, Guid ownerId, Stream content, string fileName, CancellationToken ct)
    {
        var data = await ReadLimitedAsync(content, ct);
        if (data is null) return Error.Validation("IMAGE_TOO_LARGE", "Images must be smaller than 2 MB.");
        var contentType = MediaFile.SniffContentType(data);
        if (contentType is null) return Error.Validation("IMAGE_INVALID", "Only PNG, JPEG, GIF or WebP images are accepted.");

        var owner = await FindOwnerAsync(ownerType, ownerId, ct);
        if (owner is null) return AppErrors.NotFound("Record");

        var file = MediaFile.Create(owner.Value.TenantId, ownerType, ownerId, contentType, fileName, data, clock.GetUtcNow());
        db.MediaFiles.Add(file);
        var previous = owner.Value.CurrentImage;
        owner.Value.Set(file.Id);
        if (previous is { } old) await db.MediaFiles.Where(m => m.Id == old).ExecuteDeleteAsync(ct);
        audit.Add($"{ownerType}.image_changed", ownerType, ownerId.ToString(), tenantId: owner.Value.TenantId);
        await db.SaveChangesAsync(ct);
        return new ImageDto(file.Id, UrlFor(file.Id));
    }

    public async Task<Result> RemoveImageAsync(string ownerType, Guid ownerId, CancellationToken ct)
    {
        var owner = await FindOwnerAsync(ownerType, ownerId, ct);
        if (owner is null) return AppErrors.NotFound("Record");
        if (owner.Value.CurrentImage is { } old)
        {
            owner.Value.Set(null);
            await db.MediaFiles.Where(m => m.Id == old).ExecuteDeleteAsync(ct);
            audit.Add($"{ownerType}.image_removed", ownerType, ownerId.ToString(), tenantId: owner.Value.TenantId);
            await db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    public async Task<MediaContent?> GetAsync(Guid id, CancellationToken ct) =>
        await db.MediaFiles.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new MediaContent(m.Data, m.ContentType, m.FileName)).FirstOrDefaultAsync(ct);

    private readonly record struct Owner(Guid? TenantId, Guid? CurrentImage, Action<Guid?> Set);

    private async Task<Owner?> FindOwnerAsync(string ownerType, Guid id, CancellationToken ct)
    {
        switch (ownerType)
        {
            case "tenant":
                var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct);
                return t is null ? null : new Owner(t.Id, t.ImageId, t.SetImage);
            case "customer":
                var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
                return c is null ? null : new Owner(c.TenantId, c.ImageId, c.SetImage);
            case "product":
                var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
                return p is null ? null : new Owner(p.TenantId, p.ImageId, p.SetImage);
            case "user":
                // Users are not tenant-filtered by EF (platform admins have no tenant): apply the scope here.
                var users = db.Users.Where(x => x.Id == id);
                if (!(scope.IsUnrestricted && scope.TenantId is null))
                    users = users.Where(x => x.TenantId == scope.TenantId);
                var u = await users.FirstOrDefaultAsync(ct);
                return u is null ? null : new Owner(u.TenantId, u.ImageId, u.SetImage);
            default:
                return null;
        }
    }

    /// <summary>Reads at most MaxBytes; returns null when the stream is larger.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream content, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MediaFile.MaxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
