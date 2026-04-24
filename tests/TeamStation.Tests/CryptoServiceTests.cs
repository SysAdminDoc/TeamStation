using System.Security.Cryptography;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

public class CryptoServiceTests
{
    // An in-memory IKeyStore so tests don't touch SQLite or DPAPI beyond
    // what CryptoService itself invokes. The stored blob IS the real DPAPI
    // output — we just don't persist it anywhere.
    private sealed class MemoryKeyStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Load() => LoadValue("dek_v1");
        public void Save(byte[] wrapped) => SaveValue("dek_v1", wrapped);
        public byte[]? LoadValue(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void SaveValue(string key, byte[] value) => _values[key] = value;
    }

    [Fact]
    public void CreateOrLoad_generates_and_persists_a_DEK_on_first_call()
    {
        var store = new MemoryKeyStore();
        Assert.Null(store.Load());
        var svc = CryptoService.CreateOrLoad(store);
        Assert.NotNull(store.Load());
        Assert.NotNull(svc);
    }

    [Fact]
    public void CreateOrLoad_reuses_the_existing_DEK_on_subsequent_calls()
    {
        var store = new MemoryKeyStore();
        var a = CryptoService.CreateOrLoad(store);
        var b = CryptoService.CreateOrLoad(store);

        const string plaintext = "hello world";
        var ct = a.EncryptString(plaintext);
        Assert.NotNull(ct);
        Assert.Equal(plaintext, b.DecryptString(ct));
    }

    [Fact]
    public void Encrypt_returns_null_for_null_input_and_decrypts_null_roundtrip()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        Assert.Null(svc.EncryptString(null));
        Assert.Null(svc.DecryptString(null));
    }

    [Fact]
    public void Encrypt_produces_different_ciphertext_each_call_for_same_plaintext()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var a = svc.EncryptString("password");
        var b = svc.EncryptString("password");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(Convert.ToBase64String(a!), Convert.ToBase64String(b!));
    }

    [Fact]
    public void Decrypt_throws_on_tampered_ciphertext()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var ct = svc.EncryptString("secret")!;
        // Flip a bit in the ciphertext tail.
        ct[^1] ^= 0x01;
        // AesGcm throws AuthenticationTagMismatchException (subclass of
        // CryptographicException) on tag-verify failure; accept either.
        Assert.ThrowsAny<CryptographicException>(() => svc.DecryptString(ct));
    }

    [Fact]
    public void Decrypt_throws_on_truncated_input()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        Assert.ThrowsAny<CryptographicException>(() => svc.DecryptString(new byte[5]));
    }

    [Fact]
    public void Roundtrip_handles_unicode_and_long_passwords()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var inputs = new[]
        {
            "simple",
            "P@ssw0rd!#$%^&*()",
            "Я шифровал это с кириллицей 🎉",
            new string('x', 512),
        };
        foreach (var pt in inputs)
        {
            var ct = svc.EncryptString(pt);
            Assert.Equal(pt, svc.DecryptString(ct));
        }
    }

    [Fact]
    public void Master_password_mode_reuses_the_same_DEK()
    {
        var store = new MemoryKeyStore();
        var a = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("correct horse battery staple"));
        Assert.True(CryptoService.HasMasterPassword(store));

        var b = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("correct horse battery staple"));
        var ct = a.EncryptString("portable secret");

        Assert.Equal("portable secret", b.DecryptString(ct));
    }

    [Fact]
    public void Master_password_mode_rejects_the_wrong_password()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("right-password"));

        Assert.ThrowsAny<CryptographicException>(() =>
            CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("wrong-password")));
    }
}
