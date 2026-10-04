using Licensing.SharedKernel;

namespace Licensing.Domain.Tenants;

public enum TenantStatus { Active = 1, Suspended = 2 }

/// <summary>
/// A tenant is a company using the platform to sell and license its own software (ADR-009).
/// Every tenant-owned row is isolated by TenantId.
/// </summary>
public sealed class Tenant : AggregateRoot
{
    private Tenant() { Name = Code = null!; }

    public string Name { get; private set; }
    public string Code { get; private set; }
    public string? ContactEmail { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspensionReason { get; private set; }

    /// <summary>Uploaded logo/photo (see MediaFile), or null.</summary>
    public Guid? ImageId { get; private set; }

    public void SetImage(Guid? imageId) => ImageId = imageId;

    public static Tenant Create(string name, string code, string? contactEmail, DateTimeOffset now)
    {
        var tenant = new Tenant
        {
            Name = Guard.NotEmpty(name, "Name"),
            Code = Guard.NotEmpty(code, "Code", 32).ToUpperInvariant(),
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : Common.EmailAddress.Create(contactEmail).Value,
            Status = TenantStatus.Active,
            CreatedAt = now,
        };
        tenant.Raise(new TenantCreated(tenant.Id));
        return tenant;
    }

    public void Rename(string name, string? contactEmail)
    {
        Name = Guard.NotEmpty(name, "Name");
        ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : Common.EmailAddress.Create(contactEmail).Value;
    }

    /// <summary>Suspending a tenant blocks its users from signing in and its licenses from validating.</summary>
    public void Suspend(string? reason, DateTimeOffset now)
    {
        if (Status == TenantStatus.Suspended)
            throw new DomainException(Error.Conflict("TENANT_ALREADY_SUSPENDED", "Tenant is already suspended."));
        Status = TenantStatus.Suspended;
        SuspendedAt = now;
        SuspensionReason = reason;
        Raise(new TenantSuspended(Id));
    }

    public void Resume()
    {
        if (Status != TenantStatus.Suspended)
            throw new DomainException(Error.Conflict("TENANT_NOT_SUSPENDED", "Tenant is not suspended."));
        Status = TenantStatus.Active;
        SuspendedAt = null;
        SuspensionReason = null;
        Raise(new TenantResumed(Id));
    }
}

public sealed record TenantCreated(Guid TenantId) : DomainEvent;
public sealed record TenantSuspended(Guid TenantId) : DomainEvent;
public sealed record TenantResumed(Guid TenantId) : DomainEvent;
