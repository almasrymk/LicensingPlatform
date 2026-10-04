namespace Licensing.SharedKernel;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();
}

public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Marker for any row that belongs to exactly one tenant. Isolation is enforced by global query filters.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Rows that additionally belong to a single customer of a tenant (customer portal users only see their own).</summary>
public interface ICustomerOwned : ITenantOwned
{
    Guid CustomerId { get; }
}
