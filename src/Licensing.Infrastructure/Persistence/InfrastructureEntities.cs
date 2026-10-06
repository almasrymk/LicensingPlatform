namespace Licensing.Infrastructure.Persistence;

/// <summary>Integration event written in the same transaction as the business change, published later by the dispatcher.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public Guid? TenantId { get; set; }
    /// <summary>The customer the event is about, for the integration change feed (LP-4). Null for tenant-level events.</summary>
    public Guid? CustomerId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public bool DeadLettered { get; set; }
}

/// <summary>Records that a handler has consumed an event, so redelivery never runs it twice.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Handler { get; set; } = null!;
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Stored response for an Idempotency-Key, scoped per tenant/client and endpoint.</summary>
public sealed class IdempotencyRecord
{
    public long Id { get; set; }
    public string Scope { get; set; } = null!;
    public string Key { get; set; } = null!;
    public int StatusCode { get; set; }
    public string Body { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
