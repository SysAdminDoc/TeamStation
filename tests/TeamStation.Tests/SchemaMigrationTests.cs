using Microsoft.Data.Sqlite;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// Covers the full v1 → v3 forward migration path with adversarial row shapes:
/// out-of-range enum integers, whitespace-laden IDs, oversize text fields.
///
/// Invariant under test: <b>no migration ever silently drops a row</b>. Every
/// recoverable row must survive. If a shape is too broken to recover, the
/// migration must fail loudly, never return partial data.
/// </summary>
public class SchemaMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"ts-migrate-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void V1_database_with_malformed_enum_ints_survives_migration_to_v3()
    {
        // Hand-seed a v1-shape database: no schema_version row means
        // MigrateIfNeeded treats it as version 0, which falls through both
        // the v1→v2 and v2→v3 branches.
        SeedV1Database(_dbPath, seedSchemaVersion: 1, seed: (c, tx) =>
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO entries (id, parent_folder_id, name, tv_id, password_enc,
                     mode, quality, access_control,
                     proxy_host, proxy_port, proxy_user, proxy_pass_enc,
                     notes, tags_csv, last_connected,
                     created_utc, modified_utc)
VALUES
    ($a_id, NULL, 'legit',     '123456789',  NULL, 0, 1, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z'),
    ($b_id, NULL, 'oob-mode',  '987654321',  NULL, 99, 2, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z'),
    ($c_id, NULL, 'oob-qual',  '111222333',  NULL, 0, 55, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z'),
    ($d_id, NULL, 'neg-ac',    '444555666',  NULL, 0, 1, -7, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z'),
    ($e_id, NULL, 'ws-id',     '  1234567890  ', NULL, 0, 1, 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z'),
    ($f_id, NULL, 'long-note', '555666777',  NULL, 0, 1, 0, NULL, NULL, NULL, NULL, $long_note, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z');";
            cmd.Parameters.AddWithValue("$a_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$b_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$c_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$d_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$e_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$f_id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$long_note", new string('x', 8192));
            cmd.ExecuteNonQuery();
        });

        // Open via the regular Database constructor, which triggers
        // MigrateIfNeeded. Must not throw.
        var db = new Database(_dbPath);

        // Schema version must now be 3.
        using (var c = db.OpenConnection())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM _meta WHERE key = 'schema_version';";
            var raw = (byte[])cmd.ExecuteScalar()!;
            Assert.Equal(3, BitConverter.ToInt32(raw, 0));
        }

        // Every row survives. Repo materialises them — the adversarial
        // enum ints round-trip as cast enum values; the migration did not
        // drop them.
        var crypto = CryptoService.CreateOrLoad(db);
        var repo = new EntryRepository(db, crypto);
        var all = repo.GetAll();
        Assert.Equal(6, all.Count);
        Assert.Contains(all, e => e.Name == "oob-mode");
        Assert.Contains(all, e => e.Name == "oob-qual");
        Assert.Contains(all, e => e.Name == "neg-ac");
        Assert.Contains(all, e => e.Name == "ws-id");
        Assert.Contains(all, e => e.Name == "long-note");
    }

    [Fact]
    public void V0_database_without_schema_version_row_fast_forwards_to_v3()
    {
        // Pre-v1 / corrupt: _meta has no schema_version. Everything
        // except the tables themselves is absent. Migration must still
        // arrive at v3 without data loss for the rows that were present.
        SeedV1Database(_dbPath, seedSchemaVersion: null, seed: (c, tx) =>
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO entries (id, parent_folder_id, name, tv_id, password_enc,
                     mode, quality, access_control,
                     proxy_host, proxy_port, proxy_user, proxy_pass_enc,
                     notes, tags_csv, last_connected,
                     created_utc, modified_utc)
VALUES ($id, NULL, 'legacy', '222333444', NULL, 0, 1, 0, NULL, NULL, NULL, NULL, 'imported', NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z');";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.ExecuteNonQuery();
        });

        var db = new Database(_dbPath);
        var crypto = CryptoService.CreateOrLoad(db);
        var repo = new EntryRepository(db, crypto);
        var loaded = Assert.Single(repo.GetAll());
        Assert.Equal("legacy", loaded.Name);
        Assert.Equal("imported", loaded.Notes);
    }

    [Fact]
    public void Materialized_entries_with_oob_enum_ints_do_not_crash_launchers()
    {
        // The migration is deliberately permissive — we'd rather keep a row
        // with a nonsense mode int than destroy the user's data. But the
        // launcher boundary MUST then either (a) refuse cleanly with a
        // validation error or (b) fall through to a safe default. A crash
        // on an out-of-range enum int would leak to the UI.
        SeedV1Database(_dbPath, seedSchemaVersion: 1, seed: (c, tx) =>
        {
            var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO entries (id, parent_folder_id, name, tv_id, password_enc,
                     mode, quality, access_control,
                     proxy_host, proxy_port, proxy_user, proxy_pass_enc,
                     notes, tags_csv, last_connected,
                     created_utc, modified_utc)
VALUES ($id, NULL, 'oob-mode', '123456789', NULL, 99, 55, -7, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '2026-04-24T00:00:00Z', '2026-04-24T00:00:00Z');";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.ExecuteNonQuery();
        });

        var db = new Database(_dbPath);
        var crypto = CryptoService.CreateOrLoad(db);
        var repo = new EntryRepository(db, crypto);
        var loaded = Assert.Single(repo.GetAll());

        // CLI argv build must not fault on a malformed row — even if the
        // downstream flags carry nonsense. Unknown Mode enum values fall
        // through to a safe default (no --mode flag) because the switch
        // has no default case; unknown Quality/AccessControl ints are
        // emitted verbatim (TeamViewer rejects them at its own layer).
        // The contract under test is just "does not throw an unhandled
        // NRE / IndexOutOfRange on startup."
        var argv = TeamStation.Launcher.CliArgvBuilder.Build(loaded);
        Assert.Contains("--id", argv);
        Assert.Contains("123456789", argv);
        Assert.DoesNotContain("--mode", argv); // mode=99 -> switch default falls through

        // URI build: the scheme mapper IS strict — an unknown mode raises
        // a defined ArgumentOutOfRangeException, which callers route to
        // the error UI. That is the documented contract, not an NRE.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TeamStation.Launcher.UriSchemeBuilder.Build(loaded));
    }

    [Fact]
    public void Idempotent_migration_on_already_current_schema()
    {
        // A database created once at v3 and then re-opened must be a no-op —
        // we're specifically guarding against a regression where the
        // migration re-runs on every startup and slowly destroys state.
        var db = new Database(_dbPath);
        var crypto = CryptoService.CreateOrLoad(db);
        var repo = new EntryRepository(db, crypto);
        repo.Upsert(new Core.Models.ConnectionEntry
        {
            Name = "pre-existing",
            TeamViewerId = "123456789",
            Password = "pw",
        });

        // Reopen — schema version should stay at 3; the row must survive.
        var db2 = new Database(_dbPath);
        var crypto2 = CryptoService.CreateOrLoad(db2);
        var repo2 = new EntryRepository(db2, crypto2);
        Assert.Single(repo2.GetAll());
    }

    // -----------------------------------------------------------------

    /// <summary>
    /// Creates an on-disk SQLite file with v1-shape <c>entries</c> +
    /// <c>folders</c> + <c>_meta</c> tables and executes the caller's seed
    /// action inside a single transaction. Optionally stamps a
    /// <c>schema_version</c> row so the migration path under test sees
    /// the expected starting point.
    /// </summary>
    private static void SeedV1Database(string path, int? seedSchemaVersion, Action<SqliteConnection, SqliteTransaction> seed)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();

        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        using (var tx = c.BeginTransaction())
        {
            using (var ddl = c.CreateCommand())
            {
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
CREATE INDEX ix_folders_parent ON folders(parent_folder_id);
CREATE TABLE entries (
    id               TEXT PRIMARY KEY,
    parent_folder_id TEXT REFERENCES folders(id) ON DELETE SET NULL,
    name             TEXT NOT NULL,
    tv_id            TEXT NOT NULL,
    password_enc     BLOB,
    mode             INTEGER NOT NULL,
    quality          INTEGER NOT NULL,
    access_control   INTEGER NOT NULL,
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
CREATE INDEX ix_entries_parent ON entries(parent_folder_id);
CREATE INDEX ix_entries_name   ON entries(name);
";
                ddl.ExecuteNonQuery();
            }

            if (seedSchemaVersion is int v)
            {
                using var vwrite = c.CreateCommand();
                vwrite.Transaction = tx;
                vwrite.CommandText = "INSERT INTO _meta(key, value) VALUES ('schema_version', $v);";
                vwrite.Parameters.AddWithValue("$v", BitConverter.GetBytes(v));
                vwrite.ExecuteNonQuery();
            }

            seed(c, tx);
            tx.Commit();
        }
    }
}
