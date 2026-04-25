using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TeamStation.Launcher;

/// <summary>
/// Reads the installed TeamViewer client version from the registry (with the
/// usual <c>WOW6432Node</c> mirror fallback for 32-bit installs on 64-bit
/// Windows) and falls back to <see cref="FileVersionInfo"/> on the resolved
/// <c>TeamViewer.exe</c> path. Used by the v0.3.5 status-bar pill that
/// surfaces "update available" when the detected version matches an entry in
/// the bundled offline <see cref="TeamViewerCveRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// TeamStation orchestrates the installed TeamViewer client and does NOT
/// ship the protocol implementation, so the surfacing is operator-
/// remediation guidance only — there is no auto-update path and no network
/// call.
/// </para>
/// <para>
/// The detector is decoupled from <see cref="Registry"/> via the
/// <see cref="ITeamViewerVersionSource"/> seam so unit tests can pump fake
/// versions without touching a real registry hive. v0.4.0 generalised the
/// hardcoded <c>15.74.5</c> minimum into a registry-driven evaluation;
/// <see cref="MinimumSafeVersion"/> now reflects the lowest <c>fixed_in</c>
/// across every entry in the bundled registry, with a static fallback so
/// existing call sites continue to work even if the registry fails to load.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class TeamViewerVersionDetector
{
    /// <summary>
    /// Backstop value used when the bundled <see cref="TeamViewerCveRegistry"/>
    /// fails to load (e.g. the embedded resource was stripped from a custom
    /// build). Reflects the CVE-2026-23572 baseline at v0.4.0 release time.
    /// Prefer <see cref="MinimumSafeVersion"/> for runtime decisions —
    /// it consults the registry first and only falls back to this constant.
    /// </summary>
    public static readonly Version FallbackMinimumSafeVersion = new(15, 74, 5);

    /// <summary>
    /// Highest <c>fixed_in</c> across the bundled CVE registry, or
    /// <see cref="FallbackMinimumSafeVersion"/> if the registry could not be
    /// loaded or carries no entries with <c>fixed_in</c> declared. The
    /// "needs update" decision is registry-driven via
    /// <see cref="TeamViewerSafetyEvaluator"/>; this property exists so older
    /// log lines and tooltips can still print a single threshold.
    /// </summary>
    public static Version MinimumSafeVersion =>
        TeamViewerCveRegistry.Default.RecommendedMinimumSafeVersion() ?? FallbackMinimumSafeVersion;

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
    /// Returns true when <paramref name="version"/> matches a vulnerable
    /// range in the bundled CVE registry. <c>null</c> input returns false —
    /// "version unknown" is not the same as "version unsafe", and a missing
    /// TeamViewer install is rendered with its own "not detected" message.
    /// Backward-compatible shim around <see cref="TeamViewerSafetyEvaluator"/>.
    /// </summary>
    public static bool NeedsUpdate(Version? version) =>
        TeamViewerSafetyEvaluator.Evaluate(version, TeamViewerCveRegistry.Default).NeedsUpdate;

    /// <summary>
    /// Full safety status against the bundled registry. Callers that need
    /// the matched CVEs (status-bar tooltip, Trust Center) should use this
    /// rather than <see cref="NeedsUpdate"/>.
    /// </summary>
    public static TeamViewerSafetyStatus EvaluateSafety(Version? version) =>
        TeamViewerSafetyEvaluator.Evaluate(version, TeamViewerCveRegistry.Default);

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
