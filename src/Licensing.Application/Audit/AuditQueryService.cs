using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Audit;

public sealed record AuditDto(
    long Id, Guid? TenantId, ActorType ActorType, string? ActorId, string? ActorName, string Action, string EntityType,
    string? EntityId, bool Success, string? Details, string? IpAddress, string? CorrelationId, DateTimeOffset At);

public sealed record AuditQuery(int Page = 1, int PageSize = 50, string? Action = null, string? EntityType = null, string? EntityId = null,
    bool? Success = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Actor = null, Guid? TenantId = null);

public sealed class AuditQueryService(IAppDbContext db, ITenantContext scope, TimeProvider clock)
{
    /// <summary>Audit rows have a nullable TenantId (platform actions), so the tenant scope is applied here.</summary>
    public Task<PagedResult<AuditDto>> ListAsync(AuditQuery query, CancellationToken ct)
    {
        var q = db.AuditRecords.AsQueryable();
        if (!scope.IsUnrestricted) q = q.Where(a => a.TenantId == scope.TenantId);
        else if (scope.TenantId is not null) q = q.Where(a => a.TenantId == scope.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(a => a.Action.StartsWith(query.Action));
        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) q = q.Where(a => a.EntityId == query.EntityId);
        if (query.Success is not null) q = q.Where(a => a.Success == query.Success);
        if (!string.IsNullOrWhiteSpace(query.Actor)) q = q.Where(a => a.ActorName != null && a.ActorName.Contains(query.Actor));
        if (query.TenantId is { } tid && scope.IsUnrestricted) q = q.Where(a => a.TenantId == tid);
        if (query.From is not null) q = q.Where(a => a.At >= query.From);
        if (query.To is not null) q = q.Where(a => a.At <= query.To);

        return q.OrderByDescending(a => a.Id)
            .Select(a => new AuditDto(a.Id, a.TenantId, a.ActorType, a.ActorId, a.ActorName, a.Action, a.EntityType, a.EntityId,
                a.Success, a.Details, a.IpAddress, a.CorrelationId, a.At))
            .ToPagedAsync(new PageQuery(query.Page, Math.Min(query.PageSize, 200)), ct);
    }

    /// <summary>Retention job: deletes audit rows older than the retention period, in batches.</summary>
    public async Task<int> PurgeOlderThanAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow() - retention;
        return await db.AuditRecords.Where(a => a.At < cutoff).ExecuteDeleteAsync(ct);
    }
}
