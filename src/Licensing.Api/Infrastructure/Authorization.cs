using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Infrastructure.Security;
using Licensing.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Licensing.Api.Infrastructure;

/// <summary>
/// Requires a portal user whose role grants the permission, or (E7) an API client holding the scope of the same name when
/// the permission is in <see cref="ApiScopes.GrantablePermissions"/>. Enforced server-side whatever the UI shows.
/// </summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(AuthPolicies.Permission(permission));

/// <summary>Requires an API client token that carries the scope.</summary>
public sealed class RequireScopeAttribute(string scope) : AuthorizeAttribute(AuthPolicies.Scope(scope));

public static class AuthPolicies
{
    public const string PlatformAdmin = "platform-admin";
    public const string AnyUser = "any-user";

    public static string Permission(string permission) => $"perm:{permission}";
    public static string Scope(string scope) => $"scope:{scope}";

    public static void Register(AuthorizationOptions options)
    {
        foreach (var permission in Permissions.All)
        {
            var grantable = ApiScopes.GrantablePermissions.Contains(permission);
            options.AddPolicy(Permission(permission), p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                ctx.User.FindFirst(ClaimNames.TokenType)?.Value switch
                {
                    "user" => ctx.User.HasClaim(ClaimNames.Permission, permission),
                    "client" => grantable && HasScope(ctx.User, permission),
                    _ => false,
                }));
        }

        foreach (var scope in ApiScopes.All)
            options.AddPolicy(Scope(scope), p => p.RequireAuthenticatedUser()
                .RequireClaim(ClaimNames.TokenType, "client")
                .RequireAssertion(ctx => HasScope(ctx.User, scope)));

        options.AddPolicy(PlatformAdmin, p => p.RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.TokenType, "user").RequireRole(Roles.PlatformAdmin));
        options.AddPolicy(AnyUser, p => p.RequireAuthenticatedUser().RequireClaim(ClaimNames.TokenType, "user"));
    }

    private static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope) =>
        (user.FindFirst(ClaimNames.Scope)?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope);
}

/// <summary>Writes 401/403 as ProblemDetails with AUTH_UNAUTHORIZED / AUTH_FORBIDDEN codes.</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext http, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Challenged || result.Forbidden)
        {
            var error = result.Forbidden
                ? Error.Forbidden("AUTH_FORBIDDEN", "You do not have permission to perform this action.")
                : Error.Unauthorized("AUTH_UNAUTHORIZED", "Authentication is required.");
            var problem = ApiResults.Problem(http, error);
            http.Response.StatusCode = problem.Status!.Value;
            if (result.Challenged) http.Response.Headers.WWWAuthenticate = "Bearer";
            await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
            return;
        }
        await _default.HandleAsync(next, http, policy, result);
    }
}
