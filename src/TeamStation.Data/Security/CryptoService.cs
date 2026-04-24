using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace TeamStation.Data.Security;

/// <summary>
/// Field-level credential crypto. A random 256-bit data-encryption key (DEK)
/// is generated on first run and stored DPAPI-wrapped in the database's
/// <c>_meta</c> table under key <c>dek_v1</c>. Every password field is
/// AES-256-GCM encrypted with that DEK using a fresh 96-bit nonce.
///
/// Wire format per field: <c>nonce(12) | tag(16) | ciphertext(n)</c>.
/// </summary>
/// <remarks>
/// DPAPI binding is per-user, per-machine by default. That means a portable
/// database copied to a different Windows account will not decrypt — this is
/// intentional for the default mode. Portable-mode master-password KEK
/// wrapping lands in a later release (see ROADMAP).
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _dek;

    private CryptoService(byte[] dek)
    {
        _dek = dek;
    }

    public static CryptoService CreateOrLoad(IKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);

        var wrapped = keyStore.Load();
        byte[] dek;
        if (wrapped is null)
        {
            dek = RandomNumberGenerator.GetBytes(KeySize);
            var protectedDek = ProtectedData.Protect(dek, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            keyStore.Save(protectedDek);
        }
        else
        {
            dek = ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            if (dek.Length != KeySize)
                throw new CryptographicException("Stored DEK has unexpected length.");
        }

        return new CryptoService(dek);
    }

    public byte[]? EncryptString(string? plaintext)
    {
        if (plaintext is null) return null;
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_dek, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return output;
    }

    public string? DecryptString(byte[]? ciphertext)
    {
        if (ciphertext is null) return null;
        if (ciphertext.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[ciphertext.Length - NonceSize - TagSize];
        Buffer.BlockCopy(ciphertext, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(ciphertext, NonceSize + TagSize, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_dek, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public interface IKeyStore
{
    byte[]? Load();
    void Save(byte[] wrapped);
}
