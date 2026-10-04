using Licensing.Domain.Common;
using Licensing.SharedKernel;

namespace Licensing.Domain.Customers;

public enum CustomerStatus { Active = 1, Inactive = 2 }

public sealed class Customer : AggregateRoot, ITenantOwned
{
    private readonly List<CustomerContact> _contacts = [];

    private Customer() { Name = null!; }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Country { get; private set; }
    public string? TaxNumber { get; private set; }
    public CustomerStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<CustomerContact> Contacts => _contacts.AsReadOnly();

    public static Customer Create(Guid tenantId, string name, string? email, string? phone, string? country, string? taxNumber, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        Name = Guard.NotEmpty(name, "Name"),
        Email = string.IsNullOrWhiteSpace(email) ? null : EmailAddress.Create(email).Value,
        Phone = phone?.Trim(),
        Country = country?.Trim(),
        TaxNumber = taxNumber?.Trim(),
        Status = CustomerStatus.Active,
        CreatedAt = now,
    };

    public void Update(string name, string? email, string? phone, string? country, string? taxNumber)
    {
        Name = Guard.NotEmpty(name, "Name");
        Email = string.IsNullOrWhiteSpace(email) ? null : EmailAddress.Create(email).Value;
        Phone = phone?.Trim();
        Country = country?.Trim();
        TaxNumber = taxNumber?.Trim();
    }

    public void SetStatus(CustomerStatus status) => Status = status;

    public CustomerContact AddContact(string name, string? email, string? phone, string? jobTitle, bool isPrimary)
    {
        if (isPrimary)
            foreach (var c in _contacts) c.IsPrimary = false;
        var contact = new CustomerContact(Id, TenantId, name, email, phone, jobTitle, isPrimary || _contacts.Count == 0);
        _contacts.Add(contact);
        return contact;
    }

    public bool RemoveContact(Guid contactId) => _contacts.RemoveAll(c => c.Id == contactId) > 0;
}

public sealed class CustomerContact : Entity, ITenantOwned
{
    private CustomerContact() { Name = null!; }

    internal CustomerContact(Guid customerId, Guid tenantId, string name, string? email, string? phone, string? jobTitle, bool isPrimary)
    {
        CustomerId = customerId;
        TenantId = tenantId;
        Name = Guard.NotEmpty(name, "Contact name");
        Email = string.IsNullOrWhiteSpace(email) ? null : EmailAddress.Create(email).Value;
        Phone = phone?.Trim();
        JobTitle = jobTitle?.Trim();
        IsPrimary = isPrimary;
    }

    public Guid CustomerId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? JobTitle { get; private set; }
    public bool IsPrimary { get; internal set; }
}
