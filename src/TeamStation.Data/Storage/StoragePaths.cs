using System.Runtime.Versioning;

namespace TeamStation.Data.Storage;

/// <summary>
/// Resolves the on-disk location of the TeamStation database.
///
/// Portable mode: activated when a file named <c>teamstation.portable</c>
/// sits next to the executable; DB lives alongside the exe. Otherwise the
/// DB is stored under <c>%LocalAppData%\TeamStation\</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StoragePaths
{
    public const string DatabaseFileName = "teamstation.db";
    public const string PortableMarkerFileName = "teamstation.portable";

    public static string ResolveDatabasePath(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        if (IsPortable(out var exeDir))
            return Path.Combine(exeDir, DatabaseFileName);

        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var dir = Path.Combine(root, "TeamStation");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, DatabaseFileName);
    }

    public static bool IsPortable(out string exeDirectory)
    {
        exeDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var marker = Path.Combine(exeDirectory, PortableMarkerFileName);
        return File.Exists(marker);
    }
}
