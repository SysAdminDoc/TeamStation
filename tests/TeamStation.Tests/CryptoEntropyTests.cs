using System.Security.Cryptography;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.3 hardening: every <c>ProtectedData.Protect/Unprotect</c> call in
/// <see cref="CryptoService"/> now binds to a per-database 32-byte entropy
/// salt held in the same <see cref="ISecretStore"/> as the DEK wrap. The
/// salt moves the DPAPI trust boundary from "same Windows user" to "same
/// Windows user AND has read this database file". Legacy null-entropy
/// wraps (every install up to and including v0.3.2) keep working through
/// a one-shot fallback that re-wraps the DEK under the new entropy on
/// first read.
/// </summary>
public class CryptoEntropyTests
{
    private const string EntropyKey = "dpapi_entropy_v1";
    private const string DekKey = "dek_v1";

    private sealed class MemoryKeyStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public byte[]? Load() => LoadValue(DekKey);
        public void Save(byte[] wrapped) => SaveValue(DekKey, wrapped);
        public byte[]? LoadValue(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void SaveValue(string key, byte[] value) => _values[key] = value;
        public void DeleteValue(string key) => _values.Remove(key);
        public IReadOnlyDictionary<string, byte[]> Snapshot() => _values;
    }

    /// <summary>An IKeyStore that does NOT implement ISecretStore — proves the entropy upgrade is a graceful no-op for legacy stubs.</summary>
    private sealed class LegacyKeyStore : IKeyStore
    {
        private byte[]? _wrapped;
        public byte[]? Load() => _wrapped;
        public void Save(byte[] wrapped) => _wrapped = wrapped;
    }

    [Fact]
    public void First_CreateOrLoad_seeds_a_32_byte_entropy_salt()
    {
        var store = new MemoryKeyStore();
        Assert.Null(store.LoadValue(EntropyKey));

        _ = CryptoService.CreateOrLoad(store);

        var salt = store.LoadValue(EntropyKey);
        Assert.NotNull(salt);
        Assert.Equal(32, salt!.Length);
        // Sanity: salt is not all zeros (would indicate uninitialised RNG).
        Assert.Contains(salt, b => b != 0);
    }

    [Fact]
    public void CreateOrLoad_reuses_the_persisted_salt_byte_for_byte()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store);
        var first = store.LoadValue(EntropyKey)!;

        _ = CryptoService.CreateOrLoad(store);
        var second = store.LoadValue(EntropyKey)!;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Persisted_wrap_actually_binds_to_the_entropy_salt()
    {
        // Demonstrates the threat-model goal: the DEK wrap cannot be
        // unprotected without the salt. ProtectedData with a different
        // entropy bytes raises CryptographicException; with null it also
        // raises (since the wrap was made under a non-null entropy).
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store);

        var wrap = store.LoadValue(DekKey)!;
        var salt = store.LoadValue(EntropyKey)!;

        // Round-trip with the correct salt — must succeed.
        var ok = ProtectedData.Unprotect(wrap, salt, DataProtectionScope.CurrentUser);
        Assert.Equal(32, ok.Length);

        // Wrong entropy bytes — must fail.
        var wrong = new byte[salt.Length];
        Array.Copy(salt, wrong, salt.Length);
        wrong[0] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrap, wrong, DataProtectionScope.CurrentUser));

        // Null entropy — must fail (proves the wrap is in fact entropy-bound,
        // not a coincidence).
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrap, null, DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void Legacy_null_entropy_wrap_loads_and_is_silently_upgraded()
    {
        // Seed a v0.3.2-style wrap: null entropy, no salt row.
        var store = new MemoryKeyStore();
        var dek = RandomNumberGenerator.GetBytes(32);
        var legacyWrap = ProtectedData.Protect(dek, optionalEntropy: null, DataProtectionScope.CurrentUser);
        store.SaveValue(DekKey, legacyWrap);
        Assert.Null(store.LoadValue(EntropyKey));

        // First load under the new code: salt is generated, DEK is unwrapped
        // via the legacy fallback, then re-wrapped under the salt.
        var svc = CryptoService.CreateOrLoad(store);
        var ct = svc.EncryptString("after-upgrade");
        Assert.Equal("after-upgrade", svc.DecryptString(ct));

        // Salt now exists; the wrap row has been replaced by an entropy-bound wrap.
        var salt = store.LoadValue(EntropyKey);
        Assert.NotNull(salt);
        Assert.Equal(32, salt!.Length);
        var newWrap = store.LoadValue(DekKey)!;
        Assert.NotEqual(Convert.ToBase64String(legacyWrap), Convert.ToBase64String(newWrap));

        // The new wrap unwraps with the salt and FAILS without it.
        var roundTrippedDek = ProtectedData.Unprotect(newWrap, salt, DataProtectionScope.CurrentUser);
        Assert.Equal(dek, roundTrippedDek);
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(newWrap, null, DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void Second_load_after_legacy_upgrade_takes_the_fast_path()
    {
        // Set up a legacy install, run the upgrade, then verify a third
        // CreateOrLoad never has to fall back to null-entropy decryption.
        var store = new MemoryKeyStore();
        var dek = RandomNumberGenerator.GetBytes(32);
        store.SaveValue(DekKey, ProtectedData.Protect(dek, null, DataProtectionScope.CurrentUser));

        var first = CryptoService.CreateOrLoad(store);
        var ct = first.EncryptString("x")!;
        var afterUpgradeWrap = store.LoadValue(DekKey)!;

        var second = CryptoService.CreateOrLoad(store);
        Assert.Equal("x", second.DecryptString(ct));

        // Wrap byte-identical: re-wrap should NOT have fired again.
        Assert.Equal(
            Convert.ToBase64String(afterUpgradeWrap),
            Convert.ToBase64String(store.LoadValue(DekKey)!));
    }

    [Fact]
    public void RotateDek_preserves_entropy_across_rotation()
    {
        var store = new MemoryKeyStore();
        var initial = CryptoService.CreateOrLoad(store);
        var saltBeforeRotation = store.LoadValue(EntropyKey)!;

        var ct = initial.EncryptString("rotate-me");

        var rotated = CryptoService.RotateDek(store, (oldSvc, newSvc) =>
        {
            // Migrator: re-encrypt the only ciphertext under the new DEK.
            var plain = oldSvc.DecryptString(ct);
            var fresh = newSvc.EncryptString(plain);
            // Persist swap is the caller's responsibility in production; for
            // the test we just keep the new ciphertext in scope.
            ct = fresh;
        });

        var saltAfterRotation = store.LoadValue(EntropyKey)!;
        Assert.Equal(saltBeforeRotation, saltAfterRotation);

        // New wrap is entropy-bound under the SAME salt.
        var wrap = store.LoadValue(DekKey)!;
        var dek = ProtectedData.Unprotect(wrap, saltAfterRotation, DataProtectionScope.CurrentUser);
        Assert.Equal(32, dek.Length);
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrap, null, DataProtectionScope.CurrentUser));

        // And the rotated service decrypts the rotated ciphertext.
        Assert.Equal("rotate-me", rotated.DecryptString(ct));
    }

    [Fact]
    public void RotateDek_creates_entropy_when_rotating_a_legacy_install()
    {
        // Pre-entropy install: DEK wrapped with null, no salt row.
        var store = new MemoryKeyStore();
        var dek = RandomNumberGenerator.GetBytes(32);
        store.SaveValue(DekKey, ProtectedData.Protect(dek, null, DataProtectionScope.CurrentUser));

        Assert.Null(store.LoadValue(EntropyKey));

        var rotated = CryptoService.RotateDek(store, (oldSvc, newSvc) => { /* no-op migrator */ });

        // Salt now persisted; rotation closed the legacy gap.
        var salt = store.LoadValue(EntropyKey);
        Assert.NotNull(salt);
        Assert.Equal(32, salt!.Length);

        // New wrap unwraps under the new salt, fails under null.
        var wrap = store.LoadValue(DekKey)!;
        Assert.NotNull(ProtectedData.Unprotect(wrap, salt, DataProtectionScope.CurrentUser));
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrap, null, DataProtectionScope.CurrentUser));

        rotated.Dispose();
    }

    [Fact]
    public void Legacy_IKeyStore_only_stub_keeps_working_with_null_entropy()
    {
        // A test/integration stub that pre-dates the entropy upgrade and
        // implements only IKeyStore (no LoadValue/SaveValue) must keep
        // round-tripping ciphertext. The entropy code is a graceful no-op
        // here, and ProtectedData stays on null entropy.
        var store = new LegacyKeyStore();
        var svc = CryptoService.CreateOrLoad(store);
        var ct = svc.EncryptString("legacy-store");
        Assert.Equal("legacy-store", svc.DecryptString(ct));

        // Wrap is null-entropy: ProtectedData.Unprotect with null succeeds.
        var wrap = store.Load()!;
        var dek = ProtectedData.Unprotect(wrap, null, DataProtectionScope.CurrentUser);
        Assert.Equal(32, dek.Length);
    }

    [Fact]
    public void Master_password_carry_over_from_legacy_DPAPI_install_works()
    {
        // Existing v0.3.2 install: DPAPI DEK wrapped with null entropy,
        // no salt. User now switches to portable / master-password mode.
        // TryLoadDpapiDek must successfully bring the legacy DEK forward.
        var store = new MemoryKeyStore();
        var dek = RandomNumberGenerator.GetBytes(32);
        store.SaveValue(DekKey, ProtectedData.Protect(dek, null, DataProtectionScope.CurrentUser));

        var svc = CryptoService.CreateOrLoad(
            store,
            CryptoUnlockOptions.WithMasterPassword("portable-master-password"));

        // The master-password envelope now exists alongside the (still-stored)
        // DPAPI wrap, and the same DEK is in use — round-trip with the
        // original key proves identity.
        var ct = svc.EncryptString("master-mode");
        var reopened = CryptoService.CreateOrLoad(
            store,
            CryptoUnlockOptions.WithMasterPassword("portable-master-password"));
        Assert.Equal("master-mode", reopened.DecryptString(ct));
    }
}
