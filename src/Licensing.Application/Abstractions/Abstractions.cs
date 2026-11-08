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
    DbSet<TrialGrant> TrialGrants { get; }
    DbSet<SigningKey> SigningKeys { get; }
    DbSet<ApiClient> ApiClients { get; }
    DbSet<Webhook> Webhooks { get; }
    DbSet<AuditRecord> AuditRecords { get; }
    DbSet<global::Licensing.Domain.Reporting.UsageDaily> UsageDaily { get; }
    DbSet<global::Licensing.Domain.Media.MediaFile> MediaFiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets every pending change, after a save lost a race on a unique key.</summary>
    void DiscardChanges();

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (or inside the current one), committing when it returns
    /// and rolling back when it throws.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct);

    /// <summary>
    /// E3 (R1/R2): decides whether <paramref name="deviceId"/> may take a seat on the license. Must run inside
    /// <see cref="InTransactionAsync{T}"/> together with the save of the activation: it locks the subscription until the
    /// transaction ends, so concurrent activations of one subscription cannot exceed its limit. A device already active on
    /// another license of the subscription needs no new subscription slot. Returns false when a limit is reached.
    /// </summary>
    Task<bool> TryReserveActivationSlotAsync(Guid licenseId, string deviceId, CancellationToken ct);

    /// <summary>Recomputes the device counters of the license's subscription (and its licenses) from the activations.</summary>
    Task RecountDevicesAsync(Guid licenseId, CancellationToken ct);

    /// <summary>B4: the same recount for one subscription (after its plan, and so its limit, changed).</summary>
    Task RecountSubscriptionDevicesAsync(Guid subscriptionId, CancellationToken ct);

    /// <summary>B4: whether the license's subscription is over its plan's device limit.</summary>
    Task<bool> IsOverCapacityAsync(Guid licenseId, CancellationToken ct);

    /// <summary>Repairs subscriptions whose stored counters disagree with their activations. Returns the rows corrected.</summary>
    Task<int> ReconcileDeviceCountersAsync(int batchSize, CancellationToken ct);
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

/// <summary>
/// Lets an anonymous, product-key-authenticated request act inside the tenant that owns the key (INT-002). Only
/// <see cref="Licensing.DeviceLicensingService"/> uses it, after resolving the key itself.
/// </summary>
public interface ITenantScope
{
    void RunAsSystem(Guid? tenantId);
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
    IReadOnlyList<string> Features, DateTimeOffset? LicenseExpiresAt, DateTimeOffset CheckAfter, DateTimeOffset OfflineValidUntil,
    Guid? CustomerId = null);

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
