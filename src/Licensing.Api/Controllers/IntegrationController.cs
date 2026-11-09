using Licensing.Api.Infrastructure;
using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Application.Integrations;
using Licensing.Application.Licensing;
using Licensing.Application.Subscriptions;
using Licensing.Domain.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Controllers;

/// <summary>
/// Machine API for Monitor Cloud. Client tokens only; every query stays inside the client's tenant.
/// Subscription and license changes delegate to the same services the portal uses.
/// </summary>
[ApiController]
[Route("api/v1/integration")]
[Tags("Integration")]
[EnableRateLimiting(RateLimits.Licensing)]
public sealed class IntegrationController(
    IntegrationReadService reads, SubscriptionService subscriptions, LicenseService licenses, DeviceLicensingService devices, IChangeFeed changes, ICurrentUser caller) : ControllerBase
{
    public sealed record SeatHeartbeatRequest(string? AppVersion, string? Os);

    [HttpGet("customers")]
    [RequireScope(ApiScopes.CustomersRead)]
    public Task<PagedResult<IntegrationCustomer>> Customers([FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] DateTimeOffset? updatedSince = null, CancellationToken ct = default) =>
        reads.CustomersAsync(new PageQuery(page, pageSize), updatedSince, ct);

    [HttpGet("customers/{id:guid}/entitlements")]
    [RequireScope(ApiScopes.SubscriptionsRead)]
    public async Task<IActionResult> Entitlements(Guid id, [FromQuery] string? productCode, CancellationToken ct) =>
        this.ToActionResult(await reads.EntitlementsAsync(id, productCode, ct));

    /// <summary>
    /// INT-006: the tenant's changes ordered by (occurredAt, id); types are the domain event names (*V1). Pass
    /// nextCursor back unchanged; an unknown cursor is INTEGRATION_CURSOR_INVALID (start again without one).
    /// </summary>
    [HttpGet("changes")]
    [RequireScope(ApiScopes.SubscriptionsRead)]
    public async Task<IActionResult> Changes([FromQuery] string? cursor, [FromQuery] int take = 100, CancellationToken ct = default) =>
        this.ToActionResult(await changes.ReadAsync(caller.TenantId!.Value, cursor, take, ct));

    [HttpGet("plans")]
    [RequireScope(ApiScopes.CatalogRead)]
    public Task<IReadOnlyList<IntegrationPlan>> Plans([FromQuery] string? productCode, CancellationToken ct) =>
        reads.PlansAsync(productCode, ct);

    [HttpPost("subscriptions/{id:guid}/change-plan")]
    [RequireScope(ApiScopes.SubscriptionsManage)]
    public async Task<IActionResult> ChangePlan(Guid id, ChangePlanRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.ChangePlanAsync(id, request, ct));

    [HttpPost("subscriptions/{id:guid}/suspend")]
    [RequireScope(ApiScopes.SubscriptionsManage)]
    public async Task<IActionResult> Suspend(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.SuspendAsync(id, request, ct));

    [HttpPost("subscriptions/{id:guid}/resume")]
    [RequireScope(ApiScopes.SubscriptionsManage)]
    public async Task<IActionResult> Resume(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.ResumeAsync(id, request, ct));

    [HttpPost("subscriptions/{id:guid}/cancel")]
    [RequireScope(ApiScopes.SubscriptionsManage)]
    public async Task<IActionResult> Cancel(Guid id, SubscriptionActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.CancelAsync(id, request, ct));

    [HttpPost("subscriptions/{id:guid}/renew")]
    [RequireScope(ApiScopes.SubscriptionsManage)]
    public async Task<IActionResult> Renew(Guid id, RenewSubscriptionRequest request, CancellationToken ct) =>
        this.ToActionResult(await subscriptions.RenewAsync(id, request, ct));

    /// <summary>Issues another device license on a subscription. The product key is returned only in this response.</summary>
    [HttpPost("licenses")]
    [RequireScope(ApiScopes.LicensesIssue)]
    public async Task<IActionResult> Issue(IssueLicenseRequest request, CancellationToken ct) =>
        this.ToCreated(await licenses.IssueAsync(request, ct), l => l.License.Id);

    /// <summary>Replaces the product key and releases every device. The new key is returned once.</summary>
    [HttpPost("licenses/{id:guid}/regenerate-key")]
    [RequireScope(ApiScopes.LicensesManage)]
    public async Task<IActionResult> RegenerateKey(Guid id, CancellationToken ct) =>
        this.ToActionResult(await licenses.RegenerateKeyAsync(id, ct));

    [HttpPost("licenses/{id:guid}/suspend")]
    [RequireScope(ApiScopes.LicensesManage)]
    public async Task<IActionResult> SuspendLicense(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await licenses.SuspendAsync(id, request, ct));

    [HttpPost("licenses/{id:guid}/resume")]
    [RequireScope(ApiScopes.LicensesManage)]
    public async Task<IActionResult> ResumeLicense(Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] LicenseActionRequest? request, CancellationToken ct) =>
        this.ToActionResult(await licenses.ResumeAsync(id, ct, request?.ByCustomer ?? false));

    /// <summary>INT-004: heartbeat of a seat held by Monitor Cloud; answers like <c>/licensing/heartbeat</c>.</summary>
    [HttpPost("licenses/{id:guid}/devices/{deviceId}/heartbeat")]
    [RequireScope(ApiScopes.LicensesValidate)]
    public async Task<IActionResult> SeatHeartbeat(Guid id, string deviceId, SeatHeartbeatRequest? request, CancellationToken ct) =>
        this.ToActionResult(await devices.HeartbeatSeatAsync(id, deviceId, request?.AppVersion, request?.Os, ct));

    /// <summary>INT-005: gives a seat back. 204, or <c>LIC_DEVICE_NOT_ACTIVATED</c> when the device holds none.</summary>
    [HttpPost("licenses/{id:guid}/devices/{deviceId}/release")]
    [RequireScope(ApiScopes.LicensesActivate)]
    public async Task<IActionResult> SeatRelease(Guid id, string deviceId, CancellationToken ct) =>
        this.ToActionResult(await devices.ReleaseSeatAsync(id, deviceId, ct));

    [HttpPost("licenses/{id:guid}/revoke")]
    [RequireScope(ApiScopes.LicensesManage)]
    public async Task<IActionResult> RevokeLicense(Guid id, LicenseActionRequest request, CancellationToken ct) =>
        this.ToActionResult(await licenses.RevokeAsync(id, request, ct));
}
