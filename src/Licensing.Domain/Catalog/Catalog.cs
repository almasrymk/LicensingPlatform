using Licensing.Domain.Common;
using Licensing.SharedKernel;

namespace Licensing.Domain.Catalog;

public sealed class Product : AggregateRoot, ITenantOwned
{
    private Product() { Code = Name = null!; }

    public Guid TenantId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Supported platforms, stored as a ';' separated list.</summary>
    public string PlatformsValue { get; private set; } = "";

    public static readonly string[] KnownPlatforms = ["Windows", "Linux", "macOS", "Android", "iOS", "Web"];

    public IReadOnlyList<string> Platforms =>
        PlatformsValue.Length == 0 ? [] : PlatformsValue.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public static Product Create(Guid tenantId, string code, string name, string? description, DateTimeOffset now, IEnumerable<string>? platforms = null)
    {
        var product = new Product
        {
            TenantId = tenantId,
            Code = Guard.NotEmpty(code, "Code", 32).ToUpperInvariant(),
            Name = Guard.NotEmpty(name, "Name"),
            Description = description?.Trim(),
            IsActive = true,
            CreatedAt = now,
        };
        product.SetPlatforms(platforms ?? []);
        return product;
    }

    public void Update(string name, string? description, bool isActive, IEnumerable<string>? platforms = null)
    {
        Name = Guard.NotEmpty(name, "Name");
        Description = description?.Trim();
        IsActive = isActive;
        if (platforms is not null) SetPlatforms(platforms);
    }

    private void SetPlatforms(IEnumerable<string> platforms)
    {
        var list = new List<string>();
        foreach (var p in platforms.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var known = KnownPlatforms.FirstOrDefault(k => k.Equals(p.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException(Error.Validation("VALIDATION_FAILED", $"Unknown platform '{p}'."));
            if (!list.Contains(known)) list.Add(known);
        }
        PlatformsValue = string.Join(';', list);
    }
}

public enum PlanStatus { Draft = 1, Published = 2, Archived = 3 }

/// <summary>What a plan grants, in a form client software can read.</summary>
public sealed record Entitlements(
    string ProductCode,
    string PlanCode,
    int PlanVersion,
    IReadOnlyList<string> Features,
    int? MaxActivations,
    int? DurationDays,
    int HeartbeatIntervalHours,
    int OfflineGraceDays);

/// <summary>
/// A sellable plan of a product. A published plan is immutable; changing it means publishing a new version,
/// so licenses issued earlier keep the entitlements snapshot they were issued with.
/// </summary>
public sealed class Plan : AggregateRoot, ITenantOwned
{
    private Plan() { Code = Name = FeaturesValue = null!; Price = null!; }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public int Version { get; private set; }
    public PlanStatus Status { get; private set; }
    public Money Price { get; private set; }
    /// <summary>Null means lifetime.</summary>
    public int? DurationDays { get; private set; }
    public int? TrialDays { get; private set; }
    /// <summary>Null means unlimited.</summary>
    public int? MaxActivations { get; private set; }
    public int HeartbeatIntervalHours { get; private set; }
    public int OfflineGraceDays { get; private set; }
    /// <summary>Feature codes, stored as a ';' separated list.</summary>
    public string FeaturesValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyList<string> Features =>
        FeaturesValue.Length == 0 ? [] : FeaturesValue.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public PlanLimit ActivationLimit => PlanLimit.From(MaxActivations);
    public bool IsLifetime => DurationDays is null;

    public static Plan Create(
        Guid tenantId, Guid productId, string code, string name, Money price, int? durationDays, int? trialDays,
        int? maxActivations, int heartbeatIntervalHours, int offlineGraceDays, IEnumerable<string> features, DateTimeOffset now)
    {
        var plan = new Plan
        {
            TenantId = tenantId,
            ProductId = productId,
            Code = Guard.NotEmpty(code, "Code", 32).ToUpperInvariant(),
            Version = 1,
            Status = PlanStatus.Draft,
            CreatedAt = now,
        };
        plan.Apply(name, price, durationDays, trialDays, maxActivations, heartbeatIntervalHours, offlineGraceDays, features);
        return plan;
    }

    public void Update(string name, Money price, int? durationDays, int? trialDays, int? maxActivations,
        int heartbeatIntervalHours, int offlineGraceDays, IEnumerable<string> features)
    {
        if (Status != PlanStatus.Draft)
            throw new DomainException(Error.Conflict("PLAN_NOT_DRAFT", "Only draft plans can be edited. Create a new version instead."));
        Apply(name, price, durationDays, trialDays, maxActivations, heartbeatIntervalHours, offlineGraceDays, features);
    }

    private void Apply(string name, Money price, int? durationDays, int? trialDays, int? maxActivations,
        int heartbeatIntervalHours, int offlineGraceDays, IEnumerable<string> features)
    {
        if (durationDays is < 1)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Duration must be at least one day (or empty for lifetime)."));
        if (trialDays is < 0 or > 90)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Trial days must be between 0 and 90."));
        if (heartbeatIntervalHours is < 1 or > 24 * 30)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Heartbeat interval must be between 1 hour and 30 days."));
        if (offlineGraceDays is < 0 or > 365)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Offline grace period must be between 0 and 365 days."));

        Name = Guard.NotEmpty(name, "Name");
        Price = price;
        DurationDays = durationDays;
        TrialDays = trialDays is 0 ? null : trialDays;
        MaxActivations = PlanLimit.From(maxActivations).Value;
        HeartbeatIntervalHours = heartbeatIntervalHours;
        OfflineGraceDays = offlineGraceDays;
        FeaturesValue = string.Join(';', features.Select(f => FeatureCode.Create(f).Value).Distinct().Order());
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != PlanStatus.Draft)
            throw new DomainException(Error.Conflict("PLAN_NOT_DRAFT", "Only draft plans can be published."));
        Status = PlanStatus.Published;
        PublishedAt = now;
        Raise(new PlanPublished(Id, TenantId, Code, Version));
    }

    public void Archive()
    {
        if (Status == PlanStatus.Archived)
            throw new DomainException(Error.Conflict("PLAN_ARCHIVED", "Plan is already archived."));
        Status = PlanStatus.Archived;
    }

    /// <summary>Starts a new draft version that copies this plan.</summary>
    public Plan NewVersion(int nextVersion, DateTimeOffset now)
    {
        // Value objects are copied, never shared between aggregates.
        var draft = Create(TenantId, ProductId, Code, Name, new Money(Price.Amount, Price.Currency), DurationDays, TrialDays, MaxActivations,
            HeartbeatIntervalHours, OfflineGraceDays, Features, now);
        draft.Version = nextVersion;
        return draft;
    }

    public Entitlements ToEntitlements(string productCode) => new(
        productCode, Code, Version, Features, MaxActivations, DurationDays, HeartbeatIntervalHours, OfflineGraceDays);
}

public sealed record PlanPublished(Guid PlanId, Guid TenantId, string Code, int Version) : DomainEvent;
