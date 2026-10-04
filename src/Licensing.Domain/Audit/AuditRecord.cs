namespace Licensing.Domain.Audit;

public enum ActorType { Anonymous = 0, User = 1, ApiClient = 2, System = 3 }

/// <summary>
/// Append-only record of a sensitive action. Must never contain product keys, client secrets, passwords or tokens.
/// TenantId is null for platform-level actions.
/// </summary>
public sealed class AuditRecord
{
    private AuditRecord() { Action = EntityType = null!; }

    public long Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public ActorType ActorType { get; private set; }
    public string? ActorId { get; private set; }
    public string? ActorName { get; private set; }
    public string Action { get; private set; }
    public string EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public bool Success { get; private set; }
    public string? Details { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static AuditRecord Create(Guid? tenantId, ActorType actorType, string? actorId, string? actorName, string action,
        string entityType, string? entityId, bool success, string? details, string? ip, string? correlationId, DateTimeOffset at) => new()
    {
        TenantId = tenantId,
        ActorType = actorType,
        ActorId = actorId,
        ActorName = actorName,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Success = success,
        Details = details is { Length: > 2000 } ? details[..2000] : details,
        IpAddress = ip,
        CorrelationId = correlationId,
        At = at,
    };
}
