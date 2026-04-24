using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;

namespace TeamStation.Data.Storage;

[SupportedOSPlatform("windows")]
public sealed class EntryRepository
{
    private readonly Database _db;
    private readonly CryptoService _crypto;

    public EntryRepository(Database db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    public IReadOnlyList<ConnectionEntry> GetAll()
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, parent_folder_id, name, tv_id, password_enc, mode, quality, access_control,
       proxy_host, proxy_port, proxy_user, proxy_pass_enc,
       notes, tags_csv, last_connected, created_utc, modified_utc
FROM entries
ORDER BY name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        var list = new List<ConnectionEntry>();
        while (reader.Read()) list.Add(Materialize(reader));
        return list;
    }

    public ConnectionEntry? Get(Guid id)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, parent_folder_id, name, tv_id, password_enc, mode, quality, access_control,
       proxy_host, proxy_port, proxy_user, proxy_pass_enc,
       notes, tags_csv, last_connected, created_utc, modified_utc
FROM entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Materialize(reader) : null;
    }

    public void Upsert(ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ModifiedUtc = DateTimeOffset.UtcNow;

        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO entries(
    id, parent_folder_id, name, tv_id, password_enc, mode, quality, access_control,
    proxy_host, proxy_port, proxy_user, proxy_pass_enc,
    notes, tags_csv, last_connected, created_utc, modified_utc)
VALUES (
    $id, $parent, $name, $tv_id, $pw, $mode, $quality, $ac,
    $ph, $pp, $pu, $ppw,
    $notes, $tags, $last, $created, $modified)
ON CONFLICT(id) DO UPDATE SET
    parent_folder_id = excluded.parent_folder_id,
    name             = excluded.name,
    tv_id            = excluded.tv_id,
    password_enc     = excluded.password_enc,
    mode             = excluded.mode,
    quality          = excluded.quality,
    access_control   = excluded.access_control,
    proxy_host       = excluded.proxy_host,
    proxy_port       = excluded.proxy_port,
    proxy_user       = excluded.proxy_user,
    proxy_pass_enc   = excluded.proxy_pass_enc,
    notes            = excluded.notes,
    tags_csv         = excluded.tags_csv,
    last_connected   = excluded.last_connected,
    modified_utc     = excluded.modified_utc;";
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$parent", (object?)entry.ParentFolderId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", entry.Name);
        cmd.Parameters.AddWithValue("$tv_id", entry.TeamViewerId);
        cmd.Parameters.AddWithValue("$pw", (object?)_crypto.EncryptString(entry.Password) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", (int)entry.Mode);
        cmd.Parameters.AddWithValue("$quality", (int)entry.Quality);
        cmd.Parameters.AddWithValue("$ac", (int)entry.AccessControl);
        cmd.Parameters.AddWithValue("$ph", (object?)entry.Proxy?.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pp", (object?)entry.Proxy?.Port ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pu", (object?)entry.Proxy?.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ppw", (object?)_crypto.EncryptString(entry.Proxy?.Password) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", entry.Tags.Count == 0 ? (object)DBNull.Value : string.Join(',', entry.Tags));
        cmd.Parameters.AddWithValue("$last", (object?)entry.LastConnectedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", entry.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$modified", entry.ModifiedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public void TouchLastConnected(Guid id, DateTimeOffset when)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE entries SET last_connected = $when, modified_utc = $when WHERE id = $id;";
        cmd.Parameters.AddWithValue("$when", when.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private ConnectionEntry Materialize(SqliteDataReader r)
    {
        var entry = new ConnectionEntry
        {
            Id = Guid.Parse(r.GetString(0)),
            ParentFolderId = r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
            Name = r.GetString(2),
            TeamViewerId = r.GetString(3),
            Password = r.IsDBNull(4) ? null : _crypto.DecryptString((byte[])r["password_enc"]),
            Mode = (ConnectionMode)r.GetInt32(5),
            Quality = (ConnectionQuality)r.GetInt32(6),
            AccessControl = (AccessControl)r.GetInt32(7),
            Notes = r.IsDBNull(12) ? null : r.GetString(12),
            Tags = r.IsDBNull(13)
                ? new List<string>()
                : r.GetString(13).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            LastConnectedUtc = r.IsDBNull(14) ? null : DateTimeOffset.Parse(r.GetString(14), CultureInfo.InvariantCulture),
        };

        if (!r.IsDBNull(8) && !r.IsDBNull(9))
        {
            entry.Proxy = new ProxySettings(
                Host: r.GetString(8),
                Port: r.GetInt32(9),
                Username: r.IsDBNull(10) ? null : r.GetString(10),
                Password: r.IsDBNull(11) ? null : _crypto.DecryptString((byte[])r["proxy_pass_enc"]));
        }

        // CreatedUtc is init-only; assign via reflection-free path by rebuilding object
        var createdUtc = DateTimeOffset.Parse(r.GetString(15), CultureInfo.InvariantCulture);
        var modifiedUtc = DateTimeOffset.Parse(r.GetString(16), CultureInfo.InvariantCulture);
        return CloneWithTimestamps(entry, createdUtc, modifiedUtc);
    }

    private static ConnectionEntry CloneWithTimestamps(ConnectionEntry src, DateTimeOffset created, DateTimeOffset modified)
    {
        // CreatedUtc has init-only setter — clone via object initializer to respect it
        return new ConnectionEntry
        {
            Id = src.Id,
            ParentFolderId = src.ParentFolderId,
            Name = src.Name,
            TeamViewerId = src.TeamViewerId,
            Password = src.Password,
            Mode = src.Mode,
            Quality = src.Quality,
            AccessControl = src.AccessControl,
            Proxy = src.Proxy,
            Notes = src.Notes,
            Tags = src.Tags,
            LastConnectedUtc = src.LastConnectedUtc,
            CreatedUtc = created,
            ModifiedUtc = modified,
        };
    }
}
