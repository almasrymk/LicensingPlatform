using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Licensing.Application.Abstractions;
using Licensing.Domain.Licensing;
using Licensing.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Licensing.Infrastructure.Security;

public sealed class LicenseSigningOptions
{
    public const string Section = "LicenseSigning";

    public string Issuer { get; set; } = "licensing-platform";
    /// <summary>Folder of the file-based secret store holding the private keys (encrypted with Data Protection).</summary>
    public string KeyStorePath { get; set; } = "App_Data/signing-keys";
}

/// <summary>Where license-signing private keys live. The file store is for development; production swaps in a vault.</summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken ct);
    Task SetAsync(string name, string value, CancellationToken ct);
}

public sealed class FileSecretStore(IOptions<LicenseSigningOptions> options, IDataProtectionProvider dataProtection) : ISecretStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Licensing.SigningKeys.v1");
    // Relative paths are resolved against the app folder, not the process working directory (which IIS may set elsewhere).
    private readonly string _root = Path.IsPathRooted(options.Value.KeyStorePath)
        ? options.Value.KeyStorePath
        : Path.Combine(AppContext.BaseDirectory, options.Value.KeyStorePath);

    public async Task<string?> GetAsync(string name, CancellationToken ct)
    {
        var path = PathFor(name);
        return File.Exists(path) ? _protector.Unprotect(await File.ReadAllTextAsync(path, ct)) : null;
    }

    public async Task SetAsync(string name, string value, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PathFor(name), _protector.Protect(value), ct);
    }

    private string PathFor(string name) =>
        Path.Combine(_root, string.Concat(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) + ".key");
}

/// <summary>
/// Signs offline license tokens with ECDSA P-256 (ES256, ADR-004). Each key has a kid; rotation moves the current key to
/// "retiring" (still published for verification) so tokens signed before the rotation stay valid.
/// </summary>
public sealed class EcdsaLicenseTokenSigner(AppDbContext db, ISecretStore secrets, IOptions<LicenseSigningOptions> options, TimeProvider clock,
    ILogger<EcdsaLicenseTokenSigner> logger)
    : ILicenseTokenSigner
{
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256;
    private static readonly SemaphoreSlim RotationLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, ECDsa> PrivateKeys = new();

    public async Task<SignedLicenseToken> SignAsync(LicenseTokenClaims c, CancellationToken ct)
    {
        var kid = await db.SigningKeys.Where(k => k.Status == SigningKeyStatus.Active)
            .OrderByDescending(k => k.CreatedAt).Select(k => k.Kid).FirstOrDefaultAsync(ct)
            ?? await RotateAsync(ct);

        var ecdsa = await TryLoadPrivateKeyAsync(kid, ct);
        if (ecdsa is null)
        {
            // The database names a key whose private half this server cannot read: the database was restored on another
            // machine, the key files were not deployed, or the Data Protection key ring changed. Start a new key so
            // signing keeps working; the old public key stays published (retiring) for tokens already issued.
            logger.LogWarning("Private key for signing key {Kid} is missing or unreadable on this server; rotating to a new key", kid);
            kid = await RotateAsync(ct);
            ecdsa = PrivateKeys[kid];
        }
        var now = clock.GetUtcNow();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, c.DeviceId),
            new("lic", c.LicenseNumber),
            new("lid", c.LicenseId.ToString()),
            new("tid", c.TenantId.ToString()),
            new("product", c.ProductCode),
            new("plan", c.PlanCode),
            new("features", JsonSerializer.Serialize(c.Features), JsonClaimValueTypes.JsonArray),
            new("check_after", c.CheckAfter.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (c.LicenseExpiresAt is { } exp)
            claims.Add(new Claim("lic_exp", exp.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

        var key = new ECDsaSecurityKey(ecdsa) { KeyId = kid };
        var token = new JwtSecurityToken(options.Value.Issuer, c.ProductCode, claims, now.UtcDateTime, c.OfflineValidUntil.UtcDateTime,
            new SigningCredentials(key, Algorithm));
        return new SignedLicenseToken(new JwtSecurityTokenHandler().WriteToken(token), kid, c.OfflineValidUntil);
    }

    public async Task<IReadOnlyList<PublicSigningKey>> GetPublicKeysAsync(CancellationToken ct)
    {
        var keys = await db.SigningKeys.Where(k => k.Status != SigningKeyStatus.Retired)
            .OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        return keys.Select(k =>
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(k.PublicKeyPem);
            var p = ec.ExportParameters(false);
            return new PublicSigningKey(k.Kid, "ES256", k.PublicKeyPem, k.Status.ToString().ToLowerInvariant(),
                Base64UrlEncoder.Encode(p.Q.X!), Base64UrlEncoder.Encode(p.Q.Y!), "P-256");
        }).ToList();
    }

    public async Task<string> RotateAsync(CancellationToken ct)
    {
        await RotationLock.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            var kid = $"lk-{now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
            var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            await secrets.SetAsync(kid, ecdsa.ExportPkcs8PrivateKeyPem(), ct);
            PrivateKeys[kid] = ecdsa;

            foreach (var active in await db.SigningKeys.Where(k => k.Status == SigningKeyStatus.Active).ToListAsync(ct))
                active.BeginRetirement(now);
            db.SigningKeys.Add(SigningKey.Create(kid, "ES256", ecdsa.ExportSubjectPublicKeyInfoPem(), now));
            await db.SaveChangesAsync(ct);
            return kid;
        }
        finally
        {
            RotationLock.Release();
        }
    }

    private async Task<ECDsa?> TryLoadPrivateKeyAsync(string kid, CancellationToken ct)
    {
        if (PrivateKeys.TryGetValue(kid, out var cached)) return cached;
        string? pem;
        try
        {
            pem = await secrets.GetAsync(kid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Encrypted with a Data Protection key this server does not have, or the file cannot be read.
            logger.LogWarning(ex, "Could not read the private key for signing key {Kid}", kid);
            return null;
        }
        if (pem is null) return null;
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return PrivateKeys.GetOrAdd(kid, ecdsa);
    }
}
