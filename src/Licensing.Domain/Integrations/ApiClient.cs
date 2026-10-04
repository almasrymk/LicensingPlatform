using Licensing.SharedKernel;

namespace Licensing.Domain.Integrations;

public enum ApiClientStatus { Active = 1, Disabled = 2, Revoked = 3 }

public static class ApiScopes
{
    public const string LicensesActivate = "licenses.activate";
    public const string LicensesValidate = "licenses.validate";
    public const string LicensesIssue = "licenses.issue";
    public const string LicensesRead = "licenses.read";

    public static readonly string[] All = [LicensesActivate, LicensesValidate, LicensesIssue, LicensesRead];
}

/// <summary>A machine client of a tenant. Its secret is shown once at creation or rotation and stored hashed.</summary>
public sealed class ApiClient : AggregateRoot, ITenantOwned
{
    private ApiClient() { Name = ClientId = SecretHash = ScopesValue = null!; }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string ClientId { get; private set; }
    public string SecretHash { get; private set; }
    public string ScopesValue { get; private set; }
    public ApiClientStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset SecretRotatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    public IReadOnlyList<string> Scopes => ScopesValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static ApiClient Create(Guid tenantId, string name, string clientId, string secretHash, IEnumerable<string> scopes, DateTimeOffset now)
    {
        var client = new ApiClient
        {
            TenantId = tenantId,
            Name = Guard.NotEmpty(name, "Name"),
            ClientId = clientId,
            SecretHash = secretHash,
            Status = ApiClientStatus.Active,
            CreatedAt = now,
            SecretRotatedAt = now,
        };
        client.SetScopes(scopes);
        return client;
    }

    public void SetScopes(IEnumerable<string> scopes)
    {
        var list = scopes.Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        var unknown = list.Except(ApiScopes.All).ToList();
        if (unknown.Count > 0)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", $"Unknown scopes: {string.Join(", ", unknown)}."));
        if (list.Count == 0)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "At least one scope is required."));
        ScopesValue = string.Join(' ', list.Order());
    }

    public void RotateSecret(string secretHash, DateTimeOffset now)
    {
        if (Status == ApiClientStatus.Revoked)
            throw new DomainException(Error.Conflict("APICLIENT_REVOKED", "A revoked client cannot be rotated."));
        SecretHash = secretHash;
        SecretRotatedAt = now;
    }

    public void Disable()
    {
        if (Status != ApiClientStatus.Active) throw new DomainException(Error.Conflict("APICLIENT_INVALID_TRANSITION", "Client is not active."));
        Status = ApiClientStatus.Disabled;
    }

    public void Enable()
    {
        if (Status != ApiClientStatus.Disabled) throw new DomainException(Error.Conflict("APICLIENT_INVALID_TRANSITION", "Client is not disabled."));
        Status = ApiClientStatus.Active;
    }

    public void Revoke() => Status = ApiClientStatus.Revoked;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}

/// <summary>Foundation only (Phase 4): endpoints are registered but nothing is delivered until Phase 8.</summary>
public sealed class Webhook : Entity, ITenantOwned
{
    private Webhook() { Url = EventsValue = SecretHash = null!; }

    public Guid TenantId { get; private set; }
    public string Url { get; private set; }
    public string EventsValue { get; private set; }
    public string SecretHash { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Webhook Create(Guid tenantId, string url, IEnumerable<string> events, string secretHash, DateTimeOffset now)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainException(Error.Validation("VALIDATION_FAILED", "Webhook URL must be an absolute https URL."));
        return new Webhook
        {
            TenantId = tenantId,
            Url = uri.ToString(),
            EventsValue = string.Join(' ', events),
            SecretHash = secretHash,
            IsActive = true,
            CreatedAt = now,
        };
    }
}
