using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TeamStation.Launcher;

/// <summary>
/// Reads the installed TeamViewer client version from the registry (with the
/// usual <c>WOW6432Node</c> mirror fallback for 32-bit installs on 64-bit
/// Windows) and falls back to <see cref="FileVersionInfo"/> on the resolved
/// <c>TeamViewer.exe</c> path. Used by the v0.3.5 status-bar pill that
/// surfaces "update available" when the detected version is below the
/// minimum-known-safe baseline (CVE-2026-23572 — TeamViewer 15.74.5+).
/// </summary>
/// <remarks>
/// <para>
/// TeamStation orchestrates the installed TeamViewer client and does NOT
/// ship the protocol implementation, so the v0.3.5 surfacing is operator-
/// remediation guidance only — there is no auto-update path.
/// </para>
/// <para>
/// The detector is decoupled from <see cref="Registry"/> via the
/// <see cref="ITeamViewerVersionSource"/> seam so unit tests can pump fake
/// versions without touching a real registry hive.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class TeamViewerVersionDetector
{
    /// <summary>
    /// Minimum version known to incorporate the CVE-2026-23572 (auth-bypass)
    /// patch. Below this baseline the status bar surfaces an "update
    /// available" pill. Per the April 2026 TeamViewer security bulletin.
    /// </summary>
    public static readonly Version MinimumSafeVersion = new(15, 74, 5);

    /// <summary>
    /// Detects the installed TeamViewer version against the default sources
    /// (registry + resolved exe path). Returns <c>null</c> when no version
    /// can be parsed — the caller (status bar) should render
    /// "TeamViewer not detected" in that case.
    /// </summary>
    public static Version? Detect() => Detect(DefaultSource);

    /// <summary>
    /// Test-friendly overload — accepts an <see cref="ITeamViewerVersionSource"/>
    /// so unit tests can supply a fake.
    /// </summary>
    public static Version? Detect(ITeamViewerVersionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var raw = source.ReadVersionString();
        return TryParseVersion(raw, out var version) ? version : null;
    }

    /// <summary>
    /// Returns true when <paramref name="version"/> is below
    /// <see cref="MinimumSafeVersion"/>. <c>null</c> input returns false —
    /// "version unknown" is not the same as "version unsafe", and a missing
    /// TeamViewer install is rendered with its own "not detected" message.
    /// </summary>
    public static bool NeedsUpdate(Version? version) =>
        version is not null && version < MinimumSafeVersion;

    /// <summary>
    /// TeamViewer ships its installed version as a 4-component string
    /// (15.71.5.0 or 15.71.5). <c>System.Version.TryParse</c> handles both,
    /// but we additionally reject empty input and string forms that don't
    /// start with a major version.
    /// </summary>
    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        // Trim trailing whitespace + '.0' tail noise occasionally seen in
        // OEM/manual-install registry rows.
        var trimmed = raw.Trim();
        return Version.TryParse(trimmed, out version!);
    }

    private static readonly ITeamViewerVersionSource DefaultSource = new RegistryAndFileVersionSource();

    private sealed class RegistryAndFileVersionSource : ITeamViewerVersionSource
    {
        public string? ReadVersionString()
        {
            // 1) Registry: HKLM\SOFTWARE\TeamViewer\Version
            //              HKLM\SOFTWARE\WOW6432Node\TeamViewer\Version
            //              HKCU mirror (rare but seen on per-user installs)
            foreach (var baseKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
            foreach (var subPath in new[] { @"SOFTWARE\TeamViewer", @"SOFTWARE\WOW6432Node\TeamViewer" })
            {
                using var key = baseKey.OpenSubKey(subPath);
                if (key?.GetValue("Version") is string version && !string.IsNullOrWhiteSpace(version))
                    return version;
            }

            // 2) FileVersionInfo on the resolved TeamViewer.exe.
            var exe = TeamViewerPathResolver.Resolve();
            if (exe is null) return null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                return info.FileVersion ?? info.ProductVersion;
            }
            catch
            {
                return null;
            }
        }
    }
}

/// <summary>
/// Test-friendly version-source seam. Implementations return the raw
/// <see cref="string"/> as it would appear in the registry value or
/// <see cref="FileVersionInfo.FileVersion"/> — parsing happens in
/// <see cref="TeamViewerVersionDetector.TryParseVersion(string?, out Version)"/>.
/// </summary>
public interface ITeamViewerVersionSource
{
    string? ReadVersionString();
}
