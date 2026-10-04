using Licensing.Application.Abstractions;
using Licensing.Application.Licensing;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.SharedKernel;

namespace Licensing.Application.Events;

/// <summary>Consumes an integration event delivered by the outbox dispatcher. Each handler runs at most once per event (inbox).</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>Licensing reacts to the subscription ending: its licenses stop validating with LIC_SUBSCRIPTION_EXPIRED.</summary>
public sealed class ExpireLicensesOnSubscriptionExpired(LicenseService licenses) :
    IIntegrationEventHandler<SubscriptionExpiredV1>, IIntegrationEventHandler<SubscriptionCancelledV1>
{
    public Task HandleAsync(SubscriptionExpiredV1 e, CancellationToken ct) => licenses.ExpireForSubscriptionAsync(e.SubscriptionId, ct);
    public Task HandleAsync(SubscriptionCancelledV1 e, CancellationToken ct) => licenses.ExpireForSubscriptionAsync(e.SubscriptionId, ct);
}

/// <summary>Any license status change invalidates the validate cache (in addition to the synchronous removal).</summary>
public sealed class InvalidateLicenseCache(ILicenseCache cache) : IIntegrationEventHandler<LicenseStatusChangedV1>
{
    public Task HandleAsync(LicenseStatusChangedV1 e, CancellationToken ct) =>
        cache.RemoveAsync(LicenseCacheKeys.For(e.TenantId, e.ProductKeyHash), ct);
}
