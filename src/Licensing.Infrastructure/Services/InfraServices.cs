using System.Text.Json;
using Licensing.Application.Abstractions;
using Licensing.Domain.Audit;
using Licensing.Infrastructure.Identity;
using Licensing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Licensing.Infrastructure.Services;

public sealed class AuditLogger(AppDbContext db, ICurrentUser user, TenantContext scope, IServiceScopeFactory scopes, TimeProvider clock) : IAuditLogger
{
    public void Add(string action, string entityType, string? entityId, bool success = true, string? details = null, Guid? tenantId = null) =>
        db.AuditRecords.Add(Create(action, entityType, entityId, success, details, tenantId));

    public async Task WriteNowAsync(string action, string entityType, string? entityId, bool success, string? details, Guid? tenantId, CancellationToken ct)
    {
        var record = Create(action, entityType, entityId, success, details, tenantId);
        using var s = scopes.CreateScope();
        s.ServiceProvider.GetRequiredService<TenantContext>().RunAsSystem();
        var separate = s.ServiceProvider.GetRequiredService<AppDbContext>();
        separate.AuditRecords.Add(record);
        await separate.SaveChangesAsync(ct);
    }

    private AuditRecord Create(string action, string entityType, string? entityId, bool success, string? details, Guid? tenantId)
    {
        var isSystem = scope.IsSystem && !user.IsAuthenticated;
        return AuditRecord.Create(
            tenantId ?? user.TenantId,
            isSystem ? ActorType.System : user.ActorType,
            user.ActorId ?? (isSystem ? "system" : null),
            user.ActorName ?? (isSystem ? "system" : null),
            action, entityType, entityId, success, details, user.IpAddress, user.CorrelationId, clock.GetUtcNow());
    }
}

public sealed class EfIdempotencyStore(AppDbContext db, TimeProvider clock) : IIdempotencyStore
{
    public async Task<IdempotentResponse?> GetAsync(string scope, string key, CancellationToken ct)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Scope == scope && r.Key == key, ct);
        return record is null ? null : new IdempotentResponse(record.StatusCode, record.Body);
    }

    public async Task SaveAsync(string scope, string key, IdempotentResponse response, CancellationToken ct)
    {
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = scope, Key = key, StatusCode = response.StatusCode, Body = response.Body, CreatedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request with the same key stored its response first; the first one wins.
            db.ChangeTracker.Clear();
        }
    }
}

/// <summary>
/// Validate cache over IDistributedCache (Redis when configured, memory otherwise). Any cache failure is logged and treated
/// as a miss, so the database remains the source of truth when Redis is down.
/// </summary>
public sealed class DistributedLicenseCache(IDistributedCache cache, ILogger<DistributedLicenseCache> logger) : ILicenseCache
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "License cache read failed; falling back to the database");
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct) where T : class
    {
        try
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "License cache write failed");
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        try
        {
            await cache.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "License cache invalidation failed; entry will expire with its TTL");
        }
    }
}
