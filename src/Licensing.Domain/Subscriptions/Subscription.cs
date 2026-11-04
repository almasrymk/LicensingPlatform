using Licensing.SharedKernel;

namespace Licensing.Domain.Subscriptions;

public enum SubscriptionStatus { Trial = 1, Active = 2, Suspended = 3, Cancelled = 4, Expired = 5 }

public enum SubscriptionAction { Start, Renew, ChangePlan, Suspend, Resume, Cancel, Expire }

/// <summary>
/// Subscription lifecycle (plan section 8.5):
/// Start → Trial | Active; Renew: Trial/Active/Expired → Active; ChangePlan: Trial/Active;
/// Suspend: Trial/Active → Suspended; Resume: Suspended → Active; Cancel: any non-terminal → Cancelled;
/// Expire: Trial/Active/Suspended past end date → Expired. Cancelled is terminal.
/// </summary>
public sealed class Subscription : AggregateRoot, ICustomerOwned
{
    private readonly List<SubscriptionHistory> _history = [];

    private Subscription() { }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid PlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartDate { get; private set; }
    /// <summary>Null means lifetime.</summary>
    public DateTimeOffset? EndDate { get; private set; }
    public DateTimeOffset? TrialEndsAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// The caller's own id for this subscription (INT-016), unique per tenant. A repeated start with the same reference
    /// returns this subscription instead of creating another one.
    /// </summary>
    public string? ExternalReference { get; private set; }

    /// <summary>
    /// INT-015 (ADR-004): active devices across every license of this subscription. The plan's MaxActivations limits
    /// this total, so issuing another license never adds capacity. Changed only by single conditional UPDATEs
    /// (<c>IAppDbContext.TryReserveActivationSlotAsync</c> and the release methods), never through the tracked entity.
    /// </summary>
    public int ActiveDevices { get; private set; }

    /// <summary>
    /// INT-013: true when the customer suspended it themselves (through the website). A customer can resume only such a
    /// suspension; one made by the provider's staff stays until staff resume it. Rows suspended before this existed are
    /// treated as staff suspensions.
    /// </summary>
    public bool SuspendedByCustomer { get; private set; }
    /// <summary>Optimistic concurrency token.</summary>
    public int Version { get; private set; }
    public IReadOnlyCollection<SubscriptionHistory> History => _history.AsReadOnly();

    public bool IsLifetime => EndDate is null;
    public bool IsUsable => Status is SubscriptionStatus.Active or SubscriptionStatus.Trial;

    public static IReadOnlyList<SubscriptionAction> AllowedActions(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.Trial => [SubscriptionAction.Renew, SubscriptionAction.ChangePlan, SubscriptionAction.Suspend, SubscriptionAction.Cancel, SubscriptionAction.Expire],
        SubscriptionStatus.Active => [SubscriptionAction.Renew, SubscriptionAction.ChangePlan, SubscriptionAction.Suspend, SubscriptionAction.Cancel, SubscriptionAction.Expire],
        SubscriptionStatus.Suspended => [SubscriptionAction.Resume, SubscriptionAction.Cancel, SubscriptionAction.Expire],
        SubscriptionStatus.Expired => [SubscriptionAction.Renew],
        _ => [],
    };

    public static Subscription Start(
        Guid tenantId, Guid customerId, Guid productId, Guid planId, DateTimeOffset start,
        int? durationDays, int? trialDays, DateTimeOffset now, string? notes = null, string? externalReference = null)
    {
        var sub = new Subscription
        {
            TenantId = tenantId,
            CustomerId = customerId,
            ProductId = productId,
            PlanId = planId,
            StartDate = start,
            CreatedAt = now,
            Notes = notes?.Trim(),
            ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim(),
        };

        if (trialDays is > 0)
        {
            sub.Status = SubscriptionStatus.Trial;
            sub.TrialEndsAt = start.AddDays(trialDays.Value);
            sub.EndDate = sub.TrialEndsAt;
        }
        else
        {
            sub.Status = SubscriptionStatus.Active;
            sub.EndDate = durationDays is null ? null : start.AddDays(durationDays.Value);
        }

        sub.Record(SubscriptionAction.Start, null, sub.Status, now, null);
        sub.Raise(new SubscriptionStartedV1(sub.Id, tenantId, customerId, planId, sub.StartDate, sub.EndDate, sub.Status == SubscriptionStatus.Trial));
        return sub;
    }

    /// <param name="reference">INT-018: the caller's id for this renewal (a paid order). Kept on the history entry, unique per subscription.</param>
    public void Renew(int? durationDays, DateTimeOffset now, string? reference = null)
    {
        Ensure(SubscriptionAction.Renew);
        var from = Status;
        if (durationDays is null)
        {
            EndDate = null;
        }
        else
        {
            var baseDate = EndDate is { } end && end > now && Status != SubscriptionStatus.Trial ? end : now;
            EndDate = baseDate.AddDays(durationDays.Value);
        }
        TrialEndsAt = null;
        Status = SubscriptionStatus.Active;
        Record(SubscriptionAction.Renew, from, Status, now, EndDate is null ? "lifetime" : $"until {EndDate:yyyy-MM-dd}",
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());
        Raise(new SubscriptionRenewedV1(Id, TenantId, EndDate));
    }

    public void ChangePlan(Guid newPlanId, int? durationDays, DateTimeOffset now)
    {
        Ensure(SubscriptionAction.ChangePlan);
        if (newPlanId == PlanId)
            throw new DomainException(Error.Conflict("SUBSCRIPTION_SAME_PLAN", "Subscription is already on this plan."));
        var oldPlan = PlanId;
        PlanId = newPlanId;
        if (durationDays is null) EndDate = null;
        Record(SubscriptionAction.ChangePlan, Status, Status, now, $"plan {oldPlan} -> {newPlanId}");
        Raise(new SubscriptionPlanChangedV1(Id, TenantId, oldPlan, newPlanId));
    }

    public static readonly Error SuspendedByProvider =
        Error.Conflict("SUBSCRIPTION_SUSPENDED_BY_PROVIDER", "This subscription was suspended by your provider. Contact them to resume it.");

    public void Suspend(string? reason, DateTimeOffset now, bool byCustomer = false)
    {
        Ensure(SubscriptionAction.Suspend);
        var from = Status;
        Status = SubscriptionStatus.Suspended;
        SuspendedByCustomer = byCustomer;
        Record(SubscriptionAction.Suspend, from, Status, now, reason);
        Raise(new SubscriptionSuspendedV1(Id, TenantId));
    }

    public void Resume(DateTimeOffset now, bool byCustomer = false)
    {
        Ensure(SubscriptionAction.Resume);
        if (byCustomer && !SuspendedByCustomer) throw new DomainException(SuspendedByProvider);
        SuspendedByCustomer = false;
        Status = TrialEndsAt is not null && TrialEndsAt > now ? SubscriptionStatus.Trial : SubscriptionStatus.Active;
        Record(SubscriptionAction.Resume, SubscriptionStatus.Suspended, Status, now, null);
        Raise(new SubscriptionResumedV1(Id, TenantId));
    }

    public void Cancel(string? reason, DateTimeOffset now)
    {
        Ensure(SubscriptionAction.Cancel);
        var from = Status;
        Status = SubscriptionStatus.Cancelled;
        CancelledAt = now;
        Record(SubscriptionAction.Cancel, from, Status, now, reason);
        Raise(new SubscriptionCancelledV1(Id, TenantId));
    }

    /// <summary>Idempotent: returns false when there is nothing to expire, so the hourly job can be re-run safely.</summary>
    public bool TryExpire(DateTimeOffset now)
    {
        if (!AllowedActions(Status).Contains(SubscriptionAction.Expire) || EndDate is null || EndDate > now)
            return false;
        var from = Status;
        Status = SubscriptionStatus.Expired;
        Record(SubscriptionAction.Expire, from, Status, now, null);
        Raise(new SubscriptionExpiredV1(Id, TenantId, CustomerId));
        return true;
    }

    private void Ensure(SubscriptionAction action)
    {
        if (!AllowedActions(Status).Contains(action))
            throw new DomainException(Error.Conflict(
                "SUBSCRIPTION_INVALID_TRANSITION", $"Cannot {action} a subscription that is {Status}."));
    }

    private void Record(SubscriptionAction action, SubscriptionStatus? from, SubscriptionStatus to, DateTimeOffset at, string? details, string? reference = null)
    {
        Version++;
        _history.Add(new SubscriptionHistory(Id, TenantId, action, from, to, at, details, reference));
    }
}

public sealed class SubscriptionHistory : Entity, ITenantOwned
{
    private SubscriptionHistory() { }

    internal SubscriptionHistory(Guid subscriptionId, Guid tenantId, SubscriptionAction action,
        SubscriptionStatus? fromStatus, SubscriptionStatus toStatus, DateTimeOffset at, string? details, string? reference = null)
    {
        Reference = reference;
        SubscriptionId = subscriptionId;
        TenantId = tenantId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        At = at;
        Details = details;
    }

    public Guid SubscriptionId { get; private set; }
    public Guid TenantId { get; private set; }
    public SubscriptionAction Action { get; private set; }
    public SubscriptionStatus? FromStatus { get; private set; }
    public SubscriptionStatus ToStatus { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string? Details { get; private set; }

    /// <summary>The caller's id of the action (a renewal's paid order). Unique per subscription.</summary>
    public string? Reference { get; private set; }
}

// Integration events published through the outbox. The V1 suffix is part of the contract.
public sealed record SubscriptionStartedV1(Guid SubscriptionId, Guid TenantId, Guid CustomerId, Guid PlanId,
    DateTimeOffset StartDate, DateTimeOffset? EndDate, bool IsTrial) : DomainEvent;
public sealed record SubscriptionExpiredV1(Guid SubscriptionId, Guid TenantId, Guid CustomerId) : DomainEvent;
public sealed record SubscriptionRenewedV1(Guid SubscriptionId, Guid TenantId, DateTimeOffset? EndDate) : DomainEvent;
public sealed record SubscriptionPlanChangedV1(Guid SubscriptionId, Guid TenantId, Guid OldPlanId, Guid NewPlanId) : DomainEvent;
public sealed record SubscriptionSuspendedV1(Guid SubscriptionId, Guid TenantId) : DomainEvent;
public sealed record SubscriptionResumedV1(Guid SubscriptionId, Guid TenantId) : DomainEvent;
public sealed record SubscriptionCancelledV1(Guid SubscriptionId, Guid TenantId) : DomainEvent;
