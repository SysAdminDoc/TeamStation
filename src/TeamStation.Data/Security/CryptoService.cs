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
///
/// <para>
/// The in-memory DEK buffer is allocated pinned
/// (<see cref="GC.AllocateArray{T}(int,bool)"/>) so a GC compaction cannot
/// leave stale key material at the previous heap address. Instances are
/// <see cref="IDisposable"/>; <see cref="Dispose"/> zeros the buffer via
/// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/> and any
/// subsequent operation throws <see cref="ObjectDisposedException"/>. The
/// owning object graph (normally <c>App</c>) is responsible for disposal at
/// process exit so a heap snapshot taken after shutdown cannot recover the
/// DEK.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CryptoService : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int MasterSaltSize = 32;
    private const int DpapiEntropySize = 32;
    private const int LegacyPbkdf2Iterations = 310_000;
    private const int Argon2TimeCost = 3;
    private const int Argon2MemoryKiB = 64 * 1024; // 64 MiB
    private const int Argon2Parallelism = 2;
    private const string DpapiDekKey = "dek_v1";
    private const string DpapiDekPendingKey = "dek_v1_pending";
    private const string DpapiEntropyKey = "dpapi_entropy_v1";
    private const string MasterWrappedDekKey = "dek_master_v1";
    private const string MasterSaltKey = "dek_master_salt_v1";
    private const string MasterKdfTag = "dek_master_kdf_v1";
    private const string KdfIdPbkdf2 = "pbkdf2_v1";
    private const string KdfIdArgon2id = "argon2id_v1";

    private readonly byte[] _dek;
    private bool _disposed;

    private CryptoService(byte[] pinnedDek)
    {
        _dek = pinnedDek;
    }

    /// <summary>
    /// Allocates a pinned buffer of <paramref name="size"/> bytes so a GC
    /// compaction cannot relocate the key material and leave a copy at the
    /// old heap address. Pinned allocations are a controlled-cost feature
    /// available since .NET 5 (<see cref="GC.AllocateArray{T}(int,bool)"/>).
    /// </summary>
    private static byte[] AllocatePinned(int size)
    {
        var buffer = GC.AllocateArray<byte>(size, pinned: true);
        return buffer;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into a fresh pinned buffer of the
    /// same length and zeroes the original. Callers that received their DEK
    /// from <see cref="ProtectedData.Unprotect(byte[],byte[]?,DataProtectionScope)"/>
    /// or <see cref="RandomNumberGenerator.GetBytes(int)"/> (both of which
    /// allocate unpinned arrays) must pin before handing ownership to a
    /// <see cref="CryptoService"/> instance.
    /// </summary>
    private static byte[] IntoPinned(byte[] source)
    {
        var pinned = AllocatePinned(source.Length);
        Buffer.BlockCopy(source, 0, pinned, 0, source.Length);
        CryptographicOperations.ZeroMemory(source);
        return pinned;
    }

    public static CryptoService CreateOrLoad(IKeyStore keyStore)
    {
        return CreateOrLoad(keyStore, CryptoUnlockOptions.Dpapi);
    }

    public static CryptoService CreateOrLoad(IKeyStore keyStore, CryptoUnlockOptions options)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(options);

        // Before any unlock path touches the store we reconcile any tombstone
        // left behind by a previous rotation. The ambiguous "interrupted
        // mid-rotation" state surfaces as an InvalidOperationException here
        // rather than silently auto-rolling-back — rolling back would destroy
        // rows that the migrator already re-encrypted under the staged DEK,
        // and committing would destroy rows that had not yet been migrated.
        // The correct answer requires human judgment (or a caller-driven
        // recovery UX), not a default.
        ReconcilePendingRotation(keyStore);

        if (options.UseMasterPassword)
            return CreateOrLoadWithMasterPassword(keyStore, options.MasterPasswordValue);

        // Per-database entropy salt is hashed into ProtectedData's
        // optionalEntropy parameter so the DPAPI trust boundary becomes
        // "same Windows user AND has read this database file" instead of
        // just "same Windows user". Defends against opportunistic malware
        // that scrapes DPAPI blobs in bulk without ever opening our DB.
        var entropy = GetOrCreateDpapiEntropy(keyStore, createIfMissing: true);

        var wrapped = keyStore.Load();
        byte[] dek;
        if (wrapped is null)
        {
            dek = RandomNumberGenerator.GetBytes(KeySize);
            var protectedDek = ProtectedData.Protect(dek, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);
            keyStore.Save(protectedDek);
        }
        else
        {
            dek = UnprotectDekWithLegacyFallback(wrapped, entropy, keyStore);
            if (dek.Length != KeySize)
                throw new CryptographicException("Stored DEK has unexpected length.");
        }

        return new CryptoService(IntoPinned(dek));
    }

    /// <summary>
    /// Fetches the per-database DPAPI entropy salt or generates and persists
    /// one when missing. Falls through with <c>null</c> on stores that only
    /// implement <see cref="IKeyStore"/> (no <see cref="ISecretStore.SaveValue"/>
    /// surface) — those callers keep the legacy null-entropy behaviour and
    /// the function does NOT crash, which preserves backward compatibility
    /// with any test stub that pre-dates the entropy upgrade.
    /// </summary>
    private static byte[]? GetOrCreateDpapiEntropy(IKeyStore keyStore, bool createIfMissing)
    {
        if (keyStore is not ISecretStore secret)
            return null;

        var stored = secret.LoadValue(DpapiEntropyKey);
        if (stored is { Length: DpapiEntropySize })
            return stored;

        if (!createIfMissing)
            return null;

        var fresh = RandomNumberGenerator.GetBytes(DpapiEntropySize);
        secret.SaveValue(DpapiEntropyKey, fresh);
        return fresh;
    }

    /// <summary>
    /// Unwraps a stored DEK with the per-database entropy salt and falls back
    /// to <c>optionalEntropy: null</c> on <see cref="CryptographicException"/>
    /// so legacy v0.3.2 (and earlier) installs that wrapped under null
    /// entropy keep working. On a successful legacy unwrap the DEK is
    /// silently re-wrapped under the new entropy and the wrap row is
    /// updated so the next load takes the fast path.
    /// </summary>
    private static byte[] UnprotectDekWithLegacyFallback(byte[] wrapped, byte[]? entropy, IKeyStore keyStore)
    {
        if (entropy is null)
        {
            // No ISecretStore surface — preserve legacy null-entropy behaviour.
            return ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        }

        try
        {
            return ProtectedData.Unprotect(wrapped, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Legacy null-entropy wrap. Unwrap with the original entropy,
            // then re-wrap under the new entropy and persist so we don't
            // hit the catch path again on the next load.
            var dek = ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            try
            {
                var rewrapped = ProtectedData.Protect(dek, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);
                keyStore.Save(rewrapped);
            }
            catch
            {
                // Re-wrap is best-effort. If the persist fails we still hand
                // back a working DEK; the next load will hit the same legacy
                // path and try again.
            }
            return dek;
        }
    }

    public static bool HasMasterPassword(ISecretStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        return keyStore.LoadValue(MasterWrappedDekKey) is not null;
    }

    /// <summary>
    /// Returns the current rotation state recorded in <paramref name="keyStore"/>.
    /// Useful for startup UX that wants to surface a "rotation was interrupted"
    /// recovery dialog before <see cref="CreateOrLoad(IKeyStore)"/> throws.
    /// </summary>
    public static RotationState InspectPendingRotation(IKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        if (keyStore is not ISecretStore secret)
            return RotationState.None;

        var pending = secret.LoadValue(DpapiDekPendingKey);
        if (pending is null || pending.Length == 0)
            return RotationState.None;

        var main = secret.LoadValue(DpapiDekKey);
        if (main is null)
        {
            // Pending exists but no main DEK — the pre-migrator stage landed
            // on a store that has never held a primary wrap. Treat as orphan
            // so reconciliation drops the stray row; the caller's next
            // CreateOrLoad will then seed a fresh DEK.
            return RotationState.PendingOrphan;
        }

        return CryptographicOperations.FixedTimeEquals(pending, main)
            ? RotationState.PendingOrphan
            : RotationState.InterruptedMidRotation;
    }

    /// <summary>
    /// Clears a <see cref="RotationState.PendingOrphan"/> tombstone silently.
    /// When the state is <see cref="RotationState.InterruptedMidRotation"/>
    /// this method throws — the caller must either call
    /// <see cref="ForceRollbackPendingRotation"/> (keep old main, drop pending;
    /// rows re-encrypted by the interrupted migrator become unrecoverable) or
    /// <see cref="ForceCommitPendingRotation"/> (promote pending to main;
    /// rows not yet re-encrypted by the interrupted migrator become
    /// unrecoverable) after checking the DB row state.
    /// </summary>
    public static void ReconcilePendingRotation(IKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        if (keyStore is not ISecretStore secret)
            return;

        var state = InspectPendingRotation(keyStore);
        switch (state)
        {
            case RotationState.None:
                return;
            case RotationState.PendingOrphan:
                secret.DeleteValue(DpapiDekPendingKey);
                return;
            case RotationState.InterruptedMidRotation:
                throw new InvalidOperationException(
                    "A DEK rotation was interrupted before completion. The database may contain rows "
                  + "encrypted under the new DEK and rows encrypted under the old DEK. Run a manual "
                  + "recovery by calling CryptoService.ForceCommitPendingRotation (if rows were "
                  + "migrated) or CryptoService.ForceRollbackPendingRotation (if they were not) "
                  + "after inspecting the backing store.");
            default:
                throw new InvalidOperationException($"Unknown rotation state: {state}");
        }
    }

    /// <summary>
    /// Promotes the pending wrap to the main slot and deletes the pending
    /// tombstone. The caller guarantees, externally, that the row migration
    /// under the pending DEK completed before the crash.
    /// </summary>
    public static void ForceCommitPendingRotation(IKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        if (keyStore is not ISecretStore secret)
            throw new InvalidOperationException("Pending-rotation recovery requires an ISecretStore implementation.");

        var pending = secret.LoadValue(DpapiDekPendingKey)
            ?? throw new InvalidOperationException("No pending rotation to commit.");
        secret.SaveValue(DpapiDekKey, pending);
        secret.DeleteValue(DpapiDekPendingKey);
    }

    /// <summary>
    /// Deletes the pending tombstone without touching the main slot. The
    /// caller guarantees, externally, that the row migration under the
    /// pending DEK did NOT touch any persisted ciphertext before the crash.
    /// </summary>
    public static void ForceRollbackPendingRotation(IKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        if (keyStore is not ISecretStore secret)
            throw new InvalidOperationException("Pending-rotation recovery requires an ISecretStore implementation.");

        if (secret.LoadValue(DpapiDekPendingKey) is null)
            throw new InvalidOperationException("No pending rotation to roll back.");
        secret.DeleteValue(DpapiDekPendingKey);
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

        return new CryptoService(IntoPinned(dek));
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
        // The master-password "carry the existing DPAPI DEK over" path: try
        // the new entropy first, fall through to a legacy null-entropy probe.
        // We do NOT createIfMissing here — if no salt exists yet we want the
        // legacy unwrap to succeed and the caller decides whether to seed the
        // master-password envelope without persisting an unused salt row.
        var entropy = GetOrCreateDpapiEntropy(keyStore, createIfMissing: false);
        try
        {
            var dek = entropy is null
                ? ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser)
                : UnprotectDekWithLegacyFallback(wrapped, entropy, keyStore);
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

    private static byte[] DerivePbkdf2(string masterPassword, byte[] salt)
    {
        // Rfc2898DeriveBytes.Pbkdf2 takes the password as `string` directly,
        // so no extra byte[] copy of the master password exists on our
        // heap beyond the immutable String itself. Nothing to zero here.
        return Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, LegacyPbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    private static byte[] DeriveArgon2id(string masterPassword, byte[] salt)
    {
        // Argon2id's ctor takes a byte[], so we MUST materialise the UTF-8
        // bytes of the master password. Zero that buffer as soon as the
        // derivation completes so a post-derivation memory dump cannot
        // scrape a recoverable copy of the master password from the heap.
        var pwBytes = Encoding.UTF8.GetBytes(masterPassword);
        try
        {
            using var argon2 = new Argon2id(pwBytes)
            {
                Salt = salt,
                Iterations = Argon2TimeCost,
                MemorySize = Argon2MemoryKiB,
                DegreeOfParallelism = Argon2Parallelism,
            };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwBytes);
        }
    }

    /// <summary>
    /// Two-phase-commit DEK rotation: stages the new wrapped DEK under the
    /// <c>dek_v1_pending</c> tombstone, runs <paramref name="migrator"/>, then
    /// atomically promotes the pending slot to <c>dek_v1</c> and deletes the
    /// tombstone. The previous "save new; run migrator; leave on failure"
    /// window (where a crash between migrator completion and the key-store
    /// save would have left rows re-encrypted under a DEK that no longer
    /// existed in the store) is gone — the tombstone is the crash-recovery
    /// marker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recovery semantics on startup (<see cref="CreateOrLoad(IKeyStore)"/>
    /// calls <see cref="ReconcilePendingRotation"/> first):
    /// </para>
    /// <list type="bullet">
    ///   <item><b>No pending.</b> Normal startup.</item>
    ///   <item><b>Pending exists and equals main.</b> Orphan from a post-commit
    ///     crash (step 3 succeeded, step 4 didn't). Auto-reconciled: the
    ///     tombstone is deleted and startup continues.</item>
    ///   <item><b>Pending exists and differs from main.</b> The rotation was
    ///     interrupted between staging and commit. Auto-recovery is unsafe —
    ///     the caller must call <see cref="ForceCommitPendingRotation"/> or
    ///     <see cref="ForceRollbackPendingRotation"/> after inspecting the
    ///     DB state to decide which ciphertexts survived.</item>
    /// </list>
    /// <para>
    /// Rotation is currently DPAPI-only. Master-password envelopes are not
    /// supported here — that path requires re-prompting for the master
    /// password and deriving a fresh Argon2id salt, which is a separate
    /// UX flow.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Raised when <paramref name="keyStore"/> has no wrapped DEK yet (nothing
    /// to rotate from), when the store holds a master-password envelope, or
    /// when a previous rotation's interrupted-mid-rotation tombstone has not
    /// yet been reconciled.
    /// </exception>
    public static CryptoService RotateDek(IKeyStore keyStore, Action<CryptoService, CryptoService> migrator)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(migrator);

        if (keyStore is not ISecretStore secret)
            throw new InvalidOperationException("DEK rotation requires an ISecretStore implementation.");
        if (secret.LoadValue(MasterWrappedDekKey) is not null)
            throw new InvalidOperationException(
                "DEK rotation is not supported while the database is unlocked with a master password. "
              + "Re-lock with DPAPI or implement the master-password rotation flow first.");

        // An existing tombstone blocks a new rotation. The user has to
        // resolve the prior state explicitly — stacking rotations would
        // compound the ambiguity.
        ReconcilePendingRotation(keyStore);

        var oldWrapped = secret.LoadValue(DpapiDekKey)
            ?? throw new InvalidOperationException("No DEK is stored; nothing to rotate from.");
        // Rotation participates in the same per-database entropy hardening
        // as CreateOrLoad: read the salt (creating one on the fly if a
        // pre-entropy install is rotating for the first time), unwrap with
        // legacy fallback, and persist the new wrap with the salt baked in.
        var entropy = GetOrCreateDpapiEntropy(keyStore, createIfMissing: true);
        var oldDekUnpinned = UnprotectDekWithLegacyFallback(oldWrapped, entropy, keyStore);
        if (oldDekUnpinned.Length != KeySize)
            throw new CryptographicException("Stored DEK has unexpected length.");

        var newDekUnpinned = RandomNumberGenerator.GetBytes(KeySize);
        var newWrapped = ProtectedData.Protect(newDekUnpinned, optionalEntropy: entropy, scope: DataProtectionScope.CurrentUser);

        // Hand the pinned buffers to the services so the caller sees the
        // same memory-hygiene guarantees on temporary rotation services as
        // on the long-lived process-wide one.
        var oldSvc = new CryptoService(IntoPinned(oldDekUnpinned));
        var newSvc = new CryptoService(IntoPinned(newDekUnpinned));

        // Phase tracking drives the catch-block cleanup decision. Transitions:
        //   0 → 1 pending staged
        //   1 → 2 migrator returned
        //   2 → 3 main promoted (atomic INSERT OR REPLACE)
        //   3 → 4 pending deleted (best-effort)
        // The cleanup strategy in the catch block depends on which phase
        // boundary threw — the comment inside the handler enumerates each.
        var phase = 0;
        try
        {
            secret.SaveValue(DpapiDekPendingKey, newWrapped);
            phase = 1;

            migrator(oldSvc, newSvc);
            phase = 2;

            secret.SaveValue(DpapiDekKey, newWrapped);
            phase = 3;

            try { secret.DeleteValue(DpapiDekPendingKey); }
            catch { /* startup will tidy the orphan */ }
            phase = 4;
        }
        catch
        {
            // phase == 0: the phase-1 save itself threw before anything
            //     landed on disk. Nothing to clean; just dispose and rethrow.
            // phase == 1: migrator threw. Pending is staged; main untouched.
            //     Rows may be in an indeterminate state if the migrator
            //     wasn't transactional, but that's the caller's problem —
            //     we just drop the pending tombstone so the store returns
            //     to a clean single-slot layout (matching v0.3.0 semantics
            //     minus the misleading "restore old wrap" swap).
            // phase == 2: the phase-3 promote save threw. Rows are re-
            //     encrypted under newDek, main is still oldWrapped, pending
            //     is newWrapped. Leave the tombstone in place so startup
            //     surfaces InterruptedMidRotation and the user chooses
            //     ForceCommit (preferred — pending wrap matches row
            //     encryption) or ForceRollback.
            if (phase == 1)
            {
                try { secret.DeleteValue(DpapiDekPendingKey); }
                catch { /* leave tombstone; startup will surface */ }
            }
            oldSvc.Dispose();
            newSvc.Dispose();
            throw;
        }

        oldSvc.Dispose();
        return newSvc;
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> bytes directly under the DEK,
    /// returning the same nonce|tag|ciphertext wire format
    /// <see cref="EncryptString"/> produces. Caller-owned buffer — this
    /// method does NOT zero <paramref name="plaintext"/>; the caller decides
    /// when the cleartext can go away.
    /// </summary>
    /// <remarks>
    /// New in v0.3.4. The launch hot path uses this overload so the password
    /// stays in a zeroable byte buffer instead of a CLR-interned
    /// <see cref="string"/>. UI bindings keep using <see cref="EncryptString"/>
    /// — the binding layer can't zero CLR strings, so refactoring those
    /// surfaces is performative.
    /// </remarks>
    public byte[]? EncryptBytes(byte[]? plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (plaintext is null) return null;
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_dek, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return output;
    }

    /// <summary>
    /// Decrypts <paramref name="ciphertext"/> into a fresh <see cref="byte"/>
    /// array of UTF-8 plaintext. The caller owns the returned buffer and is
    /// expected to zero it via
    /// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/> as soon
    /// as the cleartext is no longer needed. New in v0.3.4 — the legacy
    /// <see cref="DecryptString"/> shim now routes through this method.
    /// </summary>
    public byte[]? DecryptToBytes(byte[]? ciphertext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
        return plain;
    }

    public byte[]? EncryptString(string? plaintext)
    {
        if (plaintext is null) return null;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return EncryptBytes(bytes);
        }
        finally
        {
            // The intermediate UTF-8 buffer we allocated is ours to zero —
            // the source string itself is CLR-interned and unzeroable, but
            // we don't need to leak the byte[] copy on top of that.
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? DecryptString(byte[]? ciphertext)
    {
        var bytes = DecryptToBytes(ciphertext);
        if (bytes is null) return null;
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            // Zero the intermediate UTF-8 buffer. The returned string is
            // unavoidable on this legacy code path — callers wanting a
            // zeroable cleartext should call DecryptToBytes directly.
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>
    /// Zeros the in-memory DEK so a post-dispose heap snapshot cannot recover
    /// the key. Subsequent calls to <see cref="EncryptString"/> or
    /// <see cref="DecryptString"/> throw <see cref="ObjectDisposedException"/>.
    /// Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_dek);
        _disposed = true;
    }
}

/// <summary>
/// State of the in-flight-rotation marker recorded in the key store.
/// See <see cref="CryptoService.InspectPendingRotation"/> for the classification.
/// </summary>
public enum RotationState
{
    /// <summary>No pending-rotation tombstone present.</summary>
    None = 0,
    /// <summary>
    /// A pending wrap exists and is byte-equal to the main wrap. This is the
    /// post-commit orphan state; <see cref="CryptoService.ReconcilePendingRotation"/>
    /// clears it silently on next startup.
    /// </summary>
    PendingOrphan = 1,
    /// <summary>
    /// A pending wrap exists and differs from the main wrap. A prior rotation
    /// was interrupted; auto-recovery is unsafe because the database row
    /// state (re-encrypted or not) cannot be inferred from the key store
    /// alone. The caller must choose rollback or commit after inspection.
    /// </summary>
    InterruptedMidRotation = 2,
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
    void DeleteValue(string key);
}

public sealed record CryptoUnlockOptions(bool UseMasterPassword, string? MasterPasswordValue)
{
    public static readonly CryptoUnlockOptions Dpapi = new(UseMasterPassword: false, MasterPasswordValue: null);

    public static CryptoUnlockOptions WithMasterPassword(string password) =>
        new(UseMasterPassword: true, MasterPasswordValue: password);
}
