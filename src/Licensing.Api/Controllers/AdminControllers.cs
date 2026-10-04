using Licensing.Api.Infrastructure;
using Licensing.Application.Audit;
using Licensing.Application.Common;
using Licensing.Application.Customers;
using Licensing.Application.Identity;
using Licensing.Application.Tenants;
using Licensing.Domain.Customers;
using Licensing.Domain.Identity;
using Licensing.Domain.Tenants;
using Microsoft.AspNetCore.Mvc;

namespace Licensing.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Tags("Tenants")]
public sealed class TenantsController(TenantService tenants) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.TenantsRead)]
    public Task<PagedResult<TenantDto>> List([FromQuery] PageQuery page, [FromQuery] TenantFilter filter, CancellationToken ct) =>
        tenants.ListAsync(page, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.TenantsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await tenants.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.TenantsManage)]
    public async Task<IActionResult> Create(CreateTenantRequest request, CancellationToken ct) =>
        this.ToCreated(await tenants.CreateAsync(request, ct), t => t.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.TenantsManage)]
    public async Task<IActionResult> Update(Guid id, UpdateTenantRequest request, CancellationToken ct) =>
        this.ToActionResult(await tenants.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.TenantsManage)]
    public async Task<IActionResult> Suspend(Guid id, SuspendRequest request, CancellationToken ct) =>
        this.ToActionResult(await tenants.SuspendAsync(id, request, ct));

    [HttpPost("{id:guid}/resume")]
    [HasPermission(Permissions.TenantsManage)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct) => this.ToActionResult(await tenants.ResumeAsync(id, ct));
}

[ApiController]
[Route("api/v1/customers")]
[Tags("Customers")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CustomersRead)]
    public Task<PagedResult<CustomerDto>> List([FromQuery] PageQuery page, [FromQuery] CustomerFilter filter, CancellationToken ct) =>
        customers.ListAsync(page, filter, ct);

    [HttpGet("countries")]
    [HasPermission(Permissions.CustomersRead)]
    public Task<IReadOnlyList<string>> Countries(CancellationToken ct) => customers.CountriesAsync(ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CustomersRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await customers.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> Create(SaveCustomerRequest request, CancellationToken ct) =>
        this.ToCreated(await customers.CreateAsync(request, ct), c => c.Customer.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> Update(Guid id, SaveCustomerRequest request, CancellationToken ct) =>
        this.ToActionResult(await customers.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}/status")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> SetStatus(Guid id, SetCustomerStatusRequest request, CancellationToken ct) =>
        this.ToActionResult(await customers.SetStatusAsync(id, request.Status, ct));

    [HttpPost("{id:guid}/contacts")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> AddContact(Guid id, AddContactRequest request, CancellationToken ct) =>
        this.ToActionResult(await customers.AddContactAsync(id, request, ct));

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    [HasPermission(Permissions.CustomersManage)]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken ct) =>
        this.ToActionResult(await customers.RemoveContactAsync(id, contactId, ct));
}

[ApiController]
[Route("api/v1/users")]
[Tags("Users")]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.UsersManage)]
    public Task<PagedResult<UserDto>> List([FromQuery] PageQuery page, [FromQuery] UserFilter filter, CancellationToken ct) =>
        users.ListAsync(page, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await users.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct) =>
        this.ToCreated(await users.CreateAsync(request, ct), u => u.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        this.ToActionResult(await users.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => this.ToActionResult(await users.SetActiveAsync(id, true, ct));

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) => this.ToActionResult(await users.SetActiveAsync(id, false, ct));

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.UsersManage)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct) =>
        this.ToActionResult(await users.ResetPasswordAsync(id, request, ct));
}

[ApiController]
[Route("api/v1/audit")]
[Tags("Audit")]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.AuditRead)]
    public Task<PagedResult<AuditDto>> List([FromQuery] AuditQuery query, CancellationToken ct) => audit.ListAsync(query, ct);
}
