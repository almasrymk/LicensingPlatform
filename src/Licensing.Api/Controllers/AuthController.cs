using Licensing.Api.Infrastructure;
using Licensing.Application.Abstractions;
using Licensing.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Tags("Auth")]
public sealed class AuthController(AuthService auth, ICurrentUser me) : ControllerBase
{
    /// <summary>Signs a portal user in. Returns a short-lived access token and a rotating refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        this.ToActionResult(await auth.LoginAsync(request, ct));

    /// <summary>Exchanges a refresh token for a new pair. The presented token is revoked; reusing it revokes the whole session.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct) =>
        this.ToActionResult(await auth.RefreshAsync(request, ct));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>OAuth2-style client credentials for API clients. The token carries tenant_id, client_id and scopes.</summary>
    [HttpPost("client-token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.ClientToken)]
    public async Task<IActionResult> ClientToken(ClientTokenRequest request, CancellationToken ct) =>
        this.ToActionResult(await auth.ClientTokenAsync(request, ct));

    [HttpGet("me")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        this.ToActionResult(await auth.GetProfileAsync(me.UserId!.Value, ct));

    public sealed record LanguageRequest(string Language);

    [HttpPut("me/language")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    public async Task<IActionResult> SetLanguage(LanguageRequest request, CancellationToken ct) =>
        this.ToActionResult(await auth.SetLanguageAsync(me.UserId!.Value, request.Language, ct));

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPut("me/password")]
    [Authorize(Policy = AuthPolicies.AnyUser)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        this.ToActionResult(await auth.ChangePasswordAsync(me.UserId!.Value, request.CurrentPassword, request.NewPassword, ct));
}
