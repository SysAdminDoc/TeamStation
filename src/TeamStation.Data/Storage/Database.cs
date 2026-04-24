using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TeamStation.Data.Security;

namespace TeamStation.Data.Storage;

/// <summary>
/// SQLite schema owner and connection factory. Each call to
/// <see cref="OpenConnection"/> returns a fresh opened connection — Sqlite
/// serializes writes internally so sharing a single connection across the
/// whole process would serialize the UI needlessly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Database : IKeyStore
{
    private const int CurrentSchemaVersion = 2;

    public string Path { get; }

    public Database(string path)
    {
        Path = path;
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var c = new SqliteConnection($"Data Source={Path};Cache=Shared");
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Initialize()
    {
        using var c = new SqliteConnection($"Data Source={Path};Cache=Shared");
        c.Open();

        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        var isFresh = !TableExists(c, "entries");
        EnsureLatestSchema(c);

        if (isFresh)
        {
            using var tx = c.BeginTransaction();
            WriteSchemaVersion(c, tx, CurrentSchemaVersion);
            tx.Commit();
        }
        else
        {
            MigrateIfNeeded(c);
        }
    }

    private static bool TableExists(SqliteConnection c, string name)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static void EnsureLatestSchema(SqliteConnection c)
    {
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS _meta (
    key   TEXT PRIMARY KEY,
    value BLOB NOT NULL
);
CREATE TABLE IF NOT EXISTS folders (
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
CREATE INDEX IF NOT EXISTS ix_folders_parent ON folders(parent_folder_id);
CREATE TABLE IF NOT EXISTS entries (
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
CREATE INDEX IF NOT EXISTS ix_entries_parent ON entries(parent_folder_id);
CREATE INDEX IF NOT EXISTS ix_entries_name   ON entries(name);
";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    private static int ReadSchemaVersion(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM _meta WHERE key = 'schema_version';";
        if (cmd.ExecuteScalar() is byte[] raw && raw.Length >= 4)
            return BitConverter.ToInt32(raw, 0);
        return 0;
    }

    private static void WriteSchemaVersion(SqliteConnection c, SqliteTransaction tx, int version)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO _meta(key, value) VALUES ('schema_version', $v);";
        cmd.Parameters.AddWithValue("$v", BitConverter.GetBytes(version));
        cmd.ExecuteNonQuery();
    }

    private static void MigrateIfNeeded(SqliteConnection c)
    {
        var version = ReadSchemaVersion(c);
        if (version >= CurrentSchemaVersion) return;

        if (version < 2)
        {
            // v1 -> v2: drop NOT NULL from entries.mode / quality / access_control so
            // entries can express "(inherit from folder)" as NULL. SQLite can't ALTER
            // COLUMN, so rebuild the table.
            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
CREATE TABLE entries_v2 (
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
INSERT INTO entries_v2
SELECT id, parent_folder_id, name, tv_id, password_enc, mode, quality, access_control,
       proxy_host, proxy_port, proxy_user, proxy_pass_enc, notes, tags_csv, last_connected,
       created_utc, modified_utc
  FROM entries;
DROP TABLE entries;
ALTER TABLE entries_v2 RENAME TO entries;
CREATE INDEX ix_entries_parent ON entries(parent_folder_id);
CREATE INDEX ix_entries_name   ON entries(name);
";
                cmd.ExecuteNonQuery();
            }
            WriteSchemaVersion(c, tx, 2);
            tx.Commit();
        }
    }

    // ---- IKeyStore ----
    public byte[]? Load()
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM _meta WHERE key = 'dek_v1';";
        var result = cmd.ExecuteScalar();
        return result as byte[];
    }

    public void Save(byte[] wrapped)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO _meta(key, value) VALUES ('dek_v1', $v);";
        cmd.Parameters.AddWithValue("$v", wrapped);
        cmd.ExecuteNonQuery();
    }
}
