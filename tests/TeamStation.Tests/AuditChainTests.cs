using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// Verifies the HMAC-SHA256 audit-log integrity chain:
/// <list type="bullet">
///   <item>Genesis row uses all-zero prev_hash.</item>
///   <item>Subsequent rows form an unbroken chain.</item>
///   <item>Tampered <c>row_hash</c> is detected.</item>
///   <item>Missing (deleted) middle row breaks the chain via prev_hash mismatch.</item>
///   <item>Legacy rows (NULL <c>row_hash</c>) are skipped without invalidating newer rows.</item>
///   <item>Schema migration v3→v4 correctly adds nullable columns to existing tables.</item>
/// </list>
/// </summary>
public sealed class AuditChainTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly CryptoService _crypto;
    private readonly AuditLogRepository _audit;

    public AuditChainTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ts-chain-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _crypto = CryptoService.CreateOrLoad(_db);
        _audit = new AuditLogRepository(_db, _crypto);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); }
            catch { /* best-effort */ }
        }
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    private static AuditEvent MakeEvent(string action = "test-action") =>
        new() { Action = action, TargetType = "test", Summary = $"Unit-test event — {action}" };

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyLog_VerifyChain_IsValid()
    {
        var result = _audit.VerifyChain();

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(0, result.RowsVerified);
        Assert.Equal(0, result.LegacyRowsSkipped);
    }

    [Fact]
    public void SingleRow_GenesisHashIsAllZero()
    {
        _audit.Append(MakeEvent("genesis"));

        // Verify the chain is valid (implicitly checks genesis prev_hash).
        var result = _audit.VerifyChain();
        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(1, result.RowsVerified);

        // Also confirm the stored prev_hash is literally 32 zero bytes.
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT prev_hash FROM audit_log ORDER BY rowid ASC LIMIT 1;";
        var prevHash = (byte[])cmd.ExecuteScalar()!;
        Assert.Equal(32, prevHash.Length);
        Assert.All(prevHash, b => Assert.Equal(0, b));
    }

    [Fact]
    public void MultipleRows_ChainIsValid()
    {
        _audit.Append(MakeEvent("alpha"));
        _audit.Append(MakeEvent("beta"));
        _audit.Append(MakeEvent("gamma"));

        var result = _audit.VerifyChain();

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(3, result.RowsVerified);
        Assert.Equal(0, result.LegacyRowsSkipped);
        Assert.Null(result.FailedAtId);
    }

    [Fact]
    public void TamperedRowHash_IsDetected()
    {
        _audit.Append(MakeEvent("row-1"));
        _audit.Append(MakeEvent("row-2"));

        // Corrupt the row_hash of the first row.
        using (var c = _db.OpenConnection())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
UPDATE audit_log
SET    row_hash = randomblob(32)
WHERE  rowid = (SELECT MIN(rowid) FROM audit_log);";
            cmd.ExecuteNonQuery();
        }

        var result = _audit.VerifyChain();

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailedAtId);
        Assert.Contains("HMAC mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeletedMiddleRow_BreaksChainViaPrevHashMismatch()
    {
        _audit.Append(MakeEvent("first"));
        _audit.Append(MakeEvent("middle"));
        _audit.Append(MakeEvent("last"));

        // Delete the middle row — this orphans "last"'s prev_hash.
        using (var c = _db.OpenConnection())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
DELETE FROM audit_log
WHERE rowid = (
    SELECT rowid FROM audit_log ORDER BY rowid ASC LIMIT 1 OFFSET 1
);";
            cmd.ExecuteNonQuery();
        }

        var result = _audit.VerifyChain();

        Assert.False(result.IsValid);
        Assert.NotNull(result.FailedAtId);
        Assert.Contains("prev_hash mismatch", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyRowsWithNullHash_AreSkippedGracefully()
    {
        // Insert a legacy row (NULL prev_hash / row_hash) directly, simulating
        // a row written before the schema-v4 migration.
        var legacyId = Guid.NewGuid().ToString("D");
        using (var c = _db.OpenConnection())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO audit_log (id, occurred_utc, action, target_type, target_id, summary, detail, prev_hash, row_hash)
VALUES ($id, '2024-01-01T00:00:00.0000000+00:00', 'legacy', 'test', NULL, 'legacy row', NULL, NULL, NULL);";
            cmd.Parameters.AddWithValue("$id", legacyId);
            cmd.ExecuteNonQuery();
        }

        // Append a proper HMAC row after the legacy row.
        _audit.Append(MakeEvent("post-legacy"));

        var result = _audit.VerifyChain();

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(1, result.RowsVerified);
        Assert.Equal(1, result.LegacyRowsSkipped);
    }

    [Fact]
    public void NoCrypto_VerifyChain_ReturnsInvalidWithReason()
    {
        var auditNoCrypto = new AuditLogRepository(_db); // no crypto
        var result = auditNoCrypto.VerifyChain();

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
        Assert.Equal(0, result.RowsVerified);
    }

    [Fact]
    public void SchemaMigrationV3ToV4_AddsHashColumns()
    {
        // Seed a v3 database on-disk (schema without prev_hash/row_hash).
        var v3Path = Path.Combine(Path.GetTempPath(), $"ts-chain-v3seed-{Guid.NewGuid():N}.db");
        try
        {
            SeedV3Database(v3Path);

            // Opening via Database should trigger the v3→v4 migration.
            var db = new Database(v3Path);

            // Confirm the new columns exist by trying to INSERT into them.
            using var c = db.OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
INSERT INTO audit_log
    (id, occurred_utc, action, target_type, summary, prev_hash, row_hash)
VALUES ('00000000-0000-0000-0000-000000000001',
        '2024-01-01T00:00:00.0000000+00:00',
        'migration-test', 'test', 'inserted after v4 migration',
        NULL, NULL);";
            // If the columns don't exist this will throw.
            cmd.ExecuteNonQuery();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { if (File.Exists(v3Path + suffix)) File.Delete(v3Path + suffix); }
                catch { /* best-effort */ }
            }
        }
    }

    // ------------------------------------------------------------------
    // Schema seed helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a minimal on-disk SQLite file shaped like a v3 schema — the
    /// <c>audit_log</c> table exists but is missing the <c>prev_hash</c> and
    /// <c>row_hash</c> columns that the v3→v4 migration adds.
    ///
    /// The <c>entries</c> table must be present so that
    /// <see cref="Database.Initialize"/> considers the DB non-fresh and calls
    /// <c>MigrateIfNeeded</c> rather than <c>EnsureLatestSchema</c>.
    /// The schema version is stored as a 4-byte LE BLOB to match the
    /// encoding used by <c>ReadSchemaVersion</c>/<c>WriteSchemaVersion</c>.
    /// </summary>
    private static void SeedV3Database(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var ddl = conn.CreateCommand();
        ddl.Transaction = tx;
        ddl.CommandText = @"
CREATE TABLE _meta (key TEXT PRIMARY KEY, value BLOB NOT NULL);
CREATE TABLE folders (
    id                   TEXT PRIMARY KEY,
    parent_folder_id     TEXT REFERENCES folders(id) ON DELETE CASCADE,
    name                 TEXT NOT NULL,
    accent_color         TEXT,
    sort_order           INTEGER NOT NULL DEFAULT 0,
    default_mode         INTEGER,
    default_quality      INTEGER,
    default_access       INTEGER,
    default_password_enc BLOB
);
CREATE TABLE entries (
    id               TEXT PRIMARY KEY,
    parent_folder_id TEXT REFERENCES folders(id) ON DELETE SET NULL,
    name             TEXT NOT NULL,
    tv_id            TEXT NOT NULL,
    password_enc     BLOB,
    mode             INTEGER,
    quality          INTEGER,
    access_control   INTEGER,
    proxy_host       TEXT,
    proxy_port       INTEGER,
    proxy_user       TEXT,
    proxy_pass_enc   BLOB,
    notes            TEXT,
    tags_csv         TEXT,
    last_connected   TEXT,
    created_utc      TEXT NOT NULL,
    modified_utc     TEXT NOT NULL
);
CREATE TABLE session_history (
    id             TEXT PRIMARY KEY,
    entry_id       TEXT,
    entry_name     TEXT NOT NULL,
    tv_id          TEXT NOT NULL,
    profile_name   TEXT NOT NULL,
    mode           INTEGER,
    route          TEXT NOT NULL,
    process_id     INTEGER,
    started_utc    TEXT NOT NULL,
    ended_utc      TEXT,
    notes          TEXT,
    outcome        TEXT
);
CREATE TABLE audit_log (
    id            TEXT PRIMARY KEY,
    occurred_utc  TEXT NOT NULL,
    action        TEXT NOT NULL,
    target_type   TEXT NOT NULL,
    target_id     TEXT,
    summary       TEXT NOT NULL,
    detail        TEXT
);";
        ddl.ExecuteNonQuery();

        // Use the same 4-byte LE BLOB encoding as ReadSchemaVersion/WriteSchemaVersion.
        using var vCmd = conn.CreateCommand();
        vCmd.Transaction = tx;
        vCmd.CommandText = "INSERT INTO _meta(key, value) VALUES ('schema_version', $v);";
        vCmd.Parameters.AddWithValue("$v", BitConverter.GetBytes(3));
        vCmd.ExecuteNonQuery();
        tx.Commit();
    }
}
