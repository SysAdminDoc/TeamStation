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
    private CryptoService _crypto;

    public EntryRepository(Database db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    /// <summary>
    /// Replaces the active <see cref="CryptoService"/> after a successful DEK
    /// rotation. Called once by the rotation coordinator immediately after the
    /// migrator commits the re-encrypted rows and before the new DEK is promoted
    /// in the key store. The old service is already disposed by
    /// <see cref="CryptoService.RotateDek"/>; subsequent reads and writes use
    /// the new service automatically.
    /// </summary>
    public void UpdateCrypto(CryptoService newCrypto)
    {
        ArgumentNullException.ThrowIfNull(newCrypto);
        _crypto = newCrypto;
    }

    public IReadOnlyList<ConnectionEntry> GetAll()
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectSql + " ORDER BY is_pinned DESC, last_connected DESC, name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        var list = new List<ConnectionEntry>();
        while (reader.Read()) list.Add(Materialize(reader));
        return list;
    }

    /// <summary>
    /// Loads ONLY the encrypted password column for an entry and returns
    /// the cleartext as a fresh <see cref="byte"/> array of UTF-8. The
    /// caller owns the buffer and is expected to zero it via
    /// <see cref="System.Security.Cryptography.CryptographicOperations.ZeroMemory(System.Span{byte})"/>
    /// once the password is no longer needed. Returns <c>null</c> when the
    /// entry has no stored password or the entry id does not exist.
    /// </summary>
    /// <remarks>
    /// New in v0.3.4. Used by the launch hot path so the password lives as
    /// a zeroable byte buffer instead of a CLR-interned string. Does NOT
    /// touch <see cref="ConnectionEntry.Password"/>; the caller is
    /// responsible for any string-typed cache it keeps for the UI layer.
    /// </remarks>
    public byte[]? LoadEntryPasswordBytes(Guid entryId)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT password_enc FROM entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", entryId.ToString("D"));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return null;
        var ciphertext = (byte[])reader["password_enc"];
        return _crypto.DecryptToBytes(ciphertext);
    }

    /// <summary>
    /// Loads ONLY the encrypted proxy-password column for an entry and
    /// returns the cleartext as a fresh zeroable byte buffer. Returns
    /// <c>null</c> when the entry has no proxy or the proxy has no stored
    /// password.
    /// </summary>
    public byte[]? LoadEntryProxyPasswordBytes(Guid entryId)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT proxy_pass_enc FROM entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", entryId.ToString("D"));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0)) return null;
        var ciphertext = (byte[])reader["proxy_pass_enc"];
        return _crypto.DecryptToBytes(ciphertext);
    }

    public ConnectionEntry? Get(Guid id)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Materialize(reader) : null;
    }

    /// <summary>
    /// Inserts a new entry or updates an existing one in place. The
    /// <c>created_utc</c> column is written only on INSERT — the UPDATE clause
    /// intentionally omits it so edits never revise the creation timestamp.
    /// This method mutates <paramref name="entry"/>.<see cref="ConnectionEntry.ModifiedUtc"/>
    /// to the current UTC time.
    /// </summary>
    public void Upsert(ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.ModifiedUtc = DateTimeOffset.UtcNow;

        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO entries(
    id, parent_folder_id, name, tv_id, profile_name, password_enc, mode, quality, access_control,
    proxy_host, proxy_port, proxy_user, proxy_pass_enc,
    notes, tags_csv, last_connected, tv_path_override, is_pinned,
    wake_mac, wake_broadcast, pre_launch_script, post_launch_script,
    created_utc, modified_utc)
VALUES (
    $id, $parent, $name, $tv_id, $profile, $pw, $mode, $quality, $ac,
    $ph, $pp, $pu, $ppw,
    $notes, $tags, $last, $tv_path, $pinned,
    $wake_mac, $wake_broadcast, $pre_script, $post_script,
    $created, $modified)
ON CONFLICT(id) DO UPDATE SET
    parent_folder_id = excluded.parent_folder_id,
    name             = excluded.name,
    tv_id            = excluded.tv_id,
    profile_name     = excluded.profile_name,
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
    tv_path_override = excluded.tv_path_override,
    is_pinned        = excluded.is_pinned,
    wake_mac         = excluded.wake_mac,
    wake_broadcast   = excluded.wake_broadcast,
    pre_launch_script = excluded.pre_launch_script,
    post_launch_script = excluded.post_launch_script,
    modified_utc     = excluded.modified_utc;";
        cmd.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$parent", (object?)entry.ParentFolderId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", entry.Name);
        cmd.Parameters.AddWithValue("$tv_id", entry.TeamViewerId);
        cmd.Parameters.AddWithValue("$profile", string.IsNullOrWhiteSpace(entry.ProfileName) ? "Default" : entry.ProfileName.Trim());
        cmd.Parameters.AddWithValue("$pw", (object?)_crypto.EncryptString(entry.Password) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mode", entry.Mode is null ? DBNull.Value : (object)(int)entry.Mode);
        cmd.Parameters.AddWithValue("$quality", entry.Quality is null ? DBNull.Value : (object)(int)entry.Quality);
        cmd.Parameters.AddWithValue("$ac", entry.AccessControl is null ? DBNull.Value : (object)(int)entry.AccessControl);
        cmd.Parameters.AddWithValue("$ph", (object?)entry.Proxy?.Host ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pp", (object?)entry.Proxy?.Port ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pu", (object?)entry.Proxy?.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ppw", (object?)_crypto.EncryptString(entry.Proxy?.Password) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)entry.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", entry.Tags.Count == 0 ? (object)DBNull.Value : string.Join(',', entry.Tags));
        cmd.Parameters.AddWithValue("$last", (object?)entry.LastConnectedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tv_path", (object?)entry.TeamViewerPathOverride ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pinned", entry.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$wake_mac", (object?)entry.WakeMacAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wake_broadcast", (object?)entry.WakeBroadcastAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pre_script", (object?)entry.PreLaunchScript ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$post_script", (object?)entry.PostLaunchScript ?? DBNull.Value);
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

    private const string SelectSql = @"
SELECT id, parent_folder_id, name, tv_id, profile_name, password_enc, mode, quality, access_control,
       proxy_host, proxy_port, proxy_user, proxy_pass_enc,
       notes, tags_csv, last_connected, tv_path_override, is_pinned,
       wake_mac, wake_broadcast, pre_launch_script, post_launch_script,
       created_utc, modified_utc
FROM entries";

    private ConnectionEntry Materialize(SqliteDataReader r)
    {
        // Read the row into a single ConnectionEntry. `CreatedUtc` is
        // init-only on the model, so we set it through the object initializer
        // here rather than allocating a placeholder and cloning.
        ProxySettings? proxy = null;
        if (!r.IsDBNull(9) && !r.IsDBNull(10))
        {
            proxy = new ProxySettings(
                Host: r.GetString(9),
                Port: r.GetInt32(10),
                Username: r.IsDBNull(11) ? null : r.GetString(11),
                Password: r.IsDBNull(12) ? null : _crypto.DecryptString((byte[])r["proxy_pass_enc"]));
        }

        return new ConnectionEntry
        {
            Id = Guid.Parse(r.GetString(0)),
            ParentFolderId = r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
            Name = r.GetString(2),
            TeamViewerId = r.GetString(3),
            ProfileName = r.IsDBNull(4) ? "Default" : r.GetString(4),
            Password = r.IsDBNull(5) ? null : _crypto.DecryptString((byte[])r["password_enc"]),
            Mode = r.IsDBNull(6) ? null : (ConnectionMode?)r.GetInt32(6),
            Quality = r.IsDBNull(7) ? null : (ConnectionQuality?)r.GetInt32(7),
            AccessControl = r.IsDBNull(8) ? null : (AccessControl?)r.GetInt32(8),
            Proxy = proxy,
            Notes = r.IsDBNull(13) ? null : r.GetString(13),
            Tags = r.IsDBNull(14)
                ? new List<string>()
                : r.GetString(14).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            LastConnectedUtc = r.IsDBNull(15) ? null : DateTimeOffset.Parse(r.GetString(15), CultureInfo.InvariantCulture),
            TeamViewerPathOverride = r.IsDBNull(16) ? null : r.GetString(16),
            IsPinned = !r.IsDBNull(17) && r.GetInt32(17) != 0,
            WakeMacAddress = r.IsDBNull(18) ? null : r.GetString(18),
            WakeBroadcastAddress = r.IsDBNull(19) ? null : r.GetString(19),
            PreLaunchScript = r.IsDBNull(20) ? null : r.GetString(20),
            PostLaunchScript = r.IsDBNull(21) ? null : r.GetString(21),
            CreatedUtc = DateTimeOffset.Parse(r.GetString(22), CultureInfo.InvariantCulture),
            ModifiedUtc = DateTimeOffset.Parse(r.GetString(23), CultureInfo.InvariantCulture),
        };
    }
}
