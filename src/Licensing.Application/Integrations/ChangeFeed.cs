using Licensing.SharedKernel;

namespace Licensing.Application.Integrations;

/// <summary>
/// One entry of the integration change feed (INT-006, ADR-002). Only ids and the event type leave the platform;
/// the stored payload (which may hold a product key hash) is never returned.
/// </summary>
/// <param name="DeviceId">INT-024: the Licensing device id, on <c>LicenseActivationResetV1</c> only.</param>
public sealed record ChangeFeedItem(Guid Id, string Type, DateTimeOffset OccurredAt, Guid? CustomerId, Guid? SubscriptionId, Guid? LicenseId, string? DeviceId = null);

/// <summary><paramref name="NextCursor"/> is opaque; pass it back unchanged. Null only when the page is empty and no cursor was given.</summary>
public sealed record ChangeFeedPage(IReadOnlyList<ChangeFeedItem> Items, string? NextCursor, bool HasMore);

/// <summary>
/// Incremental feed over the outbox of the caller's tenant, ordered by (occurredAt, id). Monitor Cloud keeps the cursor
/// and still runs a periodic full reconcile, so a missed entry is repaired (ADR-002).
/// </summary>
public interface IChangeFeed
{
    Task<Result<ChangeFeedPage>> ReadAsync(Guid tenantId, string? cursor, int take, CancellationToken ct);
}

public static class ChangeFeedErrors
{
    public static readonly Error CursorInvalid = Error.Validation("INTEGRATION_CURSOR_INVALID", "The change feed cursor is not valid. Start again without a cursor.");
}
