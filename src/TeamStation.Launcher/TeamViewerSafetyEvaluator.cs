using System.Collections.Immutable;

namespace TeamStation.Launcher;

/// <summary>
/// Pure evaluation of a detected installed TeamViewer client version against
/// the bundled <see cref="TeamViewerCveRegistry"/>. Decoupled from
/// <see cref="TeamViewerVersionDetector"/> so unit tests can exercise the
/// matching logic without touching a registry hive, and so the same
/// evaluation drives the status-bar pill, the log line, and any future
/// Trust Center surface.
/// </summary>
public static class TeamViewerSafetyEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="version"/> against
    /// <paramref name="registry"/>. <paramref name="registry"/> may be null —
    /// in that case the result is <see cref="TeamViewerSafetyState.Unknown"/>
    /// (registry unavailable) rather than <see cref="TeamViewerSafetyState.Safe"/>,
    /// because "we have no data" is not the same as "we know it's safe".
    /// </summary>
    public static TeamViewerSafetyStatus Evaluate(Version? version, TeamViewerCveRegistry? registry)
    {
        if (version is null)
        {
            return new TeamViewerSafetyStatus(
                TeamViewerSafetyState.NotDetected,
                Version: null,
                MatchedCves: ImmutableArray<TeamViewerCveEntry>.Empty,
                RecommendedMinimumSafeVersion: registry?.RecommendedMinimumSafeVersion());
        }

        if (registry is null)
        {
            return new TeamViewerSafetyStatus(
                TeamViewerSafetyState.Unknown,
                version,
                ImmutableArray<TeamViewerCveEntry>.Empty,
                RecommendedMinimumSafeVersion: null);
        }

        var matches = registry.Match(version);
        if (matches.IsDefaultOrEmpty)
        {
            return new TeamViewerSafetyStatus(
                TeamViewerSafetyState.Safe,
                version,
                ImmutableArray<TeamViewerCveEntry>.Empty,
                RecommendedMinimumSafeVersion: registry.RecommendedMinimumSafeVersion());
        }

        return new TeamViewerSafetyStatus(
            TeamViewerSafetyState.Vulnerable,
            version,
            matches,
            RecommendedMinimumSafeVersion: registry.RecommendedMinimumSafeVersion());
    }
}

/// <summary>
/// High-level safety states that drive the status-bar surface. Distinct
/// states matter so the UI can pick calm copy for each: <c>Safe</c> ->
/// version chip only, <c>Unknown</c> -> version chip only with a tooltip
/// saying the registry is unavailable, <c>Vulnerable</c> -> yellow pill,
/// <c>NotDetected</c> -> "TeamViewer not detected" with no pill.
/// </summary>
public enum TeamViewerSafetyState
{
    /// <summary>No installed TeamViewer client could be detected.</summary>
    NotDetected,

    /// <summary>Detected but the registry was unavailable to evaluate against.</summary>
    Unknown,

    /// <summary>Detected and matches no entries in the loaded registry.</summary>
    Safe,

    /// <summary>Detected and matches one or more registry entries.</summary>
    Vulnerable,
}

/// <summary>
/// Result of <see cref="TeamViewerSafetyEvaluator.Evaluate"/>. Holds enough
/// detail for the status bar to render the version chip, the
/// "Update available" pill, and the per-CVE tooltip.
/// </summary>
public sealed record TeamViewerSafetyStatus(
    TeamViewerSafetyState State,
    Version? Version,
    ImmutableArray<TeamViewerCveEntry> MatchedCves,
    Version? RecommendedMinimumSafeVersion)
{
    /// <summary>
    /// True when the status bar should render the yellow "Update available"
    /// pill. Drives the existing
    /// <c>MainViewModel.TeamViewerNeedsUpdate</c> binding.
    /// </summary>
    public bool NeedsUpdate => State == TeamViewerSafetyState.Vulnerable;

    /// <summary>
    /// Operator-facing chip text, e.g. <c>"TeamViewer 15.71.5"</c> or
    /// <c>"TeamViewer not detected"</c>. Calm sysadmin copy — no exclamations.
    /// </summary>
    public string ChipText => State switch
    {
        TeamViewerSafetyState.NotDetected => "TeamViewer not detected",
        _ when Version is not null => $"TeamViewer {Version}",
        _ => "TeamViewer not detected",
    };

    /// <summary>
    /// Tooltip rendered on hover over the version chip / pill. For vulnerable
    /// versions, lists every matched CVE with its fixed-in version and
    /// remediation URL, so the operator can act without leaving the app.
    /// </summary>
    public string TooltipText
    {
        get
        {
            switch (State)
            {
                case TeamViewerSafetyState.NotDetected:
                    return "TeamViewer client could not be located. Install TeamViewer or set the path in Settings.";
                case TeamViewerSafetyState.Unknown:
                    return $"TeamViewer {Version} detected. CVE registry unavailable, so no safety advisory was matched.";
                case TeamViewerSafetyState.Safe:
                    return RecommendedMinimumSafeVersion is null
                        ? $"TeamViewer {Version} detected. No matches in the bundled CVE registry."
                        : $"TeamViewer {Version} detected (>= {RecommendedMinimumSafeVersion} bundled-registry baseline).";
                case TeamViewerSafetyState.Vulnerable:
                    var lines = new List<string>
                    {
                        $"TeamViewer {Version} matches {MatchedCves.Length} advisory item{(MatchedCves.Length == 1 ? "" : "s")} in the bundled, offline CVE registry."
                    };
                    foreach (var cve in MatchedCves)
                    {
                        var fix = cve.FixedIn is null ? "" : $" — fixed in {cve.FixedIn}";
                        lines.Add($"\u2022 {cve.Id}{fix}: {cve.Title}");
                        if (!string.IsNullOrWhiteSpace(cve.RemediationUrl))
                            lines.Add($"  {cve.RemediationUrl}");
                    }
                    lines.Add("Updating the installed TeamViewer client when convenient is recommended. TeamStation does not auto-update TeamViewer.");
                    return string.Join("\n", lines);
                default:
                    return string.Empty;
            }
        }
    }
}
