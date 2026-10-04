using Licensing.SharedKernel;

namespace Licensing.Domain.Media;

/// <summary>
/// An uploaded image (logo of a tenant, customer or product, or a user's photo). Stored in the database so it is
/// backed up with everything else. The id is random (not time-ordered) because images are served by id without a token.
/// </summary>
public sealed class MediaFile : Entity
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly string[] OwnerTypes = ["tenant", "customer", "product", "user"];

    private MediaFile() { OwnerType = ContentType = FileName = null!; Data = null!; }

    public Guid? TenantId { get; private set; }
    public string OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public string ContentType { get; private set; }
    public string FileName { get; private set; }
    public int Size { get; private set; }
    public byte[] Data { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MediaFile Create(Guid? tenantId, string ownerType, Guid ownerId, string contentType, string fileName, byte[] data, DateTimeOffset now)
    {
        if (!OwnerTypes.Contains(ownerType))
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Unknown image owner."));
        if (data.Length == 0 || data.Length > MaxBytes)
            throw new DomainException(Error.Validation("IMAGE_TOO_LARGE", "Images must be smaller than 2 MB."));
        var file = new MediaFile
        {
            TenantId = tenantId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            ContentType = contentType,
            FileName = Path.GetFileName(fileName) is { Length: > 0 } n ? n[..Math.Min(n.Length, 200)] : "image",
            Size = data.Length,
            Data = data,
            CreatedAt = now,
        };
        file.Id = Guid.NewGuid();
        return file;
    }

    /// <summary>Detects the image type from its first bytes; only raster formats are accepted (no SVG, which can carry script).</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return "image/jpeg";
        if (d.Length >= 6 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return "image/gif";
        if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46 && d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50)
            return "image/webp";
        return null;
    }
}
