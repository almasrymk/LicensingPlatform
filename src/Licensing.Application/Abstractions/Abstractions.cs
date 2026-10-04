using Licensing.Domain.Audit;
using Licensing.Domain.Catalog;
using Licensing.Domain.Customers;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Abstractions;

/// <summary>
/// Unit of work over all modules. Every tenant-owned set is already filtered to the caller's tenant
/// (and to the caller's customer for customer users) by global query filters.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerContact> CustomerContacts { get; }
    DbSet<Product> Products { get; }
    DbSet<Plan> Plans { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<SubscriptionHistory> SubscriptionHistory { get; }
    DbSet<License> Licenses { get; }
    DbSet<LicenseActivation> LicenseActivations { get; }
    DbSet<ActivationAttempt> ActivationAttempts { get; }
    DbSet<SigningKey> SigningKeys { get; }
    DbSet<ApiClient> ApiClients { get; }
    DbSet<Webhook> Webhooks { get; }
    DbSet<AuditRecord> AuditRecords { get; }
    DbSet<global::Licensing.Domain.Reporting.UsageDaily> UsageDaily { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically reserves one activation slot. Returns false when the limit is already reached.</summary>
    Task<bool> TryReserveActivationSlotAsync(Guid licenseId, CancellationToken ct);

    Task ReleaseActivationSlotAsync(Guid licenseId, CancellationToken ct);
}

/// <summary>Who is calling, resolved from the authenticated principal only (never from the request body).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    ActorType ActorType { get; }
    string? ActorId { get; }
    string? ActorName { get; }
    Guid? UserId { get; }
    /// <summary>The caller's own tenant. Null for platform admins.</summary>
    Guid? TenantId { get; }
    /// <summary>Set for customer users: they only see rows of this customer.</summary>
    Guid? CustomerId { get; }
    string? Role { get; }
    bool IsPlatformAdmin { get; }
    IReadOnlyCollection<string> Permissions { get; }
    IReadOnlyCollection<string> Scopes { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
    bool HasPermission(string permission);
}

/// <summary>
/// The tenant scope applied to data access. Platform admins see all tenants unless they explicitly narrow
/// the scope to one tenant; system jobs run with <see cref="IsUnrestricted"/>.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? CustomerId { get; }
    bool IsUnrestricted { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Keyed (HMAC) hashing for high-entropy secrets: product keys, client secrets, refresh tokens.</summary>
public interface ISecretHasher
{
    string Hash(string secret);
    string GenerateToken(int bytes = 32);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    AccessToken CreateUserToken(User user, IReadOnlyCollection<string> permissions);
    AccessToken CreateClientToken(ApiClient client);
}

public interface IProductKeyGenerator
{
    /// <summary>Generates a key from a CSPRNG. Returns the plain key (shown once), its hash and display prefix.</summary>
    (string Key, string Hash, string Prefix) Generate();
    string Normalize(string key);
    string Hash(string normalizedKey);
}

public sealed record LicenseTokenClaims(
    string LicenseNumber, Guid LicenseId, Guid TenantId, string ProductCode, string PlanCode, string DeviceId,
    IReadOnlyList<string> Features, DateTimeOffset? LicenseExpiresAt, DateTimeOffset CheckAfter, DateTimeOffset OfflineValidUntil);

public sealed record SignedLicenseToken(string Token, string Kid, DateTimeOffset ExpiresAt);

public sealed record PublicSigningKey(string Kid, string Algorithm, string PublicKeyPem, string Status, string X, string Y, string Crv);

public interface ILicenseTokenSigner
{
    Task<SignedLicenseToken> SignAsync(LicenseTokenClaims claims, CancellationToken ct);
    Task<IReadOnlyList<PublicSigningKey>> GetPublicKeysAsync(CancellationToken ct);
    Task<string> RotateAsync(CancellationToken ct);
}

/// <summary>Short-lived cache of validate results. Failures fall back to the database.</summary>
public interface ILicenseCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class;
    Task RemoveAsync(string key, CancellationToken ct);
}

public interface IAuditLogger
{
    /// <summary>Adds an audit record to the current unit of work (saved with the business change).</summary>
    void Add(string action, string entityType, string? entityId, bool success = true, string? details = null, Guid? tenantId = null);

    /// <summary>Writes immediately in its own unit of work (used for failures that roll back the business change).</summary>
    Task WriteNowAsync(string action, string entityType, string? entityId, bool success, string? details, Guid? tenantId, CancellationToken ct);
}

public sealed record IdempotentResponse(int StatusCode, string Body);

public interface IIdempotencyStore
{
    Task<IdempotentResponse?> GetAsync(string scope, string key, CancellationToken ct);
    Task SaveAsync(string scope, string key, IdempotentResponse response, CancellationToken ct);
}
