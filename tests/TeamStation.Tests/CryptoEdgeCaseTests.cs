using System.Globalization;
using System.Security.Cryptography;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// Edge-case coverage beyond the happy path in <see cref="CryptoServiceTests"/>.
/// Exercises the boundary conditions that historically break AES-GCM
/// implementations: empty plaintext, embedded null bytes, very large inputs,
/// surrogate pairs, invariant culture handling, per-byte tamper detection,
/// and cross-instance stability (two services unwrapping the same DEK agree
/// on every ciphertext both directions).
/// </summary>
public class CryptoEdgeCaseTests
{
    private sealed class MemoryKeyStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Load() => LoadValue("dek_v1");
        public void Save(byte[] wrapped) => SaveValue("dek_v1", wrapped);
        public byte[]? LoadValue(string key) => _values.TryGetValue(key, out var v) ? v : null;
        public void SaveValue(string key, byte[] value) => _values[key] = value;
    }

    [Fact]
    public void Empty_string_round_trips_and_still_produces_nonce_plus_tag()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var ct = svc.EncryptString(string.Empty);
        Assert.NotNull(ct);
        Assert.Equal(12 + 16, ct!.Length);
        Assert.Equal(string.Empty, svc.DecryptString(ct));
    }

    [Fact]
    public void Null_byte_embedded_strings_survive_round_trip()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var inputs = new[]
        {
            "\0",
            "prefix\0suffix",
            "\0\0\0",
            "no-null-but-has-null-char-\0-at-tail\0",
        };
        foreach (var pt in inputs)
        {
            var ct = svc.EncryptString(pt);
            Assert.Equal(pt, svc.DecryptString(ct));
        }
    }

    [Fact]
    public void Half_megabyte_plaintext_round_trips_in_single_call()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var pt = new string('x', 512 * 1024);
        var ct = svc.EncryptString(pt);
        Assert.Equal(pt.Length, svc.DecryptString(ct)!.Length);
    }

    [Fact]
    public void Surrogate_pair_input_round_trips_without_mangling()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        // Grinning Face With Tears of Joy is a surrogate pair; combined
        // with a skin-tone modifier the codepoint sequence is non-trivial.
        var pt = "\uD83D\uDE02\uD83D\uDC68\uD83C\uDFFD"; // 😂👨🏽
        var ct = svc.EncryptString(pt);
        Assert.Equal(pt, svc.DecryptString(ct));
    }

    [Fact]
    public void Invariant_culture_does_not_affect_round_trip()
    {
        // Some locale-sensitive code paths (casing, number formatting) have
        // tripped past implementations of AES-GCM when the thread culture
        // changed mid-test. Crypto should be locale-oblivious.
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // dotless-i trap
            var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
            const string pt = "Istanbul İzmir İSTANBUL";
            var ct = svc.EncryptString(pt);
            Assert.Equal(pt, svc.DecryptString(ct));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void Tamper_each_ciphertext_byte_still_trips_tag_verification()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var ct = svc.EncryptString("some-secret-credential")!;

        // Flip one bit in every byte, one at a time, starting from the tag
        // region on through the ciphertext tail. Each mutation must fail.
        for (var i = 12; i < ct.Length; i++)
        {
            var clone = (byte[])ct.Clone();
            clone[i] ^= 0x80;
            Assert.ThrowsAny<CryptographicException>(() => svc.DecryptString(clone));
        }
    }

    [Fact]
    public void Two_services_sharing_the_same_store_agree_on_ciphertext()
    {
        var store = new MemoryKeyStore();
        var a = CryptoService.CreateOrLoad(store);
        var b = CryptoService.CreateOrLoad(store);

        const string pt = "cross-instance";
        var ctA = a.EncryptString(pt);
        var ctB = b.EncryptString(pt);

        // Ciphertexts themselves differ (fresh nonce per call) but both
        // must decrypt to the same plaintext under either instance.
        Assert.Equal(pt, a.DecryptString(ctB));
        Assert.Equal(pt, b.DecryptString(ctA));
    }

    [Fact]
    public void Ciphertext_below_minimum_envelope_size_raises_not_silently_returns()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        // 12 nonce + 16 tag = 28 bytes minimum; anything under must throw.
        for (var len = 0; len < 28; len++)
        {
            var tooShort = new byte[len];
            Assert.ThrowsAny<CryptographicException>(() => svc.DecryptString(tooShort));
        }
    }

    [Fact]
    public void Nonce_is_never_reused_for_the_same_plaintext()
    {
        var svc = CryptoService.CreateOrLoad(new MemoryKeyStore());
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 200; i++)
        {
            var ct = svc.EncryptString("fixed-plaintext")!;
            var nonce = Convert.ToBase64String(ct, 0, 12);
            Assert.True(nonces.Add(nonce), $"Nonce collision detected on iteration {i}. AES-GCM nonce reuse catastrophically breaks confidentiality.");
        }
    }

    [Fact]
    public void Master_password_mode_rejects_envelope_with_corrupt_salt()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("valid-master"));

        // Flip a bit in the stored salt and re-open; the derived key will
        // not match, so the wrap must refuse to unlock.
        var salt = store.LoadValue("dek_master_salt_v1")!;
        salt[0] ^= 0x55;
        store.SaveValue("dek_master_salt_v1", salt);

        Assert.ThrowsAny<CryptographicException>(() =>
            CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("valid-master")));
    }

    [Fact]
    public void Master_password_mode_rejects_corrupted_wrap_envelope()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("valid-master"));

        var wrapped = store.LoadValue("dek_master_v1")!;
        wrapped[wrapped.Length - 1] ^= 0x42;
        store.SaveValue("dek_master_v1", wrapped);

        Assert.ThrowsAny<CryptographicException>(() =>
            CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("valid-master")));
    }
}
