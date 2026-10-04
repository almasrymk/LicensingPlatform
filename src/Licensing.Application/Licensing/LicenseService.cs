using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Licensing;

public sealed record LicenseDto(
    Guid Id, Guid TenantId, Guid CustomerId, string CustomerName, Guid SubscriptionId, string ProductCode, string PlanCode, int PlanVersion,
    string LicenseNumber, string ProductKeyPrefix, LicenseStatus Status, string? StatusReason, DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations, IReadOnlyList<string> Features,
    int HeartbeatIntervalHours, int OfflineGraceDays);

public sealed record ActivationDto(
    Guid Id, Guid LicenseId, string DeviceId, string? DeviceName, string? AppVersion, ActivationStatus Status,
    DateTimeOffset ActivatedAt, DateTimeOffset? DeactivatedAt, DateTimeOffset? LastHeartbeatAt, string? LastIpAddress,
    string? OperatingSystem, bool Online);

/// <summary>One device across all licenses (Activations / Devices screen).</summary>
public sealed record ActivationRowDto(
    Guid Id, Guid LicenseId, string LicenseNumber, Guid CustomerId, string CustomerName, string ProductCode, string DeviceId,
    string? DeviceName, string? OperatingSystem, string? AppVersion, string? LastIpAddress, ActivationStatus Status,
    DateTimeOffset ActivatedAt, DateTimeOffset? LastHeartbeatAt, bool Online);

public sealed record LicenseDetailsDto(LicenseDto License, IReadOnlyList<ActivationDto> Activations);

public sealed record IssueLicenseRequest(Guid SubscriptionId);

public sealed record LicenseFilter(Guid? CustomerId = null, Guid? SubscriptionId = null, LicenseStatus? Status = null, Guid? ProductId = null,
    Guid? PlanId = null, Guid? TenantId = null, bool? NearExpiry = null, bool? LimitReached = null);

/// <summary>State is "online", "offline" or "disabled".</summary>
public sealed record ActivationFilter(string? State = null, Guid? LicenseId = null, Guid? ProductId = null, string? Os = null, Guid? TenantId = null);
/// <summary>The only response that ever carries the full product key.</summary>
public sealed record IssuedLicenseDto(LicenseDto License, string ProductKey);
public sealed record LicenseActionRequest(string? Reason);

public sealed class LicenseService(
    IAppDbContext db, IProductKeyGenerator keys, ILicenseCache cache, IAuditLogger audit, ITenantContext scope, TimeProvider clock)
{
    private sealed class Row { public required License L { get; init; } public required string CustomerName { get; init; } }

    private IQueryable<Row> Rows(IQueryable<License> q) =>
        from l in q join c in db.Customers on l.CustomerId equals c.Id select new Row { L = l, CustomerName = c.Name };

    private static LicenseDto ToDto(Row r) => new(
        r.L.Id, r.L.TenantId, r.L.CustomerId, r.CustomerName, r.L.SubscriptionId, r.L.ProductCode, r.L.PlanCode, r.L.PlanVersion,
        r.L.LicenseNumber, r.L.ProductKeyPrefix, r.L.Status, r.L.StatusReason, r.L.IssuedAt, r.L.ExpiresAt,
        r.L.MaxActivations, r.L.ActiveActivations, r.L.Features, r.L.HeartbeatIntervalHours, r.L.OfflineGraceDays);

    public async Task<PagedResult<LicenseDto>> ListAsync(PageQuery page, LicenseFilter f, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var q = db.Licenses.AsQueryable();
        if (f.CustomerId is not null) q = q.Where(l => l.CustomerId == f.CustomerId);
        if (f.SubscriptionId is not null) q = q.Where(l => l.SubscriptionId == f.SubscriptionId);
        if (f.Status is not null) q = q.Where(l => l.Status == f.Status);
        if (f.ProductId is not null) q = q.Where(l => l.ProductId == f.ProductId);
        if (f.PlanId is not null) q = q.Where(l => l.PlanId == f.PlanId);
        if (f.TenantId is { } tid && scope.IsUnrestricted) q = q.Where(l => l.TenantId == tid);
        if (f.NearExpiry == true)
        {
            var soon = now.AddDays(30);
            q = q.Where(l => l.Status == LicenseStatus.Active && l.ExpiresAt != null && l.ExpiresAt > now && l.ExpiresAt <= soon);
        }
        if (f.LimitReached == true) q = q.Where(l => l.MaxActivations != null && l.ActiveActivations >= l.MaxActivations);
        var rows = Rows(q);
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var s = page.Search.Trim().ToUpperInvariant();
            rows = rows.Where(r => r.L.LicenseNumber.Contains(s) || r.L.ProductKeyPrefix.Contains(s) || r.CustomerName.Contains(page.Search));
        }
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(r => r.L.IssuedAt)
            .Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<LicenseDto>(items.Select(ToDto).ToList(), total, page.SafePage, page.SafePageSize);
    }

    public async Task<Result<LicenseDetailsDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Rows(db.Licenses.Where(l => l.Id == id)).FirstOrDefaultAsync(ct);
        if (row is null) return AppErrors.NotFound("License");
        var onlineSince = clock.GetUtcNow().AddHours(-2 * row.L.HeartbeatIntervalHours);
        var activations = await db.LicenseActivations.Where(a => a.LicenseId == id)
            .OrderByDescending(a => a.Status == ActivationStatus.Active).ThenByDescending(a => a.ActivatedAt)
            .Select(a => new ActivationDto(a.Id, a.LicenseId, a.DeviceId, a.DeviceName, a.AppVersion, a.Status,
                a.ActivatedAt, a.DeactivatedAt, a.LastHeartbeatAt, a.LastIpAddress, a.OperatingSystem,
                a.Status == ActivationStatus.Active && a.LastHeartbeatAt != null && a.LastHeartbeatAt >= onlineSince))
            .ToListAsync(ct);
        return new LicenseDetailsDto(ToDto(row), activations);
    }

    /// <summary>All devices the caller can see, newest first. "Online" = checked in within two heartbeat intervals.</summary>
    public async Task<PagedResult<ActivationRowDto>> ListActivationsAsync(PageQuery page, ActivationFilter f, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var q = from a in db.LicenseActivations
                join l in db.Licenses on a.LicenseId equals l.Id
                join c in db.Customers on a.CustomerId equals c.Id
                select new { a, l.LicenseNumber, l.ProductCode, l.ProductId, l.TenantId, l.HeartbeatIntervalHours, CustomerName = c.Name };
        if (f.LicenseId is not null) q = q.Where(x => x.a.LicenseId == f.LicenseId);
        if (f.ProductId is not null) q = q.Where(x => x.ProductId == f.ProductId);
        if (f.TenantId is { } tid && scope.IsUnrestricted) q = q.Where(x => x.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(f.Os)) q = q.Where(x => x.a.OperatingSystem != null && x.a.OperatingSystem.Contains(f.Os));
        // Online = active and checked in within two heartbeat intervals. The interval is per license, so the cut-off is
        // computed per distinct interval (few values) and the parts are combined, which every provider can translate.
        if (f.State == "disabled")
        {
            q = q.Where(x => x.a.Status == ActivationStatus.Deactivated);
        }
        else if (f.State is "online" or "offline")
        {
            var online = f.State == "online";
            var active = q.Where(x => x.a.Status == ActivationStatus.Active);
            var intervals = await active.Select(x => x.HeartbeatIntervalHours).Distinct().ToListAsync(ct);
            var combined = active.Where(_ => false);
            foreach (var hours in intervals)
            {
                var cutoff = now.AddHours(-2 * hours);
                var part = online
                    ? active.Where(x => x.HeartbeatIntervalHours == hours && x.a.LastHeartbeatAt != null && x.a.LastHeartbeatAt >= cutoff)
                    : active.Where(x => x.HeartbeatIntervalHours == hours && (x.a.LastHeartbeatAt == null || x.a.LastHeartbeatAt < cutoff));
                combined = combined.Concat(part);
            }
            q = combined;
        }
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var s = page.Search.Trim();
            q = q.Where(x => x.a.DeviceId.Contains(s) || (x.a.DeviceName != null && x.a.DeviceName.Contains(s)) ||
                             x.LicenseNumber.Contains(s) || x.CustomerName.Contains(s) || (x.a.LastIpAddress != null && x.a.LastIpAddress.Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.a.LastHeartbeatAt ?? x.a.ActivatedAt)
            .Skip((page.SafePage - 1) * page.SafePageSize).Take(page.SafePageSize).ToListAsync(ct);
        var items = rows.Select(x => new ActivationRowDto(x.a.Id, x.a.LicenseId, x.LicenseNumber, x.a.CustomerId, x.CustomerName, x.ProductCode,
            x.a.DeviceId, x.a.DeviceName, x.a.OperatingSystem, x.a.AppVersion, x.a.LastIpAddress, x.a.Status, x.a.ActivatedAt, x.a.LastHeartbeatAt,
            x.a.Status == ActivationStatus.Active && x.a.LastHeartbeatAt is { } hb && hb >= now.AddHours(-2 * x.HeartbeatIntervalHours))).ToList();
        return new PagedResult<ActivationRowDto>(items, total, page.SafePage, page.SafePageSize);
    }

    /// <summary>
    /// Replaces a leaked product key. The old key fails its next check, every device is released and must activate again
    /// with the new key, which is returned once.
    /// </summary>
    public async Task<Result<IssuedLicenseDto>> RegenerateKeyAsync(Guid id, CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (license is null) return AppErrors.NotFound("License");
        var oldHash = license.ProductKeyHash;
        var now = clock.GetUtcNow();
        var (plainKey, hash, prefix) = keys.Generate();
        license.RegenerateKey(hash, prefix);
        var activations = await db.LicenseActivations.Where(a => a.LicenseId == id && a.Status == ActivationStatus.Active).ToListAsync(ct);
        activations.ForEach(a => a.Deactivate(now));
        audit.Add("license.key_regenerated", "License", id.ToString(), details: $"prefix={prefix} devices_released={activations.Count}", tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, oldHash), ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, hash), ct);
        return new IssuedLicenseDto((await GetAsync(id, ct)).Value.License, plainKey);
    }

    /// <summary>Issues a license for a subscription with a snapshot of the plan's entitlements. The key is returned once.</summary>
    public async Task<Result<IssuedLicenseDto>> IssueAsync(IssueLicenseRequest request, CancellationToken ct)
    {
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == request.SubscriptionId, ct);
        if (sub is null) return AppErrors.NotFound("Subscription");
        if (!sub.IsUsable)
            return Error.Conflict("SUBSCRIPTION_NOT_ACTIVE", "Licenses can only be issued for active or trial subscriptions.");

        var x = await db.Plans.Where(p => p.Id == sub.PlanId)
            .Join(db.Products, pl => pl.ProductId, p => p.Id, (pl, p) => new { pl, p.Code }).FirstAsync(ct);

        var now = clock.GetUtcNow();
        var (plainKey, hash, prefix) = keys.Generate();
        var license = License.Issue(sub.TenantId, sub.CustomerId, sub.Id, sub.ProductId, sub.PlanId, NewLicenseNumber(now),
            hash, prefix, x.pl.ToEntitlements(x.Code), sub.EndDate, now);
        db.Licenses.Add(license);
        // Never log or audit the key itself: only its non-secret prefix.
        audit.Add("license.issued", "License", license.Id.ToString(), details: $"number={license.LicenseNumber} prefix={prefix}", tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);

        var dto = (await GetAsync(license.Id, ct)).Value.License;
        return new IssuedLicenseDto(dto, plainKey);
    }

    public Task<Result<LicenseDetailsDto>> SuspendAsync(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        MutateAsync(id, "license.suspended", l => l.Suspend(request.Reason), request.Reason, ct);

    public Task<Result<LicenseDetailsDto>> ResumeAsync(Guid id, CancellationToken ct) =>
        MutateAsync(id, "license.resumed", l => l.Resume(), null, ct);

    public Task<Result<LicenseDetailsDto>> RevokeAsync(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        MutateAsync(id, "license.revoked", l => l.Revoke(request.Reason, clock.GetUtcNow()), request.Reason, ct);

    private async Task<Result<LicenseDetailsDto>> MutateAsync(Guid id, string action, Action<License> change, string? reason, CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (license is null) return AppErrors.NotFound("License");
        change(license);
        audit.Add(action, "License", id.ToString(), details: reason, tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);
        // Invalidate synchronously as well as through the outbox event: a revoked license must fail its next validate.
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, license.ProductKeyHash), ct);
        return await GetAsync(id, ct);
    }

    /// <summary>"Reset device" from the portal: frees the activation slot so the customer can activate another machine.</summary>
    public async Task<Result<LicenseDetailsDto>> ResetDeviceAsync(Guid licenseId, Guid activationId, CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == licenseId, ct);
        if (license is null) return AppErrors.NotFound("License");
        var activation = await db.LicenseActivations.FirstOrDefaultAsync(a => a.Id == activationId && a.LicenseId == licenseId, ct);
        if (activation is null) return AppErrors.NotFound("Activation");
        if (activation.Status != ActivationStatus.Active)
            return Error.Conflict("ACTIVATION_NOT_ACTIVE", "This device is already deactivated.");

        activation.Deactivate(clock.GetUtcNow());
        audit.Add("license.device_reset", "License", licenseId.ToString(), details: $"device={activation.DeviceId}", tenantId: license.TenantId);
        await db.SaveChangesAsync(ct);
        await db.ReleaseActivationSlotAsync(licenseId, ct);
        await cache.RemoveAsync(LicenseCacheKeys.For(license.TenantId, license.ProductKeyHash), ct);
        return await GetAsync(licenseId, ct);
    }

    /// <summary>Hourly job: expires licenses whose own end date has passed. Idempotent.</summary>
    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var count = 0;
        while (!ct.IsCancellationRequested)
        {
            var due = await db.Licenses
                .Where(l => (l.Status == LicenseStatus.Active || l.Status == LicenseStatus.Suspended) && l.ExpiresAt != null && l.ExpiresAt <= now)
                .OrderBy(l => l.ExpiresAt).Take(batchSize).ToListAsync(ct);
            if (due.Count == 0) break;
            foreach (var l in due)
                if (l.TryExpire(now))
                {
                    count++;
                    audit.Add("license.expired", "License", l.Id.ToString(), tenantId: l.TenantId);
                }
            await db.SaveChangesAsync(ct);
            foreach (var l in due) await cache.RemoveAsync(LicenseCacheKeys.For(l.TenantId, l.ProductKeyHash), ct);
            if (due.Count < batchSize) break;
        }
        return count;
    }

    /// <summary>Consumer of SubscriptionExpiredV1 / SubscriptionCancelledV1: the subscription's licenses stop working.</summary>
    public async Task<int> ExpireForSubscriptionAsync(Guid subscriptionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var licenses = await db.Licenses.Where(l => l.SubscriptionId == subscriptionId).ToListAsync(ct);
        var count = licenses.Count(l => l.TryExpire(now, force: true));
        await db.SaveChangesAsync(ct);
        foreach (var l in licenses) await cache.RemoveAsync(LicenseCacheKeys.For(l.TenantId, l.ProductKeyHash), ct);
        return count;
    }

    private static string NewLicenseNumber(DateTimeOffset now)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"LIC-{now:yyyy}-{chars}";
    }
}

public static class LicenseCacheKeys
{
    public static string For(Guid tenantId, string keyHash) => $"lic:{tenantId:N}:{keyHash}";
}

/// <summary>Anything that changes whether a license validates (subscription, customer, tenant) drops its cached result.</summary>
public static class LicenseCacheInvalidation
{
    public static Task ForSubscriptionAsync(IAppDbContext db, ILicenseCache cache, Guid subscriptionId, CancellationToken ct) =>
        RemoveAsync(db.Licenses.IgnoreQueryFilters().Where(l => l.SubscriptionId == subscriptionId), cache, ct);

    public static Task ForCustomerAsync(IAppDbContext db, ILicenseCache cache, Guid customerId, CancellationToken ct) =>
        RemoveAsync(db.Licenses.IgnoreQueryFilters().Where(l => l.CustomerId == customerId), cache, ct);

    public static Task ForTenantAsync(IAppDbContext db, ILicenseCache cache, Guid tenantId, CancellationToken ct) =>
        RemoveAsync(db.Licenses.IgnoreQueryFilters().Where(l => l.TenantId == tenantId), cache, ct);

    private static async Task RemoveAsync(IQueryable<License> licenses, ILicenseCache cache, CancellationToken ct)
    {
        var keys = await licenses.Select(l => new { l.TenantId, l.ProductKeyHash }).ToListAsync(ct);
        foreach (var k in keys) await cache.RemoveAsync(LicenseCacheKeys.For(k.TenantId, k.ProductKeyHash), ct);
    }
}
