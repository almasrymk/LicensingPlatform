using Licensing.Domain.Common;
using Licensing.SharedKernel;

namespace Licensing.Domain.Identity;

public sealed class User : AggregateRoot
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private User() { Email = FullName = PasswordHash = Role = null!; }

    /// <summary>Null only for platform administrators.</summary>
    public Guid? TenantId { get; private set; }
    /// <summary>Set only for <see cref="Roles.CustomerUser"/>.</summary>
    public Guid? CustomerId { get; private set; }
    public string Email { get; private set; }
    public string FullName { get; private set; }
    public string PasswordHash { get; private set; }
    public string Role { get; private set; }
    public bool IsActive { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public string PreferredLanguage { get; private set; } = "ar";

    /// <summary>Uploaded logo/photo (see MediaFile), or null.</summary>
    public Guid? ImageId { get; private set; }

    public void SetImage(Guid? imageId) => ImageId = imageId;

    public static User CreatePlatformAdmin(string email, string fullName, string passwordHash, DateTimeOffset now) =>
        Create(null, null, email, fullName, passwordHash, Roles.PlatformAdmin, now);

    public static User CreateTenantUser(Guid tenantId, string email, string fullName, string passwordHash, string role, DateTimeOffset now)
    {
        EnsureTenantRole(role);
        return Create(tenantId, null, email, fullName, passwordHash, role, now);
    }

    public static User CreateCustomerUser(Guid tenantId, Guid customerId, string email, string fullName, string passwordHash, DateTimeOffset now) =>
        Create(tenantId, customerId, email, fullName, passwordHash, Roles.CustomerUser, now);

    private static User Create(Guid? tenantId, Guid? customerId, string email, string fullName, string passwordHash, string role, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        CustomerId = customerId,
        Email = EmailAddress.Create(email).Value,
        FullName = Guard.NotEmpty(fullName, "Full name"),
        PasswordHash = Guard.NotEmpty(passwordHash, "Password", 512),
        Role = role,
        IsActive = true,
        CreatedAt = now,
    };

    private static void EnsureTenantRole(string role)
    {
        if (role is not (Roles.TenantAdmin or Roles.TenantOperator))
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Role must be TenantAdmin or TenantOperator."));
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is not null && LockoutEnd > now;

    public void RegisterFailedLogin(DateTimeOffset now)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedAttempts)
        {
            LockoutEnd = now.Add(LockoutDuration);
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void ChangePassword(string passwordHash) => PasswordHash = Guard.NotEmpty(passwordHash, "Password", 512);

    public void Update(string fullName, string? role)
    {
        FullName = Guard.NotEmpty(fullName, "Full name");
        if (role is not null && role != Role)
        {
            if (Role is not (Roles.TenantAdmin or Roles.TenantOperator))
                throw new DomainException(Error.Validation("VALIDATION_FAILED", "Only tenant staff roles can be changed."));
            EnsureTenantRole(role);
            Role = role;
        }
    }

    public void SetLanguage(string language) => PreferredLanguage = language is "en" ? "en" : "ar";

    public void Deactivate() => IsActive = false;

    public void Activate()
    {
        IsActive = true;
        LockoutEnd = null;
        FailedLoginCount = 0;
    }
}

/// <summary>Refresh tokens are stored hashed and rotated on every use; reusing a rotated token revokes the whole family.</summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { TokenHash = null!; }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; }
    public Guid FamilyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, Guid familyId, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        UserId = userId,
        TokenHash = tokenHash,
        FamilyId = familyId,
        CreatedAt = now,
        ExpiresAt = now.Add(lifetime),
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, Guid? replacedBy = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedBy;
    }
}
