using Licensing.Application.Abstractions;
using Licensing.Domain.Catalog;
using Licensing.Domain.Customers;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Licensing;

public sealed record ActivateRequest(string ProductKey, string DeviceId, string? DeviceName, string? ProductCode, string? AppVersion, string? Os = null);
public sealed record ValidateRequest(string ProductKey, string DeviceId, string? ProductCode, string? AppVersion, string? Os = null);
public sealed record DeactivateRequest(string ProductKey, string DeviceId);
/// <summary>INT-002: the seat a device credential belongs to. The credential itself travels in a header, never in the body.</summary>
public sealed record DeviceCheckRequest(Guid LicenseId, string DeviceId, string? AppVersion, string? Os = null);

/// <summary>
/// INT-002: a product-key activation. <see cref="DeviceCredential"/> is returned only here, once; the platform keeps
/// only its keyed hash. Activating the same device again issues a new credential and invalidates the old one.
/// </summary>
public sealed record DeviceActivationResponse(LicenseCheckResponse License, string DeviceCredential);

public sealed record TrialRequest(string DeviceId, string? DeviceName, string? ProductCode, string? AppVersion, string? Os = null);

/// <summary>A device's free plan: the product key (returned once) and the activation result. No expiry for a lifetime free plan.</summary>
public sealed record FreePlanResponse(string ProductKey, LicenseCheckResponse License, string PlanCode, int? DurationDays);

/// <summary>A device's trial: the product key (returned once, like an issued key) and the activation result.</summary>
public sealed record TrialResponse(string ProductKey, LicenseCheckResponse License, int TrialDays);

/// <summary>Response of activate, validate and heartbeat. <c>Token</c> lets the client keep working offline until <c>OfflineValidUntil</c>.</summary>
/// <remarks>
/// <see cref="LicenseId"/>, <see cref="CustomerId"/> and <see cref="SubscriptionId"/> identify the seat for integrations
/// (Monitor Cloud maps the device to a tenant by <see cref="CustomerId"/>, INT-003). They are additive: older clients
/// ignore them. They are never <see cref="Guid.Empty"/>.
/// </remarks>
public sealed record LicenseCheckResponse(
    string Status, string LicenseNumber, string ProductCode, string PlanCode, IReadOnlyList<string> Features,
    DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations, DateTimeOffset CheckAfter,
    DateTimeOffset OfflineValidUntil, string Token, string Kid, bool AlreadyActivated,
    Guid LicenseId, Guid CustomerId, Guid SubscriptionId);

/// <summary>Cached projection used by validate. Contains no secrets.</summary>
public sealed record LicenseSnapshot(
    Guid LicenseId, Guid TenantId, Guid CustomerId, Guid SubscriptionId, string LicenseNumber, string ProductCode, string PlanCode,
    LicenseStatus Status, DateTimeOffset? ExpiresAt, IReadOnlyList<string> Features, int? MaxActivations,
    int ActiveActivations, int HeartbeatIntervalHours, int OfflineGraceDays, SubscriptionStatus SubscriptionStatus,
    bool TenantActive, bool CustomerActive, IReadOnlyList<string> ActiveDevices);

/// <summary>
/// The endpoints called by licensed software (through an API client token). Implements the activation rules of plan section 19:
///  1 device id format · 2 key exists in the caller's tenant · 3 tenant active · 4 customer active · 5 product matches ·
///  6 not revoked · 7 not suspended · 8 not expired · 9 subscription usable · 10 activation limit (atomic) / device activated.
/// </summary>
public sealed class DeviceLicensingService(
    IAppDbContext db,
    ICurrentUser caller,
    IProductKeyGenerator keys,
    ILicenseTokenSigner signer,
    ILicenseCache cache,
    IAuditLogger audit,
    ISecretHasher secrets,
    ITenantScope scope,
    TimeProvider clock)
{
    /// <summary>Prefix of device credentials, so secret scanners can recognise them.</summary>
    public const string CredentialPrefix = "lcd_";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>The customer that owns trial licenses started by devices themselves, one per tenant.</summary>
    public const string TrialCustomerName = "Trial devices";
    public const string FreeCustomerName = "Free plan devices";

    /// <summary>
    /// Starts the one trial a device gets for a product: a subscription on the product's current trial plan (or, when no
    /// trial plan was chosen, the first published plan with trial days, by code), a
    /// license for one device that ends with the trial, and this device's activation. A device that already had a trial
    /// gets <see cref="LicenseErrors.TrialUsed"/>; the product key is returned only here, once.
    /// </summary>
    public async Task<Result<TrialResponse>> StartTrialAsync(TrialRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = caller.TenantId!.Value;
        var deviceId = request.DeviceId?.Trim() ?? "";
        if (!LicenseActivation.IsValidDeviceId(deviceId)) return LicenseErrors.InvalidDevice;

        var productCode = request.ProductCode?.Trim() ?? "";
        var product = productCode.Length == 0 ? null
            : await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == productCode && p.IsActive, ct);
        var plan = product is null ? null : await CurrentOfferAsync(tenantId, product.Id, PlanKind.Trial, ct) ?? await db.Plans
            .Where(p => p.TenantId == tenantId && p.ProductId == product.Id && p.Status == PlanStatus.Published && p.TrialDays > 0
                && p.Kind != PlanKind.Free)
            .OrderBy(p => p.Code).FirstOrDefaultAsync(ct);
        if (product is null || plan is null || plan.TrialDays is null) return LicenseErrors.TrialUnavailable;

        // The marker is kept on the subscription, so a device cannot start a second trial by reinstalling.
        var marker = TrialMarker(deviceId);
        if (await db.Subscriptions.AnyAsync(s => s.TenantId == tenantId && s.ProductId == product.Id && s.Notes == marker, ct))
        {
            audit.Add("license.trial_refused", "Product", product.Id.ToString(), false, $"device={deviceId}", tenantId);
            await db.SaveChangesAsync(ct);
            return LicenseErrors.TrialUsed;
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Name == TrialCustomerName, ct);
        if (customer is null)
        {
            customer = Customer.Create(tenantId, TrialCustomerName, null, null, null, null, now);
            db.Customers.Add(customer);
        }
        else if (customer.Status != CustomerStatus.Active)
        {
            return LicenseErrors.TrialUnavailable;
        }

        var sub = Subscription.Start(tenantId, customer.Id, product.Id, plan.Id, now, plan.DurationDays, plan.TrialDays, now, marker);
        db.Subscriptions.Add(sub);
        var (plainKey, hash, prefix) = keys.Generate();
        var license = License.Issue(tenantId, customer.Id, sub.Id, product.Id, plan.Id, LicenseService.NewLicenseNumber(now),
            hash, prefix, plan.ToEntitlements(product.Code) with { MaxActivations = 1 }, sub.EndDate, now);
        db.Licenses.Add(license);
        audit.Add("license.trial_started", "License", license.Id.ToString(),
            details: $"number={license.LicenseNumber} prefix={prefix} device={deviceId} ends={sub.EndDate:O}", tenantId: tenantId);
        await db.SaveChangesAsync(ct);

        if (!await db.TryReserveActivationSlotAsync(license.Id, ct))
            return LicenseErrors.ActivationLimitReached;
        db.LicenseActivations.Add(LicenseActivation.Create(license, deviceId, request.DeviceName, request.AppVersion, caller.IpAddress, now, request.Os));
        db.ActivationAttempts.Add(ActivationAttempt.Record(tenantId, license, prefix, deviceId, null, caller.IpAddress, now));
        await db.SaveChangesAsync(ct);

        var snapshot = await LoadSnapshotAsync(license, ct);
        var response = await RespondAsync(snapshot!, deviceId, now, alreadyActivated: false, ct);
        return new TrialResponse(plainKey, response.Value, plan.TrialDays!.Value);
    }

    private static string TrialMarker(string deviceId) => $"device-trial:{deviceId}";
    private static string FreeMarker(string deviceId) => $"device-free:{deviceId}";

    /// <summary>The published trial or free plan the admin chose as the product's current offer, if any.</summary>
    private Task<Plan?> CurrentOfferAsync(Guid tenantId, Guid productId, PlanKind kind, CancellationToken ct) =>
        db.Plans.Where(p => p.TenantId == tenantId && p.ProductId == productId && p.Kind == kind && p.IsCurrent
                && p.Status == PlanStatus.Published)
            .OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Gives a device the product's current free plan: one license per device and product, for one device. Asking again from
    /// the same device returns <see cref="LicenseErrors.FreeUsed"/> (the device already has its free license key).
    /// </summary>
    public async Task<Result<FreePlanResponse>> StartFreeAsync(TrialRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = caller.TenantId!.Value;
        var deviceId = request.DeviceId?.Trim() ?? "";
        if (!LicenseActivation.IsValidDeviceId(deviceId)) return LicenseErrors.InvalidDevice;

        var productCode = request.ProductCode?.Trim() ?? "";
        var product = productCode.Length == 0 ? null
            : await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == productCode && p.IsActive, ct);
        var plan = product is null ? null : await CurrentOfferAsync(tenantId, product.Id, PlanKind.Free, ct);
        if (product is null || plan is null) return LicenseErrors.FreeUnavailable;

        var marker = FreeMarker(deviceId);
        if (await db.Subscriptions.AnyAsync(s => s.TenantId == tenantId && s.ProductId == product.Id && s.Notes == marker, ct))
            return LicenseErrors.FreeUsed;

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Name == FreeCustomerName, ct);
        if (customer is null)
        {
            customer = Customer.Create(tenantId, FreeCustomerName, null, null, null, null, now);
            db.Customers.Add(customer);
        }
        else if (customer.Status != CustomerStatus.Active)
        {
            return LicenseErrors.FreeUnavailable;
        }

        var sub = Subscription.Start(tenantId, customer.Id, product.Id, plan.Id, now, plan.DurationDays, null, now, marker);
        db.Subscriptions.Add(sub);
        var (plainKey, hash, prefix) = keys.Generate();
        var license = License.Issue(tenantId, customer.Id, sub.Id, product.Id, plan.Id, LicenseService.NewLicenseNumber(now),
            hash, prefix, plan.ToEntitlements(product.Code) with { MaxActivations = 1 }, sub.EndDate, now);
        db.Licenses.Add(license);
        audit.Add("license.free_started", "License", license.Id.ToString(),
            details: $"number={license.LicenseNumber} prefix={prefix} device={deviceId} plan={plan.Code}", tenantId: tenantId);
        await db.SaveChangesAsync(ct);

        if (!await db.TryReserveActivationSlotAsync(license.Id, ct))
            return LicenseErrors.ActivationLimitReached;
        db.LicenseActivations.Add(LicenseActivation.Create(license, deviceId, request.DeviceName, request.AppVersion, caller.IpAddress, now, request.Os));
        db.ActivationAttempts.Add(ActivationAttempt.Record(tenantId, license, prefix, deviceId, null, caller.IpAddress, now));
        await db.SaveChangesAsync(ct);

        var snapshot = await LoadSnapshotAsync(license, ct);
        var response = await RespondAsync(snapshot!, deviceId, now, alreadyActivated: false, ct);
        return new FreePlanResponse(plainKey, response.Value, plan.Code, plan.DurationDays);
    }

    public Task<Result<LicenseCheckResponse>> ActivateAsync(ActivateRequest request, CancellationToken ct) =>
        ActivateCoreAsync(caller.TenantId!.Value, request, ct);

    /// <summary>
    /// INT-002 (ADR-001): activation by product key alone, for an Agent without an API client. The key proves ownership;
    /// the tenant is the license's. Same rules as <see cref="ActivateAsync"/>, then a device credential is issued.
    /// </summary>
    public async Task<Result<DeviceActivationResponse>> ActivateWithKeyAsync(ActivateRequest request, CancellationToken ct)
    {
        var hash = HashOrNull(request.ProductKey);
        scope.RunAsSystem(null);
        var owner = hash is null ? null : await db.Licenses.AsNoTracking().Where(l => l.ProductKeyHash == hash).Select(l => (Guid?)l.TenantId).FirstOrDefaultAsync(ct);
        // An unknown key gets the same answer as any other invalid license, and no tenant to record the attempt in.
        if (owner is not { } tenantId) return LicenseErrors.InvalidLicense;
        scope.RunAsSystem(tenantId);

        var activated = await ActivateCoreAsync(tenantId, request, ct);
        if (activated.IsFailure) return activated.Error!;

        var deviceId = request.DeviceId.Trim();
        var licenseId = activated.Value.LicenseId;
        var activation = await db.LicenseActivations.FirstAsync(a => a.LicenseId == licenseId && a.DeviceId == deviceId && a.Status == ActivationStatus.Active, ct);
        var credential = CredentialPrefix + secrets.GenerateToken(32);
        activation.IssueCredential(secrets.Hash(credential));
        audit.Add("license.device_credential_issued", "License", licenseId.ToString(), details: $"device={deviceId}", tenantId: tenantId);
        await db.SaveChangesAsync(ct);
        return new DeviceActivationResponse(activated.Value, credential);
    }

    /// <summary>INT-002: validate (cached rules) or heartbeat (fresh token) with a device credential.</summary>
    public async Task<Result<LicenseCheckResponse>> CheckWithCredentialAsync(DeviceCheckRequest request, string? credential, bool heartbeat, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var found = await FindCredentialSeatAsync(request.LicenseId, request.DeviceId, credential, ct);
        if (found is not { } seat) return LicenseErrors.DeviceNotActivated;

        var snapshot = await LoadSnapshotAsync(seat.License, ct);
        if (snapshot is null) return LicenseErrors.InvalidLicense;
        if (CheckRules(snapshot, null, now) is { } error) return error;
        if (heartbeat)
        {
            seat.Activation.Heartbeat(request.AppVersion, caller.IpAddress, now, request.Os);
            await db.SaveChangesAsync(ct);
        }

        return await RespondAsync(snapshot, seat.Activation.DeviceId, now, false, ct);
    }

    /// <summary>INT-002: the device gives its own seat back.</summary>
    public async Task<Result> DeactivateWithCredentialAsync(DeviceCheckRequest request, string? credential, CancellationToken ct)
    {
        var found = await FindCredentialSeatAsync(request.LicenseId, request.DeviceId, credential, ct);
        if (found is not { } seat) return LicenseErrors.DeviceNotActivated;

        seat.Activation.Deactivate(clock.GetUtcNow());
        audit.Add("license.deactivated", "License", seat.License.Id.ToString(), details: $"device={seat.Activation.DeviceId}", tenantId: seat.License.TenantId);
        await db.SaveChangesAsync(ct);
        await db.ReleaseActivationSlotAsync(seat.License.Id, ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(seat.License.TenantId, seat.License.ProductKeyHash), ct);
        return Result.Success();
    }

    /// <summary>
    /// The active seat whose credential matches. Unknown license, unknown device, inactive seat and wrong credential all
    /// return null, so a caller cannot tell them apart. Hashes are compared in fixed time.
    /// </summary>
    private async Task<(License License, LicenseActivation Activation)?> FindCredentialSeatAsync(Guid licenseId, string? deviceId, string? credential, CancellationToken ct)
    {
        var device = deviceId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 200 || !credential.StartsWith(CredentialPrefix, StringComparison.Ordinal) ||
            !LicenseActivation.IsValidDeviceId(device))
            return null;

        scope.RunAsSystem(null);
        var activation = await db.LicenseActivations.FirstOrDefaultAsync(a => a.LicenseId == licenseId && a.DeviceId == device && a.Status == ActivationStatus.Active, ct);
        var expected = activation?.CredentialHash;
        var actual = secrets.Hash(credential);
        if (activation is null || expected is null ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(actual)))
            return null;

        scope.RunAsSystem(activation.TenantId);
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        return license is null ? null : (license, activation);
    }

    private async Task<Result<LicenseCheckResponse>> ActivateCoreAsync(Guid tenantId, ActivateRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var deviceId = request.DeviceId?.Trim() ?? "";
        var hash = HashOrNull(request.ProductKey);
        var prefix = PrefixOf(request.ProductKey);

        if (!LicenseActivation.IsValidDeviceId(deviceId))
            return await FailAttemptAsync(tenantId, null, prefix, deviceId, LicenseErrors.InvalidDevice, now, ct);

        var license = hash is null ? null : await db.Licenses.FirstOrDefaultAsync(l => l.ProductKeyHash == hash, ct);
        var snapshot = license is null ? null : await LoadSnapshotAsync(license, ct);
        var ruleError = snapshot is null ? LicenseErrors.InvalidLicense : CheckRules(snapshot, request.ProductCode, now);
        if (ruleError is not null)
            return await FailAttemptAsync(tenantId, license, prefix, deviceId, ruleError, now, ct);

        var existing = await db.LicenseActivations.FirstOrDefaultAsync(a => a.LicenseId == license!.Id && a.DeviceId == deviceId, ct);
        if (existing is { Status: ActivationStatus.Active })
        {
            // Same device activating again is an idempotent success, not a new activation.
            existing.Heartbeat(request.AppVersion, caller.IpAddress, now, request.Os);
            db.ActivationAttempts.Add(ActivationAttempt.Record(tenantId, license, prefix, deviceId, null, caller.IpAddress, now));
            await db.SaveChangesAsync(ct);
            return await RespondAsync(snapshot!, deviceId, now, alreadyActivated: true, ct);
        }

        if (!await db.TryReserveActivationSlotAsync(license!.Id, ct))
            return await FailAttemptAsync(tenantId, license, prefix, deviceId, LicenseErrors.ActivationLimitReached, now, ct);

        try
        {
            if (existing is not null)
                existing.Reactivate(request.DeviceName, request.AppVersion, caller.IpAddress, now, request.Os);
            else
                db.LicenseActivations.Add(LicenseActivation.Create(license, deviceId, request.DeviceName, request.AppVersion, caller.IpAddress, now, request.Os));
            db.ActivationAttempts.Add(ActivationAttempt.Record(tenantId, license, prefix, deviceId, null, caller.IpAddress, now));
            audit.Add("license.activated", "License", license.Id.ToString(), details: $"device={deviceId}", tenantId: tenantId);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent activation of the same device (unique LicenseId+DeviceId): give the slot back.
            await db.ReleaseActivationSlotAsync(license.Id, ct);
            var winner = await db.LicenseActivations.AsNoTracking()
                .FirstOrDefaultAsync(a => a.LicenseId == license.Id && a.DeviceId == deviceId && a.Status == ActivationStatus.Active, ct);
            if (winner is null) throw;
            return await RespondAsync(snapshot!, deviceId, now, alreadyActivated: true, ct);
        }

        await cache.RemoveAsync(LicenseCacheKeys.For(tenantId, license.ProductKeyHash), ct);
        var fresh = await LoadSnapshotAsync(license, ct);
        return await RespondAsync(fresh!, deviceId, now, alreadyActivated: false, ct);
    }

    public Task<Result<LicenseCheckResponse>> ValidateAsync(ValidateRequest request, CancellationToken ct) =>
        CheckAsync(request, heartbeat: false, ct);

    public Task<Result<LicenseCheckResponse>> HeartbeatAsync(ValidateRequest request, CancellationToken ct) =>
        CheckAsync(request, heartbeat: true, ct);

    private async Task<Result<LicenseCheckResponse>> CheckAsync(ValidateRequest request, bool heartbeat, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = caller.TenantId!.Value;
        var deviceId = request.DeviceId?.Trim() ?? "";
        if (!LicenseActivation.IsValidDeviceId(deviceId)) return LicenseErrors.InvalidDevice;
        var hash = HashOrNull(request.ProductKey);
        if (hash is null) return LicenseErrors.InvalidLicense;

        var cacheKey = LicenseCacheKeys.For(tenantId, hash);
        var snapshot = heartbeat ? null : await cache.GetAsync<LicenseSnapshot>(cacheKey, ct);
        if (snapshot is null)
        {
            var license = await db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.ProductKeyHash == hash, ct);
            if (license is null) return LicenseErrors.InvalidLicense;
            snapshot = await LoadSnapshotAsync(license, ct);
            if (snapshot is null) return LicenseErrors.InvalidLicense;
            await cache.SetAsync(cacheKey, snapshot, CacheTtl, ct);
        }

        if (CheckRules(snapshot, request.ProductCode, now) is { } error) return error;
        if (!snapshot.ActiveDevices.Contains(deviceId)) return LicenseErrors.DeviceNotActivated;

        if (heartbeat)
        {
            var activation = await db.LicenseActivations.FirstOrDefaultAsync(
                a => a.LicenseId == snapshot.LicenseId && a.DeviceId == deviceId && a.Status == ActivationStatus.Active, ct);
            if (activation is null) return LicenseErrors.DeviceNotActivated;
            activation.Heartbeat(request.AppVersion, caller.IpAddress, now, request.Os);
            await db.SaveChangesAsync(ct);
        }

        return await RespondAsync(snapshot, deviceId, now, false, ct);
    }

    /// <summary>
    /// INT-004: Monitor Cloud's heartbeat for a seat it holds. Cloud keeps the license id, never the product key, so the
    /// seat is found by id inside the caller's tenant. Same rules and answer as <see cref="HeartbeatAsync"/>.
    /// </summary>
    public async Task<Result<LicenseCheckResponse>> HeartbeatSeatAsync(Guid licenseId, string? deviceId, string? appVersion, string? os, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var device = deviceId?.Trim() ?? "";
        if (!LicenseActivation.IsValidDeviceId(device)) return LicenseErrors.InvalidDevice;

        var license = await db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        var snapshot = license is null ? null : await LoadSnapshotAsync(license, ct);
        if (snapshot is null) return LicenseErrors.InvalidLicense;
        if (CheckRules(snapshot, null, now) is { } error) return error;

        var activation = await db.LicenseActivations.FirstOrDefaultAsync(
            a => a.LicenseId == licenseId && a.DeviceId == device && a.Status == ActivationStatus.Active, ct);
        if (activation is null) return LicenseErrors.DeviceNotActivated;
        activation.Heartbeat(appVersion, caller.IpAddress, now, os);
        await db.SaveChangesAsync(ct);
        return await RespondAsync(snapshot, device, now, false, ct);
    }

    /// <summary>
    /// INT-005: Monitor Cloud gives a seat back (device retired or unlicensed). Frees the activation slot like
    /// <see cref="DeactivateAsync"/>; <c>LIC_DEVICE_NOT_ACTIVATED</c> when the device holds no active seat.
    /// </summary>
    public async Task<Result> ReleaseSeatAsync(Guid licenseId, string? deviceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var device = deviceId?.Trim() ?? "";
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null) return LicenseErrors.InvalidLicense;

        var activation = await db.LicenseActivations.FirstOrDefaultAsync(
            a => a.LicenseId == license.Id && a.DeviceId == device && a.Status == ActivationStatus.Active, ct);
        if (activation is null) return LicenseErrors.DeviceNotActivated;

        activation.Deactivate(now);
        audit.Add("license.released", "License", license.Id.ToString(), details: $"device={device}", tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);
        await db.ReleaseActivationSlotAsync(license.Id, ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, license.ProductKeyHash), ct);
        return Result.Success();
    }

    public async Task<Result> DeactivateAsync(DeactivateRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = HashOrNull(request.ProductKey);
        var deviceId = request.DeviceId?.Trim() ?? "";
        var license = hash is null ? null : await db.Licenses.FirstOrDefaultAsync(l => l.ProductKeyHash == hash, ct);
        if (license is null) return LicenseErrors.InvalidLicense;

        var activation = await db.LicenseActivations.FirstOrDefaultAsync(
            a => a.LicenseId == license.Id && a.DeviceId == deviceId && a.Status == ActivationStatus.Active, ct);
        if (activation is null) return LicenseErrors.DeviceNotActivated;

        activation.Deactivate(now);
        audit.Add("license.deactivated", "License", license.Id.ToString(), details: $"device={deviceId}", tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);
        await db.ReleaseActivationSlotAsync(license.Id, ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, license.ProductKeyHash), ct);
        return Result.Success();
    }

    // ---------- helpers ----------

    private static Error? CheckRules(LicenseSnapshot s, string? productCode, DateTimeOffset now)
    {
        if (!s.TenantActive || !s.CustomerActive) return LicenseErrors.InvalidLicense;
        if (!string.IsNullOrWhiteSpace(productCode) && !string.Equals(productCode.Trim(), s.ProductCode, StringComparison.OrdinalIgnoreCase))
            return LicenseErrors.InvalidLicense;
        // Order matters: an explicit action on the license wins, then the subscription, then the license's own expiry.
        if (s.Status == LicenseStatus.Revoked) return LicenseErrors.Revoked;
        if (s.Status == LicenseStatus.Suspended) return LicenseErrors.Suspended;
        return s.SubscriptionStatus switch
        {
            SubscriptionStatus.Expired => LicenseErrors.SubscriptionExpired,
            SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled => LicenseErrors.SubscriptionInactive,
            _ when s.Status == LicenseStatus.Expired || (s.ExpiresAt is not null && s.ExpiresAt <= now) => LicenseErrors.Expired,
            _ => null,
        };
    }

    private async Task<LicenseSnapshot?> LoadSnapshotAsync(License l, CancellationToken ct)
    {
        var info = await (
            from s in db.Subscriptions.Where(s => s.Id == l.SubscriptionId)
            join t in db.Tenants on s.TenantId equals t.Id
            join c in db.Customers on s.CustomerId equals c.Id
            select new { s.Status, TenantActive = t.Status == TenantStatus.Active, CustomerActive = c.Status == CustomerStatus.Active })
            .FirstOrDefaultAsync(ct);
        if (info is null) return null;

        var devices = await db.LicenseActivations
            .Where(a => a.LicenseId == l.Id && a.Status == ActivationStatus.Active).Select(a => a.DeviceId).ToListAsync(ct);
        var active = await db.Licenses.Where(x => x.Id == l.Id).Select(x => x.ActiveActivations).FirstAsync(ct);

        return new LicenseSnapshot(l.Id, l.TenantId, l.CustomerId, l.SubscriptionId, l.LicenseNumber, l.ProductCode, l.PlanCode, l.Status, l.ExpiresAt,
            l.Features, l.MaxActivations, active, l.HeartbeatIntervalHours, l.OfflineGraceDays, info.Status,
            info.TenantActive, info.CustomerActive, devices);
    }

    private async Task<Result<LicenseCheckResponse>> RespondAsync(LicenseSnapshot s, string deviceId, DateTimeOffset now, bool alreadyActivated, CancellationToken ct)
    {
        // Small jitter spreads heartbeats of many devices over time instead of all hitting at the same minute.
        var jitter = TimeSpan.FromMinutes(Random.Shared.Next(0, Math.Max(1, s.HeartbeatIntervalHours * 6)));
        var checkAfter = now.AddHours(s.HeartbeatIntervalHours).Add(jitter);
        var offlineUntil = now.AddHours(s.HeartbeatIntervalHours).AddDays(s.OfflineGraceDays);
        if (s.ExpiresAt is { } exp && exp < offlineUntil) offlineUntil = exp;

        var token = await signer.SignAsync(new LicenseTokenClaims(
            s.LicenseNumber, s.LicenseId, s.TenantId, s.ProductCode, s.PlanCode, deviceId, s.Features,
            s.ExpiresAt, checkAfter, offlineUntil), ct);

        return new LicenseCheckResponse("active", s.LicenseNumber, s.ProductCode, s.PlanCode, s.Features, s.ExpiresAt,
            s.MaxActivations, s.ActiveActivations, checkAfter, offlineUntil, token.Token, token.Kid, alreadyActivated,
            s.LicenseId, s.CustomerId, s.SubscriptionId);
    }

    private async Task<Result<LicenseCheckResponse>> FailAttemptAsync(
        Guid tenantId, License? license, string? prefix, string deviceId, Error error, DateTimeOffset now, CancellationToken ct)
    {
        db.ActivationAttempts.Add(ActivationAttempt.Record(tenantId, license, prefix, deviceId, error.Code, caller.IpAddress, now));
        audit.Add("license.activation_failed", "License", license?.Id.ToString(), false, $"code={error.Code} device={deviceId}", tenantId);
        await db.SaveChangesAsync(ct);
        return error;
    }

    private string? HashOrNull(string? productKey)
    {
        if (string.IsNullOrWhiteSpace(productKey)) return null;
        var normalized = keys.Normalize(productKey);
        return normalized.Length == 0 ? null : keys.Hash(normalized);
    }

    private string? PrefixOf(string? productKey)
    {
        if (string.IsNullOrWhiteSpace(productKey)) return null;
        var n = keys.Normalize(productKey);
        return n.Length >= 6 ? n[..6] : null;
    }
}
