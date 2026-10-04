using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Identity;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Identity;

public sealed record UserDto(
    Guid Id, string Email, string FullName, string Role, Guid? TenantId, string? TenantName,
    Guid? CustomerId, string? CustomerName, bool IsActive, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(string Email, string FullName, string Password, string Role, Guid? TenantId, Guid? CustomerId);
public sealed record UpdateUserRequest(string FullName, string? Role);
public sealed record ResetPasswordRequest(string NewPassword);

/// <summary>
/// Users are not a tenant-owned table (platform admins have no tenant), so the tenant scope is applied here explicitly.
/// </summary>
public sealed class UserService(IAppDbContext db, ICurrentUser me, ITenantContext scope, IPasswordHasher hasher, IAuditLogger audit, TimeProvider clock)
{
    private IQueryable<User> Scoped()
    {
        var q = db.Users.AsQueryable();
        if (scope.IsUnrestricted) return q;
        return scope.TenantId is { } tid ? q.Where(u => u.TenantId == tid) : q.Where(_ => false);
    }

    public async Task<PagedResult<UserDto>> ListAsync(PageQuery page, string? role, CancellationToken ct)
    {
        var q = Scoped();
        if (!string.IsNullOrWhiteSpace(page.Search))
            q = q.Where(u => u.Email.Contains(page.Search) || u.FullName.Contains(page.Search));
        if (!string.IsNullOrWhiteSpace(role)) q = q.Where(u => u.Role == role);

        var result = await q.OrderBy(u => u.Role).ThenBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role, u.TenantId,
                db.Tenants.Where(t => t.Id == u.TenantId).Select(t => t.Name).FirstOrDefault(),
                u.CustomerId,
                db.Customers.Where(c => c.Id == u.CustomerId).Select(c => c.Name).FirstOrDefault(),
                u.IsActive, u.LastLoginAt, u.CreatedAt))
            .ToPagedAsync(page, ct);
        return result;
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        if (PasswordPolicy.Check(request.Password) is { } weak) return weak;
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return AppErrors.Duplicate("A user with this email");

        var now = clock.GetUtcNow();
        User user;
        if (request.Role == Roles.PlatformAdmin)
        {
            if (!me.IsPlatformAdmin) return AppErrors.Forbidden;
            user = User.CreatePlatformAdmin(email, request.FullName, hasher.Hash(request.Password), now);
        }
        else
        {
            var tenant = me.ResolveWriteTenant(scope, request.TenantId);
            if (tenant.IsFailure) return tenant.Error!;
            if (!await db.Tenants.AnyAsync(t => t.Id == tenant.Value, ct)) return AppErrors.NotFound("Tenant");

            if (request.Role == Roles.CustomerUser)
            {
                // Customers is tenant-filtered, so a customer of another tenant is simply "not found".
                var customerOk = request.CustomerId is { } cid &&
                    await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == cid && c.TenantId == tenant.Value, ct) &&
                    (me.IsPlatformAdmin || await db.Customers.AnyAsync(c => c.Id == cid, ct));
                if (!customerOk) return AppErrors.NotFound("Customer");
                user = User.CreateCustomerUser(tenant.Value, request.CustomerId!.Value, email, request.FullName, hasher.Hash(request.Password), now);
            }
            else
            {
                user = User.CreateTenantUser(tenant.Value, email, request.FullName, hasher.Hash(request.Password), request.Role, now);
            }
        }

        db.Users.Add(user);
        audit.Add("user.created", "User", user.Id.ToString(), details: $"role={user.Role}", tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(user.Id, ct)).Value;
    }

    public async Task<Result<UserDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var list = await ListByIdAsync(id, ct);
        return list is null ? AppErrors.NotFound("User") : list;
    }

    private async Task<UserDto?> ListByIdAsync(Guid id, CancellationToken ct) =>
        await Scoped().Where(u => u.Id == id)
            .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.Role, u.TenantId,
                db.Tenants.Where(t => t.Id == u.TenantId).Select(t => t.Name).FirstOrDefault(),
                u.CustomerId,
                db.Customers.Where(c => c.Id == u.CustomerId).Select(c => c.Name).FirstOrDefault(),
                u.IsActive, u.LastLoginAt, u.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await Scoped().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return AppErrors.NotFound("User");
        user.Update(request.FullName, request.Role);
        audit.Add("user.updated", "User", id.ToString(), tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct)).Value;
    }

    public async Task<Result> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var user = await Scoped().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return AppErrors.NotFound("User");
        if (user.Id == me.UserId && !active)
            return Error.Conflict("CANNOT_DEACTIVATE_SELF", "You cannot deactivate your own account.");
        if (active) user.Activate(); else user.Deactivate();
        if (!active)
        {
            var now = clock.GetUtcNow();
            var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
            tokens.ForEach(t => t.Revoke(now));
        }
        audit.Add(active ? "user.activated" : "user.deactivated", "User", id.ToString(), tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        if (PasswordPolicy.Check(request.NewPassword) is { } weak) return weak;
        var user = await Scoped().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return AppErrors.NotFound("User");
        user.ChangePassword(hasher.Hash(request.NewPassword));
        user.Activate();
        var now = clock.GetUtcNow();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null).ToListAsync(ct);
        tokens.ForEach(t => t.Revoke(now));
        audit.Add("user.password_reset", "User", id.ToString(), tenantId: user.TenantId);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
