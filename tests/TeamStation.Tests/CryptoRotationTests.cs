using System.Security.Cryptography;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// Coverage for <see cref="CryptoService.RotateDek"/>. Rotation is the
/// break-glass path for a user who believes their DPAPI profile was
/// compromised: decrypt every field with the old DEK and re-encrypt under
/// a fresh one, atomically.
///
/// The contract is two-sided:
/// <list type="bullet">
///   <item>Success: store holds a NEW wrapped DEK, the returned service
///   decrypts ciphertexts the migrator produced, and the OLD ciphertexts
///   fail to decrypt under the new service (tag mismatch).</item>
///   <item>Failure: if the migrator throws, the store is untouched and
///   the previously-encrypted data still decrypts under a re-opened
///   service with the original DEK.</item>
/// </list>
/// </summary>
public class CryptoRotationTests
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
    public void Rotate_replaces_wrapped_DEK_and_survives_round_trip_of_migrated_fields()
    {
        var store = new MemoryKeyStore();
        var svcA = CryptoService.CreateOrLoad(store);
        var ctOld = svcA.EncryptString("secret-A")!;
        var ctOldEmpty = svcA.EncryptString(string.Empty)!;
        var originalWrap = store.Load()!;

        var newCipherA = Array.Empty<byte>();
        var newCipherEmpty = Array.Empty<byte>();

        var rotated = CryptoService.RotateDek(store, (oldSvc, newSvc) =>
        {
            var ptA = oldSvc.DecryptString(ctOld);
            var ptEmpty = oldSvc.DecryptString(ctOldEmpty);
            newCipherA = newSvc.EncryptString(ptA)!;
            newCipherEmpty = newSvc.EncryptString(ptEmpty)!;
        });

        // Store now holds a different wrapped DEK.
        Assert.NotEqual(
            Convert.ToBase64String(originalWrap),
            Convert.ToBase64String(store.Load()!));

        // New ciphertexts decrypt cleanly under the rotated service.
        Assert.Equal("secret-A", rotated.DecryptString(newCipherA));
        Assert.Equal(string.Empty, rotated.DecryptString(newCipherEmpty));

        // Old ciphertexts must NOT decrypt under the new DEK — they're
        // cryptographically bound to the old key. Tag mismatch by design.
        Assert.ThrowsAny<CryptographicException>(() => rotated.DecryptString(ctOld));
    }

    [Fact]
    public void Rotate_rollback_keeps_store_untouched_when_migrator_throws()
    {
        var store = new MemoryKeyStore();
        var svcA = CryptoService.CreateOrLoad(store);
        var ct = svcA.EncryptString("must-survive")!;
        var originalWrap = store.Load()!;

        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.RotateDek(store, (_, _) =>
                throw new InvalidOperationException("simulated repo failure")));

        // Wrapped DEK is identical — the migrator's throw rolled us back.
        Assert.Equal(
            Convert.ToBase64String(originalWrap),
            Convert.ToBase64String(store.Load()!));

        // Original ciphertext still decrypts under a re-opened service.
        var reopen = CryptoService.CreateOrLoad(store);
        Assert.Equal("must-survive", reopen.DecryptString(ct));
    }

    [Fact]
    public void Rotate_fails_loudly_when_store_has_no_DEK_to_rotate_from()
    {
        var store = new MemoryKeyStore();
        // Intentionally skip CreateOrLoad — store is empty.

        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.RotateDek(store, (_, _) => { }));
    }

    [Fact]
    public void Rotate_refuses_to_run_against_master_password_envelope()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store, CryptoUnlockOptions.WithMasterPassword("portable"));

        // The wrapped master-password DEK lives under a different key; no
        // DPAPI-wrapped DEK is stored. Rotation should refuse cleanly
        // rather than fall back to `ProtectedData.Unprotect` which would
        // throw a confusing CryptographicException instead.
        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.RotateDek(store, (_, _) => { }));
    }

    /// <summary>
    /// KeyStore that lets the test decide when the next Save should throw.
    /// Models the realistic failure mode of a store backed by a file or
    /// database that can hit IO / lock errors independent of whether the
    /// in-memory state is consistent.
    /// </summary>
    private sealed class FlakyKeyStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public int ThrowOnSaveCallNumber { get; set; } = -1;
        public int SaveCalls { get; private set; }

        public byte[]? Load() => LoadValue("dek_v1");
        public void Save(byte[] wrapped) => SaveValue("dek_v1", wrapped);
        public byte[]? LoadValue(string key) => _values.TryGetValue(key, out var v) ? v : null;

        public void SaveValue(string key, byte[] value)
        {
            SaveCalls++;
            if (SaveCalls == ThrowOnSaveCallNumber)
                throw new IOException($"Simulated store failure on save #{SaveCalls}.");
            _values[key] = value;
        }
    }

    [Fact]
    public void Rotate_surfaces_store_failure_cleanly_without_running_the_migrator()
    {
        // First Save in rotation is the pre-migrator stage of the new wrap.
        // If THAT call fails, the migrator must never have run — so the
        // database has not been touched, and the old DEK is still the
        // authoritative one for the ciphertexts already on disk.
        var store = new FlakyKeyStore();
        var svcA = CryptoService.CreateOrLoad(store);
        var ct = svcA.EncryptString("never-touch")!;
        store.ThrowOnSaveCallNumber = store.SaveCalls + 1;

        var migratorRan = false;
        Assert.Throws<IOException>(() =>
            CryptoService.RotateDek(store, (_, _) => migratorRan = true));
        Assert.False(migratorRan, "Migrator ran despite pre-migrator save failure — rotation ordering regression.");

        var reopen = CryptoService.CreateOrLoad(store);
        Assert.Equal("never-touch", reopen.DecryptString(ct));
    }

    [Fact]
    public void Rotate_restores_old_DEK_when_migrator_throws_after_new_wrap_is_staged()
    {
        var store = new MemoryKeyStore();
        var svcA = CryptoService.CreateOrLoad(store);
        var ct = svcA.EncryptString("survive-me")!;

        var migratorInvoked = false;
        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.RotateDek(store, (_, _) =>
            {
                migratorInvoked = true;
                throw new InvalidOperationException("simulated DB rewrite failure");
            }));
        Assert.True(migratorInvoked);

        // The store was transiently set to the new wrap and then rolled
        // back to the old wrap. Reopening must decrypt the original
        // ciphertext cleanly; new DEK never persisted past the rollback.
        var reopen = CryptoService.CreateOrLoad(store);
        Assert.Equal("survive-me", reopen.DecryptString(ct));
    }

    [Fact]
    public void Rotate_twice_in_sequence_produces_two_different_DEKs()
    {
        var store = new MemoryKeyStore();
        _ = CryptoService.CreateOrLoad(store);
        var wrap0 = store.Load()!;

        _ = CryptoService.RotateDek(store, (_, _) => { });
        var wrap1 = store.Load()!;
        _ = CryptoService.RotateDek(store, (_, _) => { });
        var wrap2 = store.Load()!;

        var a = Convert.ToBase64String(wrap0);
        var b = Convert.ToBase64String(wrap1);
        var c = Convert.ToBase64String(wrap2);
        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
        Assert.NotEqual(a, c);
    }
}
