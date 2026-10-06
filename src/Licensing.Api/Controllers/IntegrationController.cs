using Licensing.Api.Infrastructure;
using Licensing.Application.Common;
using Licensing.Application.Integrations;
using Licensing.Domain.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Controllers;

/// <summary>
/// Endpoints for integrated platforms such as Monitor Cloud (LP-3/4/5). API client tokens only, scoped to the client's
/// tenant, rate limited like the licensing endpoints.
/// </summary>
[ApiController]
[Route("api/v1/integration")]
[Tags("Integration")]
[EnableRateLimiting(RateLimits.Licensing)]
public sealed class IntegrationController(IntegrationService integration) : ControllerBase
{
    /// <summary>Customers of the client's tenant, oldest first.</summary>
    [HttpGet("customers")]
    [RequireScope(ApiScopes.CustomersRead)]
    public async Task<IActionResult> Customers([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] DateTimeOffset? updatedSince = null, CancellationToken ct = default) =>
        this.ToActionResult(await integration.ListCustomersAsync(new PageQuery(page, pageSize), updatedSince, ct));

    /// <summary>Subscriptions and licences of a customer for one product.</summary>
    [HttpGet("customers/{id:guid}/entitlements")]
    [RequireScope(ApiScopes.SubscriptionsRead)]
    public async Task<IActionResult> Entitlements(Guid id, [FromQuery] string? productCode, CancellationToken ct) =>
        this.ToActionResult(await integration.GetEntitlementsAsync(id, productCode, ct));

    /// <summary>Published plans of a product.</summary>
    [HttpGet("plans")]
    [RequireScope(ApiScopes.CatalogRead)]
    public async Task<IActionResult> Plans([FromQuery] string? productCode, CancellationToken ct) =>
        this.ToActionResult(await integration.ListPlansAsync(productCode, ct));

    /// <summary>Activations (devices) of a licence.</summary>
    [HttpGet("licenses/{id:guid}/activations")]
    [RequireScope(ApiScopes.LicensesRead)]
    public async Task<IActionResult> Activations(Guid id, CancellationToken ct) =>
        this.ToActionResult(await integration.ListActivationsAsync(id, ct));

    /// <summary>Heartbeat of an activated device by licence id (no product key needed after activation).</summary>
    [HttpPost("licenses/{id:guid}/devices/{deviceId}/heartbeat")]
    [RequireScope(ApiScopes.LicensesValidate)]
    public async Task<IActionResult> Heartbeat(Guid id, string deviceId, [FromBody] DeviceCheckRequest? request, CancellationToken ct) =>
        this.ToActionResult(await integration.HeartbeatAsync(id, deviceId, request ?? new DeviceCheckRequest(null, null), ct));

    /// <summary>Releases the device's seat by licence id.</summary>
    [HttpPost("licenses/{id:guid}/devices/{deviceId}/release")]
    [RequireScope(ApiScopes.LicensesActivate)]
    public async Task<IActionResult> Release(Guid id, string deviceId, CancellationToken ct) =>
        this.ToActionResult(await integration.ReleaseAsync(id, deviceId, ct));

    /// <summary>Integration events in order with a cursor (change feed over the outbox).</summary>
    [HttpGet("changes")]
    [RequireScope(ApiScopes.SubscriptionsRead)]
    public async Task<IActionResult> Changes([FromQuery] string? cursor, [FromQuery] int take = 100, CancellationToken ct = default) =>
        this.ToActionResult(await integration.GetChangesAsync(cursor, take, ct));
}
