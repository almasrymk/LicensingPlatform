using Licensing.Domain.Catalog;

namespace Licensing.Modules.Catalog.Application;

public sealed record ProductDto(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, DateTimeOffset CreatedAt,
    int PublishedPlans, int Plans, int Licenses, IReadOnlyList<string> Platforms, string? Icon, Guid? ImageId)
{
    public string ImageUrl => Licensing.Application.Media.MediaService.UrlFor(ImageId);
}
public sealed record SaveProductRequest(string Code, string Name, string? Description, bool IsActive = true, Guid? TenantId = null,
    IReadOnlyList<string>? Platforms = null, string? Icon = null);

public sealed record PlanDto(
    Guid Id, Guid TenantId, Guid ProductId, string ProductCode, string ProductName, string Code, string Name, int Version,
    PlanStatus Status, decimal Price, string Currency, int? DurationDays, int? TrialDays, int? MaxActivations,
    int HeartbeatIntervalHours, int OfflineGraceDays, IReadOnlyList<string> Features, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt,
    PlanKind Kind = PlanKind.Paid, bool IsCurrent = false);

public sealed record SavePlanRequest(
    Guid ProductId, string Code, string Name, decimal Price, string Currency, int? DurationDays, int? TrialDays,
    int? MaxActivations, int HeartbeatIntervalHours, int OfflineGraceDays, IReadOnlyList<string>? Features, PlanKind Kind = PlanKind.Paid);

/// <summary>
/// What Catalog needs to know from the licensing and subscription modules. Implemented outside the module, so Catalog
/// never queries another module's tables itself.
/// </summary>
public interface ICatalogUsageReader
{
    /// <summary>Licenses issued per product, for the given products (missing products have none).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountLicensesByProductAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    /// <summary>Whether any subscription or license was ever created from the plan.</summary>
    Task<bool> IsPlanInUseAsync(Guid planId, CancellationToken ct);
}
