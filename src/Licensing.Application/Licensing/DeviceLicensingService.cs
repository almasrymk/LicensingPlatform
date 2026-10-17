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
public sealed record TrialRequest(string DeviceId, string? DeviceName, string? ProductCode, string? AppVersion, string? Os = null);

/// <summary>A device's free plan: the product key (returned once) and the activation result. No expiry for a lifetime free plan.</summary>
public sealed record FreePlanResponse(string ProductKey, LicenseCheckResponse License, string PlanCode, int? DurationDays);

/// <summary>A device's trial: the product key (returned once, like an issued key) and the activation result.</summary>
public sealed record TrialResponse(string ProductKey, LicenseCheckResponse License, int TrialDays);

/// <summary>Response of activate, validate and heartbeat. <c>Token</c> lets the client keep working offline until <c>OfflineValidUntil</c>.</summary>
public sealed record LicenseCheckResponse(
    string Status, string LicenseNumber, string ProductCode, string PlanCode, IReadOnlyList<string> Features,
    DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations, DateTimeOffset CheckAfter,
    DateTimeOffset OfflineValidUntil, string Token, string Kid, bool AlreadyActivated = false);

/// <summary>Cached projection used by validate. Contains no secrets.</summary>
public sealed record LicenseSnapshot(
    Guid LicenseId, Guid TenantId, Guid CustomerId, string LicenseNumber, string ProductCode, string PlanCode,
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
    TimeProvider clock)
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>The customer that owns trial licenses started by devices themselves, one per tenant.</summary>
    public const string TrialCustomerName = "Trial devices";
    public const string FreeCustomerName = "Free plan devices";

    /// <summary>
    /// Starts the one trial a device gets for a product. The trial length is the product's <see cref="Product.TrialDays"/>;
    /// the trial unlocks the product's fullest published paid plan (most features, then highest price). It creates a
    /// subscription, a license for one device that ends with the trial, and this device's activation. A device that already
    /// had a trial gets <see cref="LicenseErrors.TrialUsed"/>; the product key is returned only here, once.
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
        if (product?.TrialDays is not { } trialDays) return LicenseErrors.TrialUnavailable;
        // Feature lists are not translatable to SQL, so the (few) published paid plans are ranked in memory.
        var plan = (await db.Plans
                .Where(p => p.TenantId == tenantId && p.ProductId == product.Id && p.Status == PlanStatus.Published && p.Kind == PlanKind.Paid)
                .ToListAsync(ct))
            .OrderByDescending(p => p.Features.Count).ThenByDescending(p => p.Price.Amount).ThenBy(p => p.Code).ThenByDescending(p => p.Version)
            .FirstOrDefault();
        if (plan is null) return LicenseErrors.TrialUnavailable;

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

        var sub = Subscription.Start(tenantId, customer.Id, product.Id, plan.Id, now, plan.DurationDays, trialDays, now, marker);
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
        return new TrialResponse(plainKey, response.Value, trialDays);
    }

    private static string TrialMarker(string deviceId) => $"device-trial:{deviceId}";
    private static string FreeMarker(string deviceId) => $"device-free:{deviceId}";

    /// <summary>The published free plan the admin chose as the product's current offer, if any.</summary>
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

    public async Task<Result<LicenseCheckResponse>> ActivateAsync(ActivateRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var tenantId = caller.TenantId!.Value;
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

        return new LicenseSnapshot(l.Id, l.TenantId, l.CustomerId, l.LicenseNumber, l.ProductCode, l.PlanCode, l.Status, l.ExpiresAt,
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
            s.MaxActivations, s.ActiveActivations, checkAfter, offlineUntil, token.Token, token.Kid, alreadyActivated);
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
