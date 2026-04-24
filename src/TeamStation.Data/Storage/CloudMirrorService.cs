using System.IO;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;

namespace TeamStation.Data.Storage;

/// <summary>
/// Mirrors the TeamStation SQLite database to a user-selected cloud sync
/// folder (OneDrive, Dropbox, Syncthing, etc). This is a backup mechanism,
/// not a multi-writer sync engine.
/// </summary>
/// <remarks>
/// <para>
/// A naive <see cref="File.Copy"/> of a live WAL-mode SQLite file plus its
/// <c>-wal</c> and <c>-shm</c> sidecars is <b>not safe</b>: another writer
/// can checkpoint mid-copy and leave the mirror referencing a WAL that no
/// longer matches the main file. Opening the mirror later can yield
/// "database disk image is malformed".
/// </para>
/// <para>
/// We instead open a brand-new read-only SQLite connection against the
/// source and let SQLite produce a consistent copy via <c>VACUUM INTO</c>.
/// That acquires a shared read lock for the duration of the copy, holds a
/// transaction open, and writes a single self-consistent file with no
/// sidecars required. The mirror file is always a fresh, clean DB — no
/// WAL residue.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class CloudMirrorService
{
    /// <summary>
    /// Writes a consistent snapshot of <paramref name="sourceDatabasePath"/>
    /// into <paramref name="destinationFolder"/>, replacing any previous mirror
    /// atomically. No-op when either argument is blank or the source is missing.
    /// </summary>
    /// <returns>The mirror file path if written; <c>null</c> if the call was a no-op.</returns>
    public static string? MirrorDatabase(string? sourceDatabasePath, string? destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabasePath) ||
            string.IsNullOrWhiteSpace(destinationFolder) ||
            !File.Exists(sourceDatabasePath))
        {
            return null;
        }

        Directory.CreateDirectory(destinationFolder);
        var finalPath = Path.Combine(destinationFolder, Path.GetFileName(sourceDatabasePath));
        var stagingPath = Path.Combine(
            destinationFolder,
            $".{Path.GetFileName(sourceDatabasePath)}.{Guid.NewGuid():N}.partial");

        if (File.Exists(stagingPath))
        {
            try { File.Delete(stagingPath); } catch { /* next step will surface the error */ }
        }

        try
        {
            using (var c = new SqliteConnection($"Data Source={sourceDatabasePath};Mode=ReadOnly"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path;";
                cmd.Parameters.AddWithValue("$path", stagingPath);
                cmd.ExecuteNonQuery();
            }

            File.Move(stagingPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
