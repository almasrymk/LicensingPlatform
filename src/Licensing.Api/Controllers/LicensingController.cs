using System.Text.Json;
using Licensing.Api.Infrastructure;
using Licensing.Application.Abstractions;
using Licensing.Application.Licensing;
using Licensing.Domain.Integrations;
using Licensing.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Controllers;

/// <summary>
/// Endpoints called by licensed software (ADR-011): authenticated with an API client token, scoped to the client's tenant,
/// rate limited per client.
/// </summary>
[ApiController]
[Route("api/v1/licensing")]
[Tags("Licensing (devices)")]
[EnableRateLimiting(RateLimits.Licensing)]
public sealed class LicensingController(DeviceLicensingService licensing, IIdempotencyStore idempotency, ICurrentUser caller) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Activates a device. Send an <c>Idempotency-Key</c> header so a retried request returns the original result
    /// instead of consuming another activation.
    /// </summary>
    [HttpPost("activate")]
    [RequireScope(ApiScopes.LicensesActivate)]
    public async Task<IActionResult> Activate(ActivateRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var scope = $"activate:{caller.TenantId}:{caller.ActorId}";
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (idempotencyKey.Length > 100)
                return this.ToProblem(Error.Validation("VALIDATION_FAILED", "Idempotency-Key must be at most 100 characters."));
            var stored = await idempotency.GetAsync(scope, idempotencyKey, ct);
            if (stored is not null)
            {
                Response.Headers["Idempotent-Replayed"] = "true";
                return new ContentResult { StatusCode = stored.StatusCode, Content = stored.Body, ContentType = "application/json" };
            }
        }

        var result = await licensing.ActivateAsync(request, ct);
        IActionResult response = result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var (status, body) = result.IsSuccess
                ? (200, JsonSerializer.Serialize(result.Value, Json))
                : (ApiResults.StatusFor(result.Error!.Kind), JsonSerializer.Serialize(ApiResults.Problem(HttpContext, result.Error!), Json));
            await idempotency.SaveAsync(scope, idempotencyKey, new IdempotentResponse(status, body), ct);
        }
        return response;
    }

    /// <summary>Checks a license for an activated device. Served from a short-lived cache that is invalidated on any status change.</summary>
    [HttpPost("validate")]
    [RequireScope(ApiScopes.LicensesValidate)]
    public async Task<IActionResult> Validate(ValidateRequest request, CancellationToken ct) =>
        this.ToActionResult(await licensing.ValidateAsync(request, ct));

    /// <summary>Periodic check-in. Always reads the database and returns a fresh signed token.</summary>
    [HttpPost("heartbeat")]
    [RequireScope(ApiScopes.LicensesValidate)]
    public async Task<IActionResult> Heartbeat(ValidateRequest request, CancellationToken ct) =>
        this.ToActionResult(await licensing.HeartbeatAsync(request, ct));

    /// <summary>Releases this device's activation slot.</summary>
    [HttpPost("deactivate")]
    [RequireScope(ApiScopes.LicensesActivate)]
    public async Task<IActionResult> Deactivate(DeactivateRequest request, CancellationToken ct) =>
        this.ToActionResult(await licensing.DeactivateAsync(request, ct));
}
