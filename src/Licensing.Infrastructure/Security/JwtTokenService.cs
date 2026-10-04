using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Licensing.Application.Abstractions;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Licensing.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "licensing-platform";
    public string Audience { get; set; } = "licensing-platform";
    /// <summary>HMAC key for user and client access tokens (at least 32 bytes). Separate from license-signing keys.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int ClientTokenMinutes { get; set; } = 60;

    public SymmetricSecurityKey GetKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}

public static class ClaimNames
{
    public const string TenantId = "tid";
    public const string CustomerId = "cid";
    public const string Permission = "perm";
    public const string Scope = "scope";
    public const string ClientId = "client_id";
    public const string TokenType = "typ_lic";
    public const string Language = "lang";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenService
{
    private readonly JwtOptions _o = options.Value;

    public AccessToken CreateUserToken(User user, IReadOnlyCollection<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new(ClaimNames.TokenType, "user"),
            new(ClaimNames.Language, user.PreferredLanguage),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (user.TenantId is { } tid) claims.Add(new Claim(ClaimNames.TenantId, tid.ToString()));
        if (user.CustomerId is { } cid) claims.Add(new Claim(ClaimNames.CustomerId, cid.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(ClaimNames.Permission, p)));
        return Create(claims, TimeSpan.FromMinutes(_o.AccessTokenMinutes));
    }

    public AccessToken CreateClientToken(ApiClient client)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, client.Id.ToString()),
            new(ClaimNames.ClientId, client.ClientId),
            new(JwtRegisteredClaimNames.Name, client.Name),
            new(ClaimNames.TenantId, client.TenantId.ToString()),
            new(ClaimNames.TokenType, "client"),
            new(ClaimNames.Scope, client.ScopesValue),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        return Create(claims, TimeSpan.FromMinutes(_o.ClientTokenMinutes));
    }

    private AccessToken Create(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var now = clock.GetUtcNow();
        var expires = now.Add(lifetime);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(_o.GetKey(), SecurityAlgorithms.HmacSha256));
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
