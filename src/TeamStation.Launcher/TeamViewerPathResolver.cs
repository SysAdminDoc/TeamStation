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

            // Legacy layout: .../TeamViewer/VersionN/TeamViewer.exe — highest version wins
            var parent = Path.Combine(root, "TeamViewer");
            if (!Directory.Exists(parent)) continue;
            var versioned = Directory.EnumerateDirectories(parent, "Version*")
                .Select(d => new { Dir = d, Exe = Path.Combine(d, "TeamViewer.exe") })
                .Where(x => File.Exists(x.Exe))
                .OrderByDescending(x => x.Dir, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (versioned is not null) return versioned.Exe;
        }

        return null;
    }
}
