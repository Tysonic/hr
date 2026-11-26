using System.Security.Cryptography;
using System.Text;

namespace TyHr.Infrastructure.Security;

public class SecretsOptions
{
    /// <summary>
    /// Base64 of 32 random bytes (openssl rand -base64 32). Encrypts [Encrypted] fields: losing it loses those values,
    /// so keep it with the database backups' secrets. Set as Secrets__Key in production.
    /// </summary>
    public string? Key { get; set; }
}

/// <summary>
/// Encrypts values before they are stored, with AES-GCM and a 32-byte key from config (ported from Tycount's
/// SecretProtector), so the database alone does not give them away. Stored form: "v1:" + base64(nonce, ciphertext,
/// tag). Values stored before encryption have no prefix and come back as they are.
/// </summary>
public sealed class SecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecretProtector(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException(
                "Secrets:Key is not set, so ID and account numbers can't be encrypted. Set it to 32 random bytes, base64-encoded " +
                "(openssl rand -base64 32): appsettings.Development.json has a development key; production sets Secrets__Key.");
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) throw new InvalidOperationException("Secrets:Key must be 32 random bytes, base64-encoded.");
    }

    public string Protect(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var sealedValue = new byte[NonceSize + plain.Length + TagSize];
        var nonce = sealedValue.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, sealedValue.AsSpan(NonceSize, plain.Length), sealedValue.AsSpan(NonceSize + plain.Length));
        return Prefix + Convert.ToBase64String(sealedValue);
    }

    public string Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        var sealedValue = Convert.FromBase64String(stored[Prefix.Length..]);
        var plain = new byte[sealedValue.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(sealedValue.AsSpan(0, NonceSize), sealedValue.AsSpan(NonceSize, plain.Length),
            sealedValue.AsSpan(NonceSize + plain.Length), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
