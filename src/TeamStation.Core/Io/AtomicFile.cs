using System.IO;

namespace TeamStation.Core.Io;

/// <summary>
/// Small shared helper for crash-safe text-file writes. A crash or disk-full
/// mid-write cannot leave a truncated target on disk: the payload lands in a
/// sibling <c>&lt;target&gt;.&lt;guid&gt;.tmp</c> file first and only replaces
/// the target via <see cref="File.Move(string,string,bool)"/>, which is atomic
/// on Windows for paths on the same volume. If any step throws, the temp
/// sidecar is best-effort-deleted before rethrowing so repeated failures do
/// not accumulate stale <c>.tmp</c> files.
/// </summary>
/// <remarks>
/// Duplication history: the settings store and the JSON-backup export path
/// both grew private copies of this pattern during v0.1.x–v0.3.0. The helper
/// was hoisted here in v0.3.1 so behaviour stays in lock-step and the crash
/// tests exercise the single shared code path.
/// </remarks>
public static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="destination"/>
    /// via a sibling temp file. If either the write or the rename throws,
    /// the temp sidecar is removed (best-effort) and the original exception
    /// is re-surfaced so callers see the root cause, not a cleanup error.
    /// </summary>
    public static void WriteAllText(string destination, string contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(contents);

        var dir = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".";
        Directory.CreateDirectory(dir);
        // Dot-prefix plus a per-save GUID prevents two concurrent saves
        // colliding on the same sidecar and prevents the Windows indexer /
        // antivirus from occasionally grabbing a partial write.
        var temp = Path.Combine(dir, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* best-effort cleanup; surface the original exception */ }
            throw;
        }
    }

    /// <summary>
    /// Byte-oriented counterpart of <see cref="WriteAllText"/>. Same atomic
    /// semantics; useful for JSON blobs that are already serialized to
    /// UTF-8 bytes or for binary payloads.
    /// </summary>
    public static void WriteAllBytes(string destination, byte[] contents)
    {
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(contents);

        var dir = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".";
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, contents);
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch { /* best-effort cleanup; surface the original exception */ }
            throw;
        }
    }
}
