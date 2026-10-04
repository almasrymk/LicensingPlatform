using System.Security.Cryptography;
using System.Text;
using Licensing.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Licensing.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>Server-side pepper for HMAC hashing of product keys, client secrets and refresh tokens. Keep it in a secret store.</summary>
    public string SecretPepper { get; set; } = "";
}

/// <summary>PBKDF2-SHA256 with a per-password salt and 210k iterations (OWASP 2023 guidance).</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// HMAC-SHA256 keyed with a server pepper. The inputs are high-entropy random values, so a fast keyed hash is enough,
/// and a database leak alone does not allow offline guessing.
/// </summary>
public sealed class HmacSecretHasher(IOptions<SecurityOptions> options) : ISecretHasher
{
    private readonly byte[] _pepper = Encoding.UTF8.GetBytes(
        string.IsNullOrWhiteSpace(options.Value.SecretPepper)
            ? throw new InvalidOperationException("Security:SecretPepper must be configured.")
            : options.Value.SecretPepper);

    public string Hash(string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(secret)));

    public string GenerateToken(int bytes = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Product keys: 30 characters from a 32-symbol alphabet (150 bits from a CSPRNG), shown as XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX.
/// The plan's 4-character groups only give 100 bits, below the required 128, so groups are 6 characters (see ADR-003 note in docs).
/// </summary>
public sealed class ProductKeyGenerator(ISecretHasher hasher) : IProductKeyGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I
    private const int Groups = 5;
    private const int GroupSize = 6;

    public (string Key, string Hash, string Prefix) Generate()
    {
        var chars = new char[Groups * GroupSize];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var normalized = new string(chars);
        var display = string.Join('-', Enumerable.Range(0, Groups).Select(g => normalized.Substring(g * GroupSize, GroupSize)));
        return (display, Hash(normalized), normalized[..GroupSize]);
    }

    public string Normalize(string key) =>
        new(key.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public string Hash(string normalizedKey) => hasher.Hash("pk:" + normalizedKey);
}
