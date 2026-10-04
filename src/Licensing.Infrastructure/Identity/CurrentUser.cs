using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Licensing.Application.Abstractions;
using Licensing.Domain.Audit;
using Licensing.Domain.Identity;
using Licensing.Infrastructure.Security;
using Microsoft.AspNetCore.Http;

namespace Licensing.Infrastructure.Identity;

/// <summary>Reads the caller from the validated token. Tenant and customer come from claims only, never from request data.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private string? Claim(string type) => Principal?.FindFirst(type)?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public bool IsClient => Claim(ClaimNames.TokenType) == "client";

    public ActorType ActorType => !IsAuthenticated ? ActorType.Anonymous : IsClient ? ActorType.ApiClient : ActorType.User;
    public string? ActorId => Claim(JwtRegisteredClaimNames.Sub) ?? Claim(ClaimTypes.NameIdentifier);
    public string? ActorName => IsClient ? Claim(ClaimNames.ClientId) : Claim(JwtRegisteredClaimNames.Email) ?? Claim(ClaimTypes.Email);
    public Guid? UserId => !IsClient && Guid.TryParse(ActorId, out var id) ? id : null;
    public Guid? TenantId => Guid.TryParse(Claim(ClaimNames.TenantId), out var id) ? id : null;
    public Guid? CustomerId => Guid.TryParse(Claim(ClaimNames.CustomerId), out var id) ? id : null;
    public string? Role => Claim(ClaimTypes.Role);
    public bool IsPlatformAdmin => !IsClient && Role == Roles.PlatformAdmin;

    public IReadOnlyCollection<string> Permissions =>
        Principal?.FindAll(ClaimNames.Permission).Select(c => c.Value).ToList() ?? [];

    public IReadOnlyCollection<string> Scopes =>
        Claim(ClaimNames.Scope)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Resolves the data scope for the request:
/// tenant users → their tenant (and customer, for customer users); platform admins → all tenants, or one tenant
/// they explicitly select with the X-Tenant-Id header; anonymous → nothing; background jobs → <see cref="RunAsSystem"/>.
/// </summary>
public sealed class TenantContext(ICurrentUser user, IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantHeader = "X-Tenant-Id";
    private bool _system;
    private Guid? _systemTenant;

    public void RunAsSystem(Guid? tenantId = null)
    {
        _system = true;
        _systemTenant = tenantId;
    }

    public bool IsSystem => _system;

    public bool IsUnrestricted => _system || user.IsPlatformAdmin;

    public Guid? TenantId
    {
        get
        {
            if (_system) return _systemTenant;
            if (user.IsPlatformAdmin)
                return Guid.TryParse(accessor.HttpContext?.Request.Headers[TenantHeader].FirstOrDefault(), out var selected) ? selected : null;
            return user.TenantId;
        }
    }

    public Guid? CustomerId => _system || user.IsPlatformAdmin ? null : user.CustomerId;
}

/// <summary>Identity used by background jobs and the outbox dispatcher.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public ActorType ActorType => ActorType.System;
    public string? ActorId => "system";
    public string? ActorName => "system";
    public Guid? UserId => null;
    public Guid? TenantId => null;
    public Guid? CustomerId => null;
    public string? Role => null;
    public bool IsPlatformAdmin => false;
    public IReadOnlyCollection<string> Permissions => [];
    public IReadOnlyCollection<string> Scopes => [];
    public string? IpAddress => null;
    public string? CorrelationId => null;
    public bool HasPermission(string permission) => false;
}
