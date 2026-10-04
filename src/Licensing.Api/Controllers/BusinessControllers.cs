using Licensing.Api.Infrastructure;
using Licensing.Application.Abstractions;
using Licensing.Application.Catalog;
using Licensing.Application.Common;
using Licensing.Application.Integrations;
using Licensing.Application.Licensing;
using Licensing.Application.Reports;
using Licensing.Application.Subscriptions;
using Licensing.Domain.Catalog;
using Licensing.Domain.Identity;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Licensing.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Tags("Catalog")]
public sealed class ProductsController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CatalogRead)]
    public Task<PagedResult<ProductDto>> List([FromQuery] PageQuery page, CancellationToken ct) => catalog.ListProductsAsync(page, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CatalogRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await catalog.GetProductAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Create(SaveProductRequest request, CancellationToken ct) =>
        this.ToCreated(await catalog.CreateProductAsync(request, ct), p => p.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Update(Guid id, SaveProductRequest request, CancellationToken ct) =>
        this.ToActionResult(await catalog.UpdateProductAsync(id, request, ct));
}

[ApiController]
[Route("api/v1/plans")]
[Tags("Catalog")]
public sealed class PlansController(CatalogService catalog) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.CatalogRead)]
    public Task<PagedResult<PlanDto>> List([FromQuery] PageQuery page, [FromQuery] Guid? productId, [FromQuery] PlanStatus? status, CancellationToken ct) =>
        catalog.ListPlansAsync(page, productId, status, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.CatalogRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await catalog.GetPlanAsync(id, ct));

    /// <summary>The plan as machine-readable entitlements (what a license issued from it grants).</summary>
    [HttpGet("{id:guid}/entitlements")]
    [HasPermission(Permissions.CatalogRead)]
    public async Task<IActionResult> Entitlements(Guid id, CancellationToken ct) => this.ToActionResult(await catalog.GetEntitlementsAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Create(SavePlanRequest request, CancellationToken ct) =>
        this.ToCreated(await catalog.CreatePlanAsync(request, ct), p => p.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Update(Guid id, SavePlanRequest request, CancellationToken ct) =>
        this.ToActionResult(await catalog.UpdatePlanAsync(id, request, ct));

    [HttpPost("{id:guid}/publish")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => this.ToActionResult(await catalog.PublishPlanAsync(id, ct));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) => this.ToActionResult(await catalog.ArchivePlanAsync(id, ct));

    [HttpPost("{id:guid}/new-version")]
    [HasPermission(Permissions.CatalogManage)]
    public async Task<IActionResult> NewVersion(Guid id, CancellationToken ct) =>
        this.ToCreated(await catalog.NewPlanVersionAsync(id, ct), p => p.Id);
}

[ApiController]
[Route("api/v1/subscriptions")]
[Tags("Subscriptions")]
public sealed class SubscriptionsController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.SubscriptionsRead)]
    public Task<PagedResult<SubscriptionDto>> List([FromQuery] PageQuery page, [FromQuery] Guid? customerId, [FromQuery] SubscriptionStatus? status, CancellationToken ct) =>
        subscriptions.ListAsync(page, customerId, status, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.SubscriptionsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await subscriptions.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> Start(StartSubscriptionRequest request, CancellationToken ct) =>
        this.ToCreated(await subscriptions.StartAsync(request, ct), s => s.Subscription.Id);

    [HttpPost("{id:guid}/renew")]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> Renew(Guid id, RenewSubscriptionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.RenewAsync(id, request, ct));

    [HttpPost("{id:guid}/change-plan")]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> ChangePlan(Guid id, ChangePlanRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.ChangePlanAsync(id, request, ct));

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> Suspend(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.SuspendAsync(id, request, ct));

    [HttpPost("{id:guid}/resume")]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> Resume(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.ResumeAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.SubscriptionsManage)]
    public async Task<IActionResult> Cancel(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.CancelAsync(id, request, ct));
}

[ApiController]
[Route("api/v1/licenses")]
[Tags("Licenses")]
public sealed class LicensesController(LicenseService licenses) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.LicensesRead)]
    public Task<PagedResult<LicenseDto>> List([FromQuery] PageQuery page, [FromQuery] Guid? customerId, [FromQuery] Guid? subscriptionId,
        [FromQuery] LicenseStatus? status, CancellationToken ct) => licenses.ListAsync(page, customerId, subscriptionId, status, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.LicensesRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => this.ToActionResult(await licenses.GetAsync(id, ct));

    /// <summary>Issues a license. The response is the only time the full product key is returned.</summary>
    [HttpPost]
    [HasPermission(Permissions.LicensesManage)]
    public async Task<IActionResult> Issue(IssueLicenseRequest request, CancellationToken ct) =>
        this.ToCreated(await licenses.IssueAsync(request, ct), l => l.License.Id);

    [HttpPost("{id:guid}/suspend")]
    [HasPermission(Permissions.LicensesManage)]
    public async Task<IActionResult> Suspend(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await licenses.SuspendAsync(id, request, ct));

    [HttpPost("{id:guid}/resume")]
    [HasPermission(Permissions.LicensesManage)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct) => this.ToActionResult(await licenses.ResumeAsync(id, ct));

    [HttpPost("{id:guid}/revoke")]
    [HasPermission(Permissions.LicensesManage)]
    public async Task<IActionResult> Revoke(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await licenses.RevokeAsync(id, request, ct));

    [HttpPost("{id:guid}/activations/{activationId:guid}/reset")]
    [HasPermission(Permissions.LicensesManage)]
    public async Task<IActionResult> ResetDevice(Guid id, Guid activationId, CancellationToken ct) =>
        this.ToActionResult(await licenses.ResetDeviceAsync(id, activationId, ct));
}

[ApiController]
[Route("api/v1/api-clients")]
[Tags("Integrations")]
public sealed class ApiClientsController(ApiClientService clients) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.ApiClientsManage)]
    public Task<IReadOnlyList<ApiClientDto>> List(CancellationToken ct) => clients.ListAsync(ct);

    /// <summary>Creates a client. The secret is returned once and stored hashed.</summary>
    [HttpPost]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Create(CreateApiClientRequest request, CancellationToken ct) =>
        this.ToCreated(await clients.CreateAsync(request, ct), c => c.Client.Id);

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Update(Guid id, UpdateApiClientRequest request, CancellationToken ct) =>
        this.ToActionResult(await clients.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/rotate-secret")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Rotate(Guid id, CancellationToken ct) => this.ToActionResult(await clients.RotateSecretAsync(id, ct));

    [HttpPost("{id:guid}/disable")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct) => this.ToActionResult(await clients.DisableAsync(id, ct));

    [HttpPost("{id:guid}/enable")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct) => this.ToActionResult(await clients.EnableAsync(id, ct));

    [HttpPost("{id:guid}/revoke")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) => this.ToActionResult(await clients.RevokeAsync(id, ct));

    [HttpGet("/api/v1/webhooks")]
    [HasPermission(Permissions.ApiClientsManage)]
    public Task<IReadOnlyList<WebhookDto>> Webhooks(CancellationToken ct) => clients.ListWebhooksAsync(ct);

    [HttpPost("/api/v1/webhooks")]
    [HasPermission(Permissions.ApiClientsManage)]
    public async Task<IActionResult> CreateWebhook(CreateWebhookRequest request, CancellationToken ct) =>
        this.ToCreated(await clients.CreateWebhookAsync(request, ct), w => w.Id);
}

[ApiController]
[Route("api/v1/reports")]
[Tags("Reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpGet("dashboard")]
    [HasPermission(Permissions.ReportsRead)]
    public Task<DashboardDto> Dashboard(CancellationToken ct) => reports.DashboardAsync(ct);

    [HttpGet("expiring-licenses")]
    [HasPermission(Permissions.ReportsRead)]
    public Task<IReadOnlyList<ExpiringLicenseDto>> Expiring([FromQuery] int days = 30, CancellationToken ct = default) =>
        reports.ExpiringLicensesAsync(days, ct);

    [HttpGet("active-devices")]
    [HasPermission(Permissions.ReportsRead)]
    public Task<IReadOnlyList<ActiveDeviceDto>> ActiveDevices(CancellationToken ct) => reports.ActiveDevicesAsync(ct);

    public sealed record FailedActivationsResponse(IReadOnlyList<FailedActivationDto> Items, IReadOnlyList<FailedActivationSummaryDto> Summary);

    [HttpGet("failed-activations")]
    [HasPermission(Permissions.ReportsRead)]
    public async Task<FailedActivationsResponse> Failed([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var (items, summary) = await reports.FailedActivationsAsync(days, ct);
        return new FailedActivationsResponse(items, summary);
    }

    [HttpGet("usage")]
    [HasPermission(Permissions.ReportsRead)]
    public Task<IReadOnlyList<UsageDto>> Usage([FromQuery] int days = 30, CancellationToken ct = default) => reports.UsageAsync(days, ct);
}

[ApiController]
[Route("api/v1/signing-keys")]
[Tags("Licensing")]
public sealed class SigningKeysController(ILicenseTokenSigner signer, Application.Abstractions.IAuditLogger audit, IAppDbContext db) : ControllerBase
{
    /// <summary>Public keys that verify offline license tokens (active and retiring). Clients cache this and select by kid.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    public async Task<IActionResult> PublicKeys(CancellationToken ct)
    {
        var keys = await signer.GetPublicKeysAsync(ct);
        return Ok(new
        {
            keys = keys.Select(k => new { kty = "EC", crv = k.Crv, x = k.X, y = k.Y, kid = k.Kid, alg = k.Algorithm, use = "sig", status = k.Status }),
            pem = keys.Select(k => new { k.Kid, k.Status, k.PublicKeyPem }),
        });
    }

    /// <summary>Rotates the license-signing key. The previous key keeps verifying (status "retiring").</summary>
    [HttpPost("rotate")]
    [HasPermission(Permissions.SigningKeysManage)]
    public async Task<IActionResult> Rotate(CancellationToken ct)
    {
        var kid = await signer.RotateAsync(ct);
        audit.Add("signing_key.rotated", "SigningKey", kid);
        await db.SaveChangesAsync(ct);
        return Ok(new { kid });
    }
}
