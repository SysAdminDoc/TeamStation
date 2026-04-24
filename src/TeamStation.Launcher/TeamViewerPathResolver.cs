using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TeamStation.Launcher;

/// <summary>
/// Locates <c>TeamViewer.exe</c> on disk by probing the registry and standard
/// install paths in the order documented in <c>docs/teamviewer-reference.md</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TeamViewerPathResolver
{
    private static readonly string[] RegistryPaths =
    [
        @"SOFTWARE\TeamViewer",
        @"SOFTWARE\WOW6432Node\TeamViewer",
    ];

    public static string? Resolve()
    {
        foreach (var baseKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var subPath in RegistryPaths)
            {
                using var key = baseKey.OpenSubKey(subPath);
                if (key?.GetValue("InstallationDirectory") is not string dir) continue;
                var candidate = Path.Combine(dir, "TeamViewer.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var root in new[] { pf, pfx86 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var direct = Path.Combine(root, "TeamViewer", "TeamViewer.exe");
            if (File.Exists(direct)) return direct;

            // Legacy layout: .../TeamViewer/VersionN/TeamViewer.exe — highest version wins.
            // Sort numerically so "Version10" ranks above "Version9" (string-sort
            // would lexically place "Version9" first).
            var parent = Path.Combine(root, "TeamViewer");
            if (!Directory.Exists(parent)) continue;

            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(parent, "Version*"); }
            catch { continue; } // Access denied, etc.

            var versioned = dirs
                .Select(d => new { Dir = d, Exe = Path.Combine(d, "TeamViewer.exe") })
                .Where(x => File.Exists(x.Exe))
                .OrderByDescending(x => VersionSuffix(x.Dir))
                .FirstOrDefault();
            if (versioned is not null) return versioned.Exe;
        }

        return null;
    }

    private static int VersionSuffix(string directory)
    {
        var name = Path.GetFileName(directory);
        if (string.IsNullOrEmpty(name) || !name.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
            return -1;
        return int.TryParse(name[7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }
}
