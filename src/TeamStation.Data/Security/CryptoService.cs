using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace TeamStation.Data.Security;

/// <summary>
/// Field-level credential crypto. A random 256-bit data-encryption key (DEK)
/// is generated on first run and stored DPAPI-wrapped in the database's
/// <c>_meta</c> table under key <c>dek_v1</c>. Every password field is
/// AES-256-GCM encrypted with that DEK using a fresh 96-bit nonce.
///
/// Wire format per field: <c>nonce(12) | tag(16) | ciphertext(n)</c>.
///
/// Portable mode wraps the DEK with a master-password-derived KEK. New wraps
/// use Argon2id (wire format tag <c>argon2id_v1</c>); legacy PBKDF2-SHA256
/// wraps (<c>pbkdf2_v1</c>) are still readable for upgrade, and are
/// re-wrapped with Argon2id opportunistically on next unlock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int MasterSaltSize = 32;
    private const int LegacyPbkdf2Iterations = 310_000;
    private const int Argon2TimeCost = 3;
    private const int Argon2MemoryKiB = 64 * 1024; // 64 MiB
    private const int Argon2Parallelism = 2;
    private const string DpapiDekKey = "dek_v1";
    private const string MasterWrappedDekKey = "dek_master_v1";
    private const string MasterSaltKey = "dek_master_salt_v1";
    private const string MasterKdfTag = "dek_master_kdf_v1";
    private const string KdfIdPbkdf2 = "pbkdf2_v1";
    private const string KdfIdArgon2id = "argon2id_v1";

    private readonly byte[] _dek;

    private CryptoService(byte[] dek)
    {
        _dek = dek;
    }

    public static CryptoService CreateOrLoad(IKeyStore keyStore)
    {
        return CreateOrLoad(keyStore, CryptoUnlockOptions.Dpapi);
    }

    public static CryptoService CreateOrLoad(IKeyStore keyStore, CryptoUnlockOptions options)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(options);

        if (options.UseMasterPassword)
            return CreateOrLoadWithMasterPassword(keyStore, options.MasterPasswordValue);

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

    public static bool HasMasterPassword(ISecretStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        return keyStore.LoadValue(MasterWrappedDekKey) is not null;
    }

    private static CryptoService CreateOrLoadWithMasterPassword(IKeyStore keyStore, string? masterPassword)
    {
        if (keyStore is not ISecretStore secretStore)
            throw new InvalidOperationException("Master-password storage requires an ISecretStore implementation.");
        if (string.IsNullOrEmpty(masterPassword))
            throw new CryptographicException("Master password is required.");

        var salt = secretStore.LoadValue(MasterSaltKey);
        var wrapped = secretStore.LoadValue(MasterWrappedDekKey);
        var kdfTag = ReadKdfTag(secretStore);

        byte[] dek;
        if (wrapped is null)
        {
            dek = TryLoadDpapiDek(keyStore) ?? RandomNumberGenerator.GetBytes(KeySize);
            salt = RandomNumberGenerator.GetBytes(MasterSaltSize);
            var protectedDek = ProtectWithMasterPassword(dek, masterPassword, salt, KdfIdArgon2id);
            secretStore.SaveValue(MasterSaltKey, salt);
            secretStore.SaveValue(MasterWrappedDekKey, protectedDek);
            WriteKdfTag(secretStore, KdfIdArgon2id);
        }
        else
        {
            if (salt is null || salt.Length != MasterSaltSize)
                throw new CryptographicException("Stored master-password salt is missing or invalid.");
            dek = UnprotectWithMasterPassword(wrapped, masterPassword, salt, kdfTag);
            if (!string.Equals(kdfTag, KdfIdArgon2id, StringComparison.Ordinal))
            {
                // Opportunistic upgrade to Argon2id — next launch is faster-to-fail and GPU-resistant.
                var newSalt = RandomNumberGenerator.GetBytes(MasterSaltSize);
                var upgraded = ProtectWithMasterPassword(dek, masterPassword, newSalt, KdfIdArgon2id);
                secretStore.SaveValue(MasterSaltKey, newSalt);
                secretStore.SaveValue(MasterWrappedDekKey, upgraded);
                WriteKdfTag(secretStore, KdfIdArgon2id);
            }
        }

        if (dek.Length != KeySize)
            throw new CryptographicException("Stored DEK has unexpected length.");

        return new CryptoService(dek);
    }

    private static string ReadKdfTag(ISecretStore secretStore)
    {
        var raw = secretStore.LoadValue(MasterKdfTag);
        if (raw is null || raw.Length == 0)
            return KdfIdPbkdf2; // Pre-Argon2 wraps have no tag stored.
        return Encoding.UTF8.GetString(raw);
    }

    private static void WriteKdfTag(ISecretStore secretStore, string tag)
    {
        secretStore.SaveValue(MasterKdfTag, Encoding.UTF8.GetBytes(tag));
    }

    private static byte[]? TryLoadDpapiDek(IKeyStore keyStore)
    {
        var wrapped = keyStore.Load();
        if (wrapped is null) return null;
        try
        {
            var dek = ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return dek.Length == KeySize ? dek : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[] ProtectWithMasterPassword(byte[] dek, string masterPassword, byte[] salt, string kdfId)
    {
        var key = DeriveMasterKey(masterPassword, salt, kdfId);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[dek.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, dek, cipher, tag);

            var output = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] UnprotectWithMasterPassword(byte[] wrapped, string masterPassword, byte[] salt, string kdfId)
    {
        if (wrapped.Length < NonceSize + TagSize)
            throw new CryptographicException("Stored master-password envelope is too short.");

        var key = DeriveMasterKey(masterPassword, salt, kdfId);
        try
        {
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipher = new byte[wrapped.Length - NonceSize - TagSize];
            Buffer.BlockCopy(wrapped, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(wrapped, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(wrapped, NonceSize + TagSize, cipher, 0, cipher.Length);

            var dek = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, dek);
            return dek;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveMasterKey(string masterPassword, byte[] salt, string kdfId) => kdfId switch
    {
        KdfIdArgon2id => DeriveArgon2id(masterPassword, salt),
        KdfIdPbkdf2 => DerivePbkdf2(masterPassword, salt),
        _ => throw new CryptographicException($"Unknown master-key KDF: {kdfId}"),
    };

    private static byte[] DerivePbkdf2(string masterPassword, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, LegacyPbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

    private static byte[] DeriveArgon2id(string masterPassword, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(masterPassword))
        {
            Salt = salt,
            Iterations = Argon2TimeCost,
            MemorySize = Argon2MemoryKiB,
            DegreeOfParallelism = Argon2Parallelism,
        };
        return argon2.GetBytes(KeySize);
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

public interface ISecretStore : IKeyStore
{
    byte[]? LoadValue(string key);
    void SaveValue(string key, byte[] value);
}

public sealed record CryptoUnlockOptions(bool UseMasterPassword, string? MasterPasswordValue)
{
    public static readonly CryptoUnlockOptions Dpapi = new(UseMasterPassword: false, MasterPasswordValue: null);

    public static CryptoUnlockOptions WithMasterPassword(string password) =>
        new(UseMasterPassword: true, MasterPasswordValue: password);
}
