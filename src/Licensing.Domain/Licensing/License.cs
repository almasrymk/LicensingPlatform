using Licensing.Domain.Catalog;
using Licensing.SharedKernel;

namespace Licensing.Domain.Licensing;

public enum LicenseStatus { Active = 1, Suspended = 2, Revoked = 3, Expired = 4 }

public enum ActivationStatus { Active = 1, Deactivated = 2 }

/// <summary>Public error codes of the licensing endpoints (plan section 19.1).</summary>
public static class LicenseErrors
{
    // Deliberately generic: never reveals whether the key does not exist, belongs to another tenant or product.
    public static readonly Error InvalidLicense = Error.NotFound("LIC_INVALID_LICENSE", "The license is not valid.");
    public static readonly Error Suspended = Error.Forbidden("LIC_SUSPENDED", "The license is suspended.");
    public static readonly Error Revoked = Error.Forbidden("LIC_REVOKED", "The license has been revoked.");
    public static readonly Error Expired = Error.Forbidden("LIC_EXPIRED", "The license has expired.");
    public static readonly Error SubscriptionExpired = Error.Forbidden("LIC_SUBSCRIPTION_EXPIRED", "The subscription for this license has expired.");
    public static readonly Error SubscriptionInactive = Error.Forbidden("LIC_SUBSCRIPTION_INACTIVE", "The subscription for this license is not active.");
    public static readonly Error ActivationLimitReached = Error.Conflict("LIC_ACTIVATION_LIMIT_REACHED", "The maximum number of activations has been reached.");
    public static readonly Error DeviceNotActivated = Error.NotFound("LIC_DEVICE_NOT_ACTIVATED", "This device is not activated for the license.");
    public static readonly Error InvalidDevice = Error.Validation("LIC_INVALID_DEVICE", "Device id must be 8-128 characters of letters, digits, '-', '_', ':' or '.'.");
    public static readonly Error InvalidTransition = Error.Conflict("LIC_INVALID_TRANSITION", "This action is not allowed in the license's current state.");
}

public sealed class License : AggregateRoot, ICustomerOwned
{
    private License() { LicenseNumber = ProductKeyHash = ProductKeyPrefix = FeaturesValue = PlanCode = ProductCode = null!; }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid PlanId { get; private set; }
    public string LicenseNumber { get; private set; }
    /// <summary>Keyed hash of the product key; the key itself is only shown once, at issue time.</summary>
    public string ProductKeyHash { get; private set; }
    /// <summary>First group of the key, safe to display to identify it.</summary>
    public string ProductKeyPrefix { get; private set; }
    public LicenseStatus Status { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    // Entitlements snapshot taken at issue time.
    public string ProductCode { get; private set; }
    public string PlanCode { get; private set; }
    public int PlanVersion { get; private set; }
    public string FeaturesValue { get; private set; }
    public int? MaxActivations { get; private set; }
    public int HeartbeatIntervalHours { get; private set; }
    public int OfflineGraceDays { get; private set; }

    /// <summary>Maintained with an atomic conditional UPDATE so concurrent activations never exceed the limit.</summary>
    public int ActiveActivations { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? StatusReason { get; private set; }

    public IReadOnlyList<string> Features =>
        FeaturesValue.Length == 0 ? [] : FeaturesValue.Split(';', StringSplitOptions.RemoveEmptyEntries);

    public static License Issue(
        Guid tenantId, Guid customerId, Guid subscriptionId, Guid productId, Guid planId, string licenseNumber,
        string productKeyHash, string productKeyPrefix, Entitlements entitlements, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        var license = new License
        {
            TenantId = tenantId,
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            ProductId = productId,
            PlanId = planId,
            LicenseNumber = licenseNumber,
            ProductKeyHash = productKeyHash,
            ProductKeyPrefix = productKeyPrefix,
            Status = LicenseStatus.Active,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            ProductCode = entitlements.ProductCode,
            PlanCode = entitlements.PlanCode,
            PlanVersion = entitlements.PlanVersion,
            FeaturesValue = string.Join(';', entitlements.Features),
            MaxActivations = entitlements.MaxActivations,
            HeartbeatIntervalHours = entitlements.HeartbeatIntervalHours,
            OfflineGraceDays = entitlements.OfflineGraceDays,
        };
        license.Raise(new LicenseIssuedV1(license.Id, tenantId, customerId, licenseNumber));
        return license;
    }

    /// <summary>Checks the state rules shared by activate, validate and heartbeat. Returns null when usable.</summary>
    public Error? CheckUsable(DateTimeOffset now) => Status switch
    {
        LicenseStatus.Revoked => LicenseErrors.Revoked,
        LicenseStatus.Suspended => LicenseErrors.Suspended,
        LicenseStatus.Expired => LicenseErrors.Expired,
        _ when ExpiresAt is not null && ExpiresAt <= now => LicenseErrors.Expired,
        _ => null,
    };

    public void Suspend(string? reason)
    {
        if (Status != LicenseStatus.Active) throw new DomainException(LicenseErrors.InvalidTransition);
        Status = LicenseStatus.Suspended;
        StatusReason = reason;
        Raise(new LicenseStatusChangedV1(Id, TenantId, ProductKeyHash, Status));
    }

    public void Resume()
    {
        if (Status != LicenseStatus.Suspended) throw new DomainException(LicenseErrors.InvalidTransition);
        Status = LicenseStatus.Active;
        StatusReason = null;
        Raise(new LicenseStatusChangedV1(Id, TenantId, ProductKeyHash, Status));
    }

    public void Revoke(string? reason, DateTimeOffset now)
    {
        if (Status == LicenseStatus.Revoked) throw new DomainException(LicenseErrors.InvalidTransition);
        Status = LicenseStatus.Revoked;
        RevokedAt = now;
        StatusReason = reason;
        Raise(new LicenseStatusChangedV1(Id, TenantId, ProductKeyHash, Status));
    }

    /// <summary>Idempotent expiry used by the hourly job and by the subscription-expired consumer.</summary>
    public bool TryExpire(DateTimeOffset now, bool force = false)
    {
        if (Status is LicenseStatus.Revoked or LicenseStatus.Expired) return false;
        if (!force && (ExpiresAt is null || ExpiresAt > now)) return false;
        Status = LicenseStatus.Expired;
        StatusReason = force ? "subscription expired" : null;
        Raise(new LicenseStatusChangedV1(Id, TenantId, ProductKeyHash, Status));
        return true;
    }

    /// <summary>Follows the subscription when it is renewed (licenses expired with it come back).</summary>
    public void Reinstate(DateTimeOffset? expiresAt)
    {
        ExpiresAt = expiresAt;
        if (Status == LicenseStatus.Expired)
        {
            Status = LicenseStatus.Active;
            StatusReason = null;
        }
        Raise(new LicenseStatusChangedV1(Id, TenantId, ProductKeyHash, Status));
    }
}

public sealed class LicenseActivation : Entity, ICustomerOwned
{
    private LicenseActivation() { DeviceId = null!; }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LicenseId { get; private set; }
    public string DeviceId { get; private set; }
    public string? DeviceName { get; private set; }
    public string? AppVersion { get; private set; }
    public ActivationStatus Status { get; private set; }
    public DateTimeOffset ActivatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public string? LastIpAddress { get; private set; }

    public static LicenseActivation Create(License license, string deviceId, string? deviceName, string? appVersion, string? ip, DateTimeOffset now) => new()
    {
        TenantId = license.TenantId,
        CustomerId = license.CustomerId,
        LicenseId = license.Id,
        DeviceId = deviceId,
        DeviceName = deviceName?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 200)] : null,
        AppVersion = appVersion?.Trim(),
        Status = ActivationStatus.Active,
        ActivatedAt = now,
        LastHeartbeatAt = now,
        LastIpAddress = ip,
    };

    public void Reactivate(string? deviceName, string? appVersion, string? ip, DateTimeOffset now)
    {
        Status = ActivationStatus.Active;
        DeactivatedAt = null;
        ActivatedAt = now;
        LastHeartbeatAt = now;
        DeviceName = deviceName ?? DeviceName;
        AppVersion = appVersion ?? AppVersion;
        LastIpAddress = ip;
    }

    public void Heartbeat(string? appVersion, string? ip, DateTimeOffset now)
    {
        LastHeartbeatAt = now;
        AppVersion = appVersion ?? AppVersion;
        LastIpAddress = ip;
    }

    public void Deactivate(DateTimeOffset now)
    {
        Status = ActivationStatus.Deactivated;
        DeactivatedAt = now;
    }

    public static bool IsValidDeviceId(string? deviceId) =>
        deviceId is { Length: >= 8 and <= 128 } &&
        deviceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');
}

/// <summary>Every activation attempt, successful or not; feeds the failed-activations report.</summary>
public sealed class ActivationAttempt : Entity, ITenantOwned
{
    private ActivationAttempt() { DeviceId = null!; }

    public Guid TenantId { get; private set; }
    public Guid? LicenseId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string? ProductKeyPrefix { get; private set; }
    public string DeviceId { get; private set; }
    public bool Success { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static ActivationAttempt Record(Guid tenantId, License? license, string? keyPrefix, string deviceId, string? errorCode, string? ip, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        LicenseId = license?.Id,
        CustomerId = license?.CustomerId,
        ProductKeyPrefix = keyPrefix,
        DeviceId = deviceId.Length > 128 ? deviceId[..128] : deviceId,
        Success = errorCode is null,
        ErrorCode = errorCode,
        IpAddress = ip,
        At = now,
    };
}

public sealed record LicenseIssuedV1(Guid LicenseId, Guid TenantId, Guid CustomerId, string LicenseNumber) : DomainEvent;
public sealed record LicenseStatusChangedV1(Guid LicenseId, Guid TenantId, string ProductKeyHash, LicenseStatus Status) : DomainEvent;
