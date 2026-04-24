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
    private const int CurrentSchemaVersion = 1;

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

        using var tx = c.BeginTransaction();
        using (var create = c.CreateCommand())
        {
            create.Transaction = tx;
            create.CommandText = @"
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
CREATE INDEX IF NOT EXISTS ix_entries_parent ON entries(parent_folder_id);
CREATE INDEX IF NOT EXISTS ix_entries_name   ON entries(name);
";
            create.ExecuteNonQuery();
        }

        using (var ver = c.CreateCommand())
        {
            ver.Transaction = tx;
            ver.CommandText = "INSERT OR REPLACE INTO _meta(key, value) VALUES ('schema_version', $v);";
            ver.Parameters.AddWithValue("$v", BitConverter.GetBytes(CurrentSchemaVersion));
            ver.ExecuteNonQuery();
        }

        tx.Commit();
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
