using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;

namespace TeamStation.Data.Storage;

[SupportedOSPlatform("windows")]
public sealed class FolderRepository
{
    private readonly Database _db;
    private readonly CryptoService _crypto;

    public FolderRepository(Database db, CryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    public IReadOnlyList<Folder> GetAll()
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, parent_folder_id, name, accent_color, sort_order,
       default_mode, default_quality, default_access, default_password_enc
FROM folders
ORDER BY sort_order, name COLLATE NOCASE;";
        using var reader = cmd.ExecuteReader();
        var list = new List<Folder>();
        while (reader.Read()) list.Add(Materialize(reader));
        return list;
    }

    public void Upsert(Folder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO folders(id, parent_folder_id, name, accent_color, sort_order,
                    default_mode, default_quality, default_access, default_password_enc)
VALUES ($id, $parent, $name, $accent, $sort, $mode, $q, $ac, $pw)
ON CONFLICT(id) DO UPDATE SET
    parent_folder_id     = excluded.parent_folder_id,
    name                 = excluded.name,
    accent_color         = excluded.accent_color,
    sort_order           = excluded.sort_order,
    default_mode         = excluded.default_mode,
    default_quality      = excluded.default_quality,
    default_access       = excluded.default_access,
    default_password_enc = excluded.default_password_enc;";
        cmd.Parameters.AddWithValue("$id", folder.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$parent", (object?)folder.ParentFolderId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", folder.Name);
        cmd.Parameters.AddWithValue("$accent", (object?)folder.AccentColor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sort", folder.SortOrder);
        cmd.Parameters.AddWithValue("$mode", (object?)folder.DefaultMode is null ? DBNull.Value : (int)folder.DefaultMode);
        cmd.Parameters.AddWithValue("$q", (object?)folder.DefaultQuality is null ? DBNull.Value : (int)folder.DefaultQuality);
        cmd.Parameters.AddWithValue("$ac", (object?)folder.DefaultAccessControl is null ? DBNull.Value : (int)folder.DefaultAccessControl);
        cmd.Parameters.AddWithValue("$pw", (object?)_crypto.EncryptString(folder.DefaultPassword) ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM folders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private Folder Materialize(SqliteDataReader r)
    {
        return new Folder
        {
            Id = Guid.Parse(r.GetString(0)),
            ParentFolderId = r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
            Name = r.GetString(2),
            AccentColor = r.IsDBNull(3) ? null : r.GetString(3),
            SortOrder = r.GetInt32(4),
            DefaultMode = r.IsDBNull(5) ? null : (ConnectionMode)r.GetInt32(5),
            DefaultQuality = r.IsDBNull(6) ? null : (ConnectionQuality)r.GetInt32(6),
            DefaultAccessControl = r.IsDBNull(7) ? null : (AccessControl)r.GetInt32(7),
            DefaultPassword = r.IsDBNull(8) ? null : _crypto.DecryptString((byte[])r["default_password_enc"]),
        };
    }
}
