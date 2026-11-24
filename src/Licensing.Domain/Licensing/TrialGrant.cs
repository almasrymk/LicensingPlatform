using Licensing.SharedKernel;

namespace Licensing.Domain.Licensing;

/// <summary>
/// B5 (trial for a new install, product owner 2026-11-08): a short-lived, single-use permission to start one device
/// trial of a product, issued to a signed-in website customer by the website's API client. The code (prefix
/// <see cref="CodePrefix"/>) is shown once and stored as a keyed hash. The Agent redeems it anonymously on
/// <c>POST api/v1/licensing/device/trial</c>, which needs no shared secret in the installer. Redemption still applies
/// the one-trial-per-device rule; the grant only replaces the API client the device used to need.
/// </summary>
public sealed class TrialGrant : Entity, ITenantOwned
{
    public const string CodePrefix = "ltg_";
    public const int ReferenceMaxLength = 100;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    private TrialGrant() { CodeHash = Reference = null!; }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>The website's own id of the person it was issued to (for example <c>website-user:{id}</c>), one trial each.</summary>
    public string Reference { get; private set; }
    public string CodeHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public string? RedeemedDeviceId { get; private set; }
    public Guid? LicenseId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public static TrialGrant Issue(Guid tenantId, Guid productId, string reference, string codeHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromDays(7))
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "A trial grant lives between a minute and seven days."));
        var trimmed = Guard.NotEmpty(reference, "Reference", ReferenceMaxLength);
        return new TrialGrant
        {
            TenantId = tenantId,
            ProductId = productId,
            Reference = trimmed,
            CodeHash = Guard.NotEmpty(codeHash, "CodeHash"),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public bool IsOpen(DateTimeOffset now) => RedeemedAt is null && CancelledAt is null && now < ExpiresAt;

    /// <summary>A newer grant for the same reference replaces this unused one (the code was lost or expired).</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (RedeemedAt is null) CancelledAt ??= now;
    }

    public void Redeem(string deviceId, Guid licenseId, DateTimeOffset now)
    {
        if (!IsOpen(now)) throw new DomainException(TrialGrantErrors.Invalid);
        RedeemedAt = now;
        RedeemedDeviceId = deviceId;
        LicenseId = licenseId;
    }
}

public static class TrialGrantErrors
{
    /// <summary>Unknown, expired, cancelled, already used or for another product: one answer for all.</summary>
    public static readonly Error Invalid = Error.NotFound("LIC_TRIAL_GRANT_INVALID", "This trial code is not valid. Request a new one on the website.");

    /// <summary>The person behind the reference already started a trial of this product.</summary>
    public static readonly Error AlreadyUsed = Error.Conflict("LIC_TRIAL_GRANT_USED", "A trial of this product was already started for this account.");
}
