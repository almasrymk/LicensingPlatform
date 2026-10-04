using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Customers;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Customers;

public sealed record ContactDto(Guid Id, string Name, string? Email, string? Phone, string? JobTitle, bool IsPrimary);

public sealed record CustomerDto(
    Guid Id, Guid TenantId, string? TenantName, string Name, string? Email, string? Phone, string? Country, string? TaxNumber,
    CustomerStatus Status, DateTimeOffset CreatedAt, int ActiveSubscriptions, int ActiveLicenses, Guid? ImageId)
{
    public string ImageUrl => Media.MediaService.UrlFor(ImageId);
}

public sealed record CustomerFilter(CustomerStatus? Status = null, string? Country = null, Guid? TenantId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record CustomerDetailsDto(CustomerDto Customer, IReadOnlyList<ContactDto> Contacts);

public sealed record SaveCustomerRequest(string Name, string? Email, string? Phone, string? Country, string? TaxNumber, Guid? TenantId = null);
public sealed record AddContactRequest(string Name, string? Email, string? Phone, string? JobTitle, bool IsPrimary);
public sealed record SetCustomerStatusRequest(CustomerStatus Status);

public sealed class CustomerService(IAppDbContext db, ICurrentUser me, ITenantContext scope, IAuditLogger audit, ILicenseCache cache, TimeProvider clock)
{
    /// <summary>Customer users only ever see their own customer record.</summary>
    private IQueryable<Customer> Scoped() =>
        scope.CustomerId is { } cid ? db.Customers.Where(c => c.Id == cid) : db.Customers;

    private IQueryable<CustomerDto> Project(IQueryable<Customer> q) => q.Select(c => new CustomerDto(
        c.Id, c.TenantId, db.Tenants.Where(t => t.Id == c.TenantId).Select(t => t.Name).FirstOrDefault(),
        c.Name, c.Email, c.Phone, c.Country, c.TaxNumber, c.Status, c.CreatedAt,
        db.Subscriptions.Count(s => s.CustomerId == c.Id && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial)),
        db.Licenses.Count(l => l.CustomerId == c.Id && l.Status == LicenseStatus.Active),
        c.ImageId));

    public Task<PagedResult<CustomerDto>> ListAsync(PageQuery page, CustomerFilter filter, CancellationToken ct)
    {
        var q = Scoped();
        if (!string.IsNullOrWhiteSpace(page.Search))
        {
            var s = page.Search.Trim();
            q = q.Where(c => c.Name.Contains(s) || (c.Email != null && c.Email.Contains(s)) || (c.Phone != null && c.Phone.Contains(s)));
        }
        if (filter.Status is not null) q = q.Where(c => c.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Country)) q = q.Where(c => c.Country == filter.Country);
        if (filter.TenantId is { } tid && scope.IsUnrestricted) q = q.Where(c => c.TenantId == tid);
        if (filter.From is not null) q = q.Where(c => c.CreatedAt >= filter.From);
        if (filter.To is not null) q = q.Where(c => c.CreatedAt <= filter.To);
        return Project(q.OrderBy(c => c.Name)).ToPagedAsync(page, ct);
    }

    public async Task<IReadOnlyList<string>> CountriesAsync(CancellationToken ct) =>
        await Scoped().Where(c => c.Country != null && c.Country != "").Select(c => c.Country!).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<Result<CustomerDetailsDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await Project(Scoped().Where(c => c.Id == id)).FirstOrDefaultAsync(ct);
        if (dto is null) return AppErrors.NotFound("Customer");
        var contacts = await db.CustomerContacts.Where(c => c.CustomerId == id)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name)
            .Select(c => new ContactDto(c.Id, c.Name, c.Email, c.Phone, c.JobTitle, c.IsPrimary))
            .ToListAsync(ct);
        return new CustomerDetailsDto(dto, contacts);
    }

    public async Task<Result<CustomerDetailsDto>> CreateAsync(SaveCustomerRequest request, CancellationToken ct)
    {
        var tenant = me.ResolveWriteTenant(scope, request.TenantId);
        if (tenant.IsFailure) return tenant.Error!;
        if (!await db.Tenants.AnyAsync(t => t.Id == tenant.Value, ct)) return AppErrors.NotFound("Tenant");

        var customer = Customer.Create(tenant.Value, request.Name, request.Email, request.Phone, request.Country, request.TaxNumber, clock.GetUtcNow());
        db.Customers.Add(customer);
        audit.Add("customer.created", "Customer", customer.Id.ToString(), tenantId: customer.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<Result<CustomerDetailsDto>> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await Scoped().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return AppErrors.NotFound("Customer");
        customer.Update(request.Name, request.Email, request.Phone, request.Country, request.TaxNumber);
        audit.Add("customer.updated", "Customer", id.ToString(), tenantId: customer.TenantId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<CustomerDetailsDto>> SetStatusAsync(Guid id, CustomerStatus status, CancellationToken ct)
    {
        var customer = await Scoped().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return AppErrors.NotFound("Customer");
        customer.SetStatus(status);
        audit.Add("customer.status_changed", "Customer", id.ToString(), details: status.ToString(), tenantId: customer.TenantId);
        await db.SaveChangesAsync(ct);
        await Licensing.LicenseCacheInvalidation.ForCustomerAsync(db, cache, id, ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<CustomerDetailsDto>> AddContactAsync(Guid id, AddContactRequest request, CancellationToken ct)
    {
        var customer = await Scoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return AppErrors.NotFound("Customer");
        customer.AddContact(request.Name, request.Email, request.Phone, request.JobTitle, request.IsPrimary);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<CustomerDetailsDto>> RemoveContactAsync(Guid id, Guid contactId, CancellationToken ct)
    {
        var customer = await Scoped().Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return AppErrors.NotFound("Customer");
        if (!customer.RemoveContact(contactId)) return AppErrors.NotFound("Contact");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
