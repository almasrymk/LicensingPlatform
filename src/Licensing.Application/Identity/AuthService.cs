using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Domain.Tenants;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Identity;

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ClientTokenRequest(string ClientId, string ClientSecret);

public sealed record UserProfile(
    Guid Id, string Email, string FullName, string Role, Guid? TenantId, string? TenantName,
    Guid? CustomerId, string? CustomerName, IReadOnlyList<string> Permissions, string Language, string ImageUrl = "");

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, UserProfile User);
public sealed record ClientTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);

public static class AuthErrors
{
    // Same message for unknown user and wrong password, so emails cannot be enumerated.
    public static readonly Error InvalidCredentials = Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Email or password is incorrect.");
    public static readonly Error Locked = new("AUTH_LOCKED", "Too many failed attempts. Try again later.", ErrorKind.Locked);
    public static readonly Error InvalidRefreshToken = Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "The session has expired. Sign in again.");
    public static readonly Error InvalidClient = Error.Unauthorized("AUTH_INVALID_CLIENT", "Client credentials are invalid.");
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ISecretHasher secretHasher,
    IJwtTokenService jwt,
    IAuditLogger audit,
    TimeProvider clock)
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            await audit.WriteNowAsync("auth.login", "User", null, false, "unknown email", null, ct);
            return AuthErrors.InvalidCredentials;
        }

        if (user.IsLockedOut(now))
        {
            await audit.WriteNowAsync("auth.login", "User", user.Id.ToString(), false, "locked out", user.TenantId, ct);
            return AuthErrors.Locked;
        }

        if (!passwordHasher.Verify(request.Password ?? "", user.PasswordHash))
        {
            user.RegisterFailedLogin(now);
            audit.Add("auth.login", "User", user.Id.ToString(), false, user.IsLockedOut(now) ? "wrong password, locked" : "wrong password", user.TenantId);
            await db.SaveChangesAsync(ct);
            return user.IsLockedOut(now) ? AuthErrors.Locked : AuthErrors.InvalidCredentials;
        }

        if (!user.IsActive || !await TenantIsActiveAsync(user.TenantId, ct))
        {
            await audit.WriteNowAsync("auth.login", "User", user.Id.ToString(), false, "inactive user or tenant", user.TenantId, ct);
            return AuthErrors.InvalidCredentials;
        }

        user.RegisterSuccessfulLogin(now);
        var response = await IssueAsync(user, Guid.CreateVersion7(), now, ct);
        audit.Add("auth.login", "User", user.Id.ToString(), true, null, user.TenantId);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return AuthErrors.InvalidRefreshToken;

        var hash = secretHasher.Hash(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return AuthErrors.InvalidRefreshToken;

        if (!token.IsActive(now))
        {
            if (token.RevokedAt is not null && token.ReplacedById is not null)
            {
                // A rotated token was presented again: assume theft and end the whole session family.
                var family = await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null).ToListAsync(ct);
                family.ForEach(t => t.Revoke(now));
                audit.Add("auth.refresh_reuse_detected", "User", token.UserId.ToString(), false, "refresh token family revoked");
                await db.SaveChangesAsync(ct);
            }
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.IsActive || !await TenantIsActiveAsync(user.TenantId, ct))
            return AuthErrors.InvalidRefreshToken;

        var response = await IssueAsync(user, token.FamilyId, now, ct, rotated: token);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;
        var hash = secretHasher.Hash(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        token.Revoke(clock.GetUtcNow());
        audit.Add("auth.logout", "User", token.UserId.ToString());
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<UserProfile>> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? AppErrors.NotFound("User") : await ToProfileAsync(user, ct);
    }

    public async Task<Result> SetLanguageAsync(Guid userId, string language, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AppErrors.NotFound("User");
        user.SetLanguage(language);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AppErrors.NotFound("User");
        if (!passwordHasher.Verify(currentPassword, user.PasswordHash)) return AuthErrors.InvalidCredentials;
        if (PasswordPolicy.Check(newPassword) is { } error) return error;
        user.ChangePassword(passwordHasher.Hash(newPassword));
        var now = clock.GetUtcNow();
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.Revoke(now));
        audit.Add("auth.password_changed", "User", userId.ToString(), tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<ClientTokenResponse>> ClientTokenAsync(ClientTokenRequest request, CancellationToken ct)
    {
        var client = await db.ApiClients.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.ClientId == request.ClientId, ct);
        if (client is null || client.Status != ApiClientStatus.Active ||
            !CryptoCompare(client.SecretHash, secretHasher.Hash(request.ClientSecret ?? "")) ||
            !await TenantIsActiveAsync(client.TenantId, ct))
        {
            await audit.WriteNowAsync("auth.client_token", "ApiClient", client?.Id.ToString(), false, "invalid client", client?.TenantId, ct);
            return AuthErrors.InvalidClient;
        }

        // The caller is anonymous until the token exists, so update the row directly instead of through the tenant-scoped unit of work.
        var now = clock.GetUtcNow();
        await db.ApiClients.IgnoreQueryFilters().Where(c => c.Id == client.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, now), ct);
        var token = jwt.CreateClientToken(client);
        audit.Add("auth.client_token", "ApiClient", client.Id.ToString(), true, null, client.TenantId);
        await db.SaveChangesAsync(ct);
        return new ClientTokenResponse(token.Token, "Bearer",
            (int)(token.ExpiresAt - clock.GetUtcNow()).TotalSeconds, string.Join(' ', client.Scopes));
    }

    private async Task<AuthResponse> IssueAsync(User user, Guid familyId, DateTimeOffset now, CancellationToken ct, RefreshToken? rotated = null)
    {
        var permissions = Permissions.ForRole(user.Role);
        var access = jwt.CreateUserToken(user, permissions);
        var refreshPlain = secretHasher.GenerateToken();
        var refresh = RefreshToken.Issue(user.Id, secretHasher.Hash(refreshPlain), familyId, now, RefreshTokenLifetime);
        db.RefreshTokens.Add(refresh);
        rotated?.Revoke(now, refresh.Id);
        return new AuthResponse(access.Token, access.ExpiresAt, refreshPlain, await ToProfileAsync(user, ct));
    }

    private async Task<UserProfile> ToProfileAsync(User user, CancellationToken ct)
    {
        string? tenantName = null, customerName = null;
        if (user.TenantId is { } tid)
            tenantName = await db.Tenants.Where(t => t.Id == tid).Select(t => t.Name).FirstOrDefaultAsync(ct);
        if (user.CustomerId is { } cid)
            customerName = await db.Customers.IgnoreQueryFilters().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct);
        return new UserProfile(user.Id, user.Email, user.FullName, user.Role, user.TenantId, tenantName,
            user.CustomerId, customerName, Permissions.ForRole(user.Role), user.PreferredLanguage, Media.MediaService.UrlFor(user.ImageId));
    }

    private async Task<bool> TenantIsActiveAsync(Guid? tenantId, CancellationToken ct) =>
        tenantId is null || await db.Tenants.AnyAsync(t => t.Id == tenantId && t.Status == TenantStatus.Active, ct);

    private static bool CryptoCompare(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}

public static class PasswordPolicy
{
    public static Error? Check(string? password) =>
        password is { Length: >= 8 } && password.Any(char.IsDigit) && password.Any(char.IsLetter)
            ? null
            : Error.Validation("WEAK_PASSWORD", "Password must be at least 8 characters and contain letters and digits.");
}
