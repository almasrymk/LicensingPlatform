using Licensing.SharedKernel;

namespace Licensing.Domain.Licensing;

public enum SigningKeyStatus { Active = 1, Retiring = 2, Retired = 3 }

/// <summary>
/// Public half of a license-signing key. The private half lives in the secret store, never in the database,
/// and is separate from the keys that sign user JWTs. Clients pick the verification key by <see cref="Kid"/>.
/// </summary>
public sealed class SigningKey : Entity
{
    private SigningKey() { Kid = Algorithm = PublicKeyPem = null!; }

    public string Kid { get; private set; }
    public string Algorithm { get; private set; }
    public string PublicKeyPem { get; private set; }
    public SigningKeyStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RetiringAt { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }

    public static SigningKey Create(string kid, string algorithm, string publicKeyPem, DateTimeOffset now) => new()
    {
        Kid = kid,
        Algorithm = algorithm,
        PublicKeyPem = publicKeyPem,
        Status = SigningKeyStatus.Active,
        CreatedAt = now,
    };

    /// <summary>A retiring key no longer signs but still verifies, so tokens issued before a rotation keep working.</summary>
    public void BeginRetirement(DateTimeOffset now)
    {
        if (Status != SigningKeyStatus.Active) return;
        Status = SigningKeyStatus.Retiring;
        RetiringAt = now;
    }

    public void Retire(DateTimeOffset now)
    {
        Status = SigningKeyStatus.Retired;
        RetiredAt = now;
    }
}
