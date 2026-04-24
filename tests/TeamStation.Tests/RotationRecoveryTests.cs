using System.Security.Cryptography;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// Coverage for the two-phase-commit rotation introduced in v0.3.1.
/// Complements <see cref="CryptoRotationTests"/> which focuses on the
/// migrator contract; this class focuses on the <c>dek_v1_pending</c>
/// tombstone + <see cref="RotationState"/> / <see cref="CryptoService.ReconcilePendingRotation"/>
/// recovery surface.
/// </summary>
public class RotationRecoveryTests
{
    private const string PendingKey = "dek_v1_pending";
    private const string MainKey = "dek_v1";

    private sealed class Store : ISecretStore
    {
        public Dictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);
        public byte[]? Load() => LoadValue(MainKey);
        public void Save(byte[] wrapped) => SaveValue(MainKey, wrapped);
        public byte[]? LoadValue(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void SaveValue(string key, byte[] value) => Values[key] = value;
        public void DeleteValue(string key) => Values.Remove(key);
    }

    [Fact]
    public void InspectPendingRotation_returns_None_on_clean_store()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        Assert.Equal(RotationState.None, CryptoService.InspectPendingRotation(store));
    }

    [Fact]
    public void InspectPendingRotation_returns_PendingOrphan_when_pending_equals_main()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        store.SaveValue(PendingKey, main);

        Assert.Equal(RotationState.PendingOrphan, CryptoService.InspectPendingRotation(store));
    }

    [Fact]
    public void InspectPendingRotation_returns_Interrupted_when_pending_differs_from_main()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        var pending = new byte[main.Length];
        RandomNumberGenerator.Fill(pending);

        store.SaveValue(PendingKey, pending);

        Assert.Equal(RotationState.InterruptedMidRotation, CryptoService.InspectPendingRotation(store));
    }

    [Fact]
    public void Reconcile_deletes_pending_orphan_silently()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        store.SaveValue(PendingKey, main);

        CryptoService.ReconcilePendingRotation(store);

        Assert.Null(store.LoadValue(PendingKey));
        Assert.NotNull(store.LoadValue(MainKey));
    }

    [Fact]
    public void Reconcile_refuses_to_auto_recover_interrupted_state()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        var pending = new byte[main.Length];
        RandomNumberGenerator.Fill(pending);
        store.SaveValue(PendingKey, pending);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CryptoService.ReconcilePendingRotation(store));
        Assert.Contains("interrupted", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The tombstone MUST still be in place so the caller can decide to
        // ForceCommit or ForceRollback. Auto-clearing it would lose the
        // only signal we have that recovery is needed.
        Assert.NotNull(store.LoadValue(PendingKey));
        Assert.Equal(
            Convert.ToBase64String(pending),
            Convert.ToBase64String(store.LoadValue(PendingKey)!));
    }

    [Fact]
    public void CreateOrLoad_auto_reconciles_pending_orphan_and_succeeds()
    {
        var store = new Store();
        var svc0 = CryptoService.CreateOrLoad(store);
        var ct = svc0.EncryptString("still-decryptable")!;
        var main = store.LoadValue(MainKey)!;
        store.SaveValue(PendingKey, main); // simulate post-commit crash: pending == main

        var svc1 = CryptoService.CreateOrLoad(store);

        Assert.Null(store.LoadValue(PendingKey));
        Assert.Equal("still-decryptable", svc1.DecryptString(ct));
        svc0.Dispose();
        svc1.Dispose();
    }

    [Fact]
    public void CreateOrLoad_refuses_to_open_when_rotation_was_interrupted()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        var pending = new byte[main.Length];
        RandomNumberGenerator.Fill(pending);
        store.SaveValue(PendingKey, pending);

        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.CreateOrLoad(store));
    }

    [Fact]
    public void ForceCommit_promotes_pending_to_main_and_clears_tombstone()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        var pending = new byte[main.Length];
        RandomNumberGenerator.Fill(pending);
        store.SaveValue(PendingKey, pending);

        CryptoService.ForceCommitPendingRotation(store);

        Assert.Null(store.LoadValue(PendingKey));
        Assert.Equal(
            Convert.ToBase64String(pending),
            Convert.ToBase64String(store.LoadValue(MainKey)!));
    }

    [Fact]
    public void ForceRollback_drops_pending_keeps_main()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        var main = store.LoadValue(MainKey)!;
        var pending = new byte[main.Length];
        RandomNumberGenerator.Fill(pending);
        store.SaveValue(PendingKey, pending);

        CryptoService.ForceRollbackPendingRotation(store);

        Assert.Null(store.LoadValue(PendingKey));
        Assert.Equal(
            Convert.ToBase64String(main),
            Convert.ToBase64String(store.LoadValue(MainKey)!));
    }

    [Fact]
    public void ForceCommit_throws_when_no_pending_to_commit()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.ForceCommitPendingRotation(store));
    }

    [Fact]
    public void ForceRollback_throws_when_no_pending_to_roll_back()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);
        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.ForceRollbackPendingRotation(store));
    }

    [Fact]
    public void Rotate_stages_pending_before_running_migrator()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);

        var pendingBeforeMigrator = default(byte[]);
        _ = CryptoService.RotateDek(store, (_, _) =>
        {
            pendingBeforeMigrator = store.LoadValue(PendingKey);
        });

        // Before the migrator ran, pending MUST have been written so a
        // crash inside the migrator leaves the tombstone. After RotateDek
        // returns cleanly, the tombstone is deleted.
        Assert.NotNull(pendingBeforeMigrator);
        Assert.Null(store.LoadValue(PendingKey));
    }

    [Fact]
    public void Rotate_deletes_pending_after_successful_promote()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);

        _ = CryptoService.RotateDek(store, (_, _) => { });

        Assert.Null(store.LoadValue(PendingKey));
        Assert.NotNull(store.LoadValue(MainKey));
    }

    [Fact]
    public void Rotate_deletes_pending_when_migrator_throws()
    {
        var store = new Store();
        _ = CryptoService.CreateOrLoad(store);

        Assert.Throws<InvalidOperationException>(() =>
            CryptoService.RotateDek(store, (_, _) =>
                throw new InvalidOperationException("boom")));

        // On migrator failure the cleanup path drops the tombstone; main
        // was never touched in phase-3, so the store returns to a clean
        // single-slot state.
        Assert.Null(store.LoadValue(PendingKey));
        Assert.NotNull(store.LoadValue(MainKey));
    }
}
