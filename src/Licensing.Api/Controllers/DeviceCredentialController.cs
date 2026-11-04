using Licensing.Api.Infrastructure;
using Licensing.Application.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Controllers;

/// <summary>
/// INT-002 (ADR-001): licensing for an installation that has no API client. <c>activate</c> takes the product key and
/// returns a device credential once; the other calls send it in the <c>X-Device-Credential</c> header. Anonymous,
/// rate limited per IP. The API-client routes of <see cref="LicensingController"/> are unchanged.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/licensing/device")]
[Tags("Licensing (device credential)")]
[EnableRateLimiting(RateLimits.DeviceLicensing)]
public sealed class DeviceCredentialController(DeviceLicensingService licensing) : ControllerBase
{
    public const string CredentialHeader = "X-Device-Credential";

    /// <summary>
    /// Activates this device with a product key and returns <c>{ license, deviceCredential }</c>. The credential is
    /// shown only here. Activating the same device again returns a new credential and invalidates the previous one.
    /// </summary>
    [HttpPost("activate")]
    public async Task<IActionResult> Activate(ActivateRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return this.ToActionResult(await licensing.ActivateWithKeyAsync(request, ct));
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate(DeviceCheckRequest request, [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken ct) =>
        this.ToActionResult(await licensing.CheckWithCredentialAsync(request, credential, heartbeat: false, ct));

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(DeviceCheckRequest request, [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken ct) =>
        this.ToActionResult(await licensing.CheckWithCredentialAsync(request, credential, heartbeat: true, ct));

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(DeviceCheckRequest request, [FromHeader(Name = CredentialHeader)] string? credential, CancellationToken ct) =>
        this.ToActionResult(await licensing.DeactivateWithCredentialAsync(request, credential, ct));
}
