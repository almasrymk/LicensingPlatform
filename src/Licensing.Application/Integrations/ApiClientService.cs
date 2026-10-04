using Licensing.Application.Abstractions;
using Licensing.Application.Common;
using Licensing.Domain.Integrations;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Integrations;

public sealed record ApiClientDto(
    Guid Id, Guid TenantId, string Name, string ClientId, IReadOnlyList<string> Scopes, ApiClientStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset SecretRotatedAt, DateTimeOffset? LastUsedAt);

/// <summary>The only response that carries the plain client secret.</summary>
public sealed record ApiClientSecretDto(ApiClientDto Client, string ClientSecret);

public sealed record CreateApiClientRequest(string Name, IReadOnlyList<string> Scopes, Guid? TenantId = null);
public sealed record UpdateApiClientRequest(string? Name, IReadOnlyList<string> Scopes);
public sealed record CreateWebhookRequest(string Url, IReadOnlyList<string> Events, Guid? TenantId = null);
public sealed record WebhookDto(Guid Id, string Url, IReadOnlyList<string> Events, bool IsActive, DateTimeOffset CreatedAt);

public sealed class ApiClientService(IAppDbContext db, ICurrentUser me, ITenantContext scope, ISecretHasher hasher, IAuditLogger audit, TimeProvider clock)
{
    private static ApiClientDto ToDto(ApiClient c) =>
        new(c.Id, c.TenantId, c.Name, c.ClientId, c.Scopes, c.Status, c.CreatedAt, c.SecretRotatedAt, c.LastUsedAt);

    public async Task<IReadOnlyList<ApiClientDto>> ListAsync(CancellationToken ct) =>
        (await db.ApiClients.OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<Result<ApiClientSecretDto>> CreateAsync(CreateApiClientRequest request, CancellationToken ct)
    {
        var tenant = me.ResolveWriteTenant(scope, request.TenantId);
        if (tenant.IsFailure) return tenant.Error!;
        var secret = "lcs_" + hasher.GenerateToken(32);
        var client = ApiClient.Create(tenant.Value, request.Name, "lc_" + hasher.GenerateToken(12), hasher.Hash(secret), request.Scopes ?? [], clock.GetUtcNow());
        db.ApiClients.Add(client);
        audit.Add("apiclient.created", "ApiClient", client.Id.ToString(), details: $"scopes={client.ScopesValue}", tenantId: client.TenantId);
        await db.SaveChangesAsync(ct);
        return new ApiClientSecretDto(ToDto(client), secret);
    }

    public async Task<Result<ApiClientDto>> UpdateAsync(Guid id, UpdateApiClientRequest request, CancellationToken ct)
    {
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return AppErrors.NotFound("API client");
        client.SetScopes(request.Scopes ?? []);
        audit.Add("apiclient.scopes_changed", "ApiClient", id.ToString(), details: client.ScopesValue, tenantId: client.TenantId);
        await db.SaveChangesAsync(ct);
        return ToDto(client);
    }

    public async Task<Result<ApiClientSecretDto>> RotateSecretAsync(Guid id, CancellationToken ct)
    {
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return AppErrors.NotFound("API client");
        var secret = "lcs_" + hasher.GenerateToken(32);
        client.RotateSecret(hasher.Hash(secret), clock.GetUtcNow());
        audit.Add("apiclient.secret_rotated", "ApiClient", id.ToString(), tenantId: client.TenantId);
        await db.SaveChangesAsync(ct);
        return new ApiClientSecretDto(ToDto(client), secret);
    }

    public Task<Result<ApiClientDto>> DisableAsync(Guid id, CancellationToken ct) => MutateAsync(id, "apiclient.disabled", c => c.Disable(), ct);
    public Task<Result<ApiClientDto>> EnableAsync(Guid id, CancellationToken ct) => MutateAsync(id, "apiclient.enabled", c => c.Enable(), ct);
    public Task<Result<ApiClientDto>> RevokeAsync(Guid id, CancellationToken ct) => MutateAsync(id, "apiclient.revoked", c => c.Revoke(), ct);

    private async Task<Result<ApiClientDto>> MutateAsync(Guid id, string action, Action<ApiClient> change, CancellationToken ct)
    {
        var client = await db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return AppErrors.NotFound("API client");
        change(client);
        audit.Add(action, "ApiClient", id.ToString(), tenantId: client.TenantId);
        await db.SaveChangesAsync(ct);
        return ToDto(client);
    }

    public async Task<IReadOnlyList<WebhookDto>> ListWebhooksAsync(CancellationToken ct) =>
        (await db.Webhooks.OrderBy(w => w.CreatedAt).ToListAsync(ct))
            .Select(w => new WebhookDto(w.Id, w.Url, w.EventsValue.Split(' ', StringSplitOptions.RemoveEmptyEntries), w.IsActive, w.CreatedAt)).ToList();

    public async Task<Result<WebhookDto>> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken ct)
    {
        var tenant = me.ResolveWriteTenant(scope, request.TenantId);
        if (tenant.IsFailure) return tenant.Error!;
        var hook = Webhook.Create(tenant.Value, request.Url, request.Events ?? [], hasher.Hash(hasher.GenerateToken()), clock.GetUtcNow());
        db.Webhooks.Add(hook);
        audit.Add("webhook.created", "Webhook", hook.Id.ToString(), tenantId: hook.TenantId);
        await db.SaveChangesAsync(ct);
        return new WebhookDto(hook.Id, hook.Url, request.Events ?? [], hook.IsActive, hook.CreatedAt);
    }
}
