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
public sealed class Database : ISecretStore
{
    private const int CurrentSchemaVersion = 3;

    public string Path { get; }
    public bool OptimizeOnConnectionClose { get; set; } = true;

    public Database(string path)
    {
        Path = path;
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var c = new OptimizingSqliteConnection($"Data Source={Path};Cache=Shared", () => OptimizeOnConnectionClose);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    public DatabaseIntegrityReport CheckIntegrity(int maxMessages = 8)
    {
        if (maxMessages < 1)
            maxMessages = 1;

        try
        {
            using var c = OpenConnection();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";

            var messages = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read() && messages.Count < maxMessages)
            {
                if (!reader.IsDBNull(0))
                    messages.Add(reader.GetString(0));
            }

            if (messages.Count == 1 && string.Equals(messages[0], "ok", StringComparison.OrdinalIgnoreCase))
                return DatabaseIntegrityReport.Ok;

            return new DatabaseIntegrityReport(
                IsOk: false,
                Messages: messages.Count == 0 ? ["integrity_check returned no rows"] : messages,
                ErrorMessage: null);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or ObjectDisposedException)
        {
            return new DatabaseIntegrityReport(
                IsOk: false,
                Messages: [],
                ErrorMessage: $"{ex.GetType().Name}: {ex.Message}");
        }
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
    default_password_enc BLOB,
    default_tv_path      TEXT,
    default_wake_broadcast TEXT,
    pre_launch_script    TEXT,
    post_launch_script   TEXT
);
CREATE INDEX IF NOT EXISTS ix_folders_parent ON folders(parent_folder_id);
CREATE TABLE IF NOT EXISTS entries (
    id               TEXT PRIMARY KEY,
    parent_folder_id TEXT REFERENCES folders(id) ON DELETE SET NULL,
    name             TEXT NOT NULL,
    tv_id            TEXT NOT NULL,
    profile_name     TEXT NOT NULL DEFAULT 'Default',
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
    tv_path_override TEXT,
    is_pinned        INTEGER NOT NULL DEFAULT 0,
    wake_mac         TEXT,
    wake_broadcast   TEXT,
    pre_launch_script TEXT,
    post_launch_script TEXT,
    created_utc      TEXT NOT NULL,
    modified_utc     TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_entries_parent ON entries(parent_folder_id);
CREATE INDEX IF NOT EXISTS ix_entries_name   ON entries(name);
CREATE INDEX IF NOT EXISTS ix_entries_last_connected ON entries(last_connected);
CREATE TABLE IF NOT EXISTS session_history (
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
CREATE INDEX IF NOT EXISTS ix_session_history_started ON session_history(started_utc);
CREATE TABLE IF NOT EXISTS audit_log (
    id            TEXT PRIMARY KEY,
    occurred_utc  TEXT NOT NULL,
    action        TEXT NOT NULL,
    target_type   TEXT NOT NULL,
    target_id     TEXT,
    summary       TEXT NOT NULL,
    detail        TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_log_occurred ON audit_log(occurred_utc);
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
            version = 2;
        }

        if (version < 3)
        {
            using var tx = c.BeginTransaction();
            AddColumnIfMissing(c, tx, "entries", "profile_name", "TEXT NOT NULL DEFAULT 'Default'");
            AddColumnIfMissing(c, tx, "entries", "tv_path_override", "TEXT");
            AddColumnIfMissing(c, tx, "entries", "is_pinned", "INTEGER NOT NULL DEFAULT 0");
            AddColumnIfMissing(c, tx, "entries", "wake_mac", "TEXT");
            AddColumnIfMissing(c, tx, "entries", "wake_broadcast", "TEXT");
            AddColumnIfMissing(c, tx, "entries", "pre_launch_script", "TEXT");
            AddColumnIfMissing(c, tx, "entries", "post_launch_script", "TEXT");
            AddColumnIfMissing(c, tx, "folders", "default_tv_path", "TEXT");
            AddColumnIfMissing(c, tx, "folders", "default_wake_broadcast", "TEXT");
            AddColumnIfMissing(c, tx, "folders", "pre_launch_script", "TEXT");
            AddColumnIfMissing(c, tx, "folders", "post_launch_script", "TEXT");

            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
CREATE INDEX IF NOT EXISTS ix_entries_last_connected ON entries(last_connected);
CREATE TABLE IF NOT EXISTS session_history (
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
CREATE INDEX IF NOT EXISTS ix_session_history_started ON session_history(started_utc);
CREATE TABLE IF NOT EXISTS audit_log (
    id            TEXT PRIMARY KEY,
    occurred_utc  TEXT NOT NULL,
    action        TEXT NOT NULL,
    target_type   TEXT NOT NULL,
    target_id     TEXT,
    summary       TEXT NOT NULL,
    detail        TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_log_occurred ON audit_log(occurred_utc);
";
                cmd.ExecuteNonQuery();
            }

            WriteSchemaVersion(c, tx, 3);
            tx.Commit();
        }
    }

    private static void AddColumnIfMissing(
        SqliteConnection c,
        SqliteTransaction tx,
        string table,
        string column,
        string definition)
    {
        using (var probe = c.CreateCommand())
        {
            probe.Transaction = tx;
            probe.CommandText = $"PRAGMA table_info({table});";
            using var reader = probe.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        cmd.ExecuteNonQuery();
    }

    // ---- IKeyStore ----
    public byte[]? Load()
    {
        return LoadValue("dek_v1");
    }

    public void Save(byte[] wrapped)
    {
        SaveValue("dek_v1", wrapped);
    }

    public byte[]? LoadValue(string key)
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM _meta WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result as byte[];
    }

    public void SaveValue(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO _meta(key, value) VALUES ($key, $v);";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteValue(string key)
    {
        using var c = OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM _meta WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    private sealed class OptimizingSqliteConnection : SqliteConnection
    {
        private readonly Func<bool> _shouldOptimize;
        private bool _hasOptimized;

        public OptimizingSqliteConnection(string connectionString, Func<bool> shouldOptimize)
            : base(connectionString)
        {
            _shouldOptimize = shouldOptimize;
        }

        public override void Close()
        {
            OptimizeBeforeClose();
            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                OptimizeBeforeClose();

            base.Dispose(disposing);
        }

        private void OptimizeBeforeClose()
        {
            if (_hasOptimized || !_shouldOptimize() || State != System.Data.ConnectionState.Open)
                return;

            _hasOptimized = true;
            try
            {
                using var cmd = CreateCommand();
                cmd.CommandText = "PRAGMA optimize;";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Maintenance must never block app shutdown or repository disposal.
            }
            catch (ObjectDisposedException)
            {
                // Dispose can race with shutdown paths; skip maintenance rather than surfacing noise.
            }
            catch (InvalidOperationException)
            {
                // Connection state can change during shutdown; best effort is enough.
            }
        }
    }
}

public sealed record DatabaseIntegrityReport(
    bool IsOk,
    IReadOnlyList<string> Messages,
    string? ErrorMessage)
{
    public static DatabaseIntegrityReport Ok { get; } = new(true, ["ok"], null);

    public string Summary
    {
        get
        {
            if (IsOk)
                return "SQLite integrity check passed.";
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
                return $"SQLite integrity check could not complete: {ErrorMessage}";

            return "SQLite integrity check reported: " + string.Join("; ", Messages);
        }
    }
}
