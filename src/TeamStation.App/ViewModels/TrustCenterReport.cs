using System.Collections.Immutable;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

/// <summary>
/// Immutable snapshot of TeamStation's local trust posture at a point in time.
/// Built by <see cref="TrustCenterReportFactory.Build"/> from raw probe inputs
/// (file metadata, settings, version detection, CVE registry, binary
/// provenance) so the same report can drive the Trust Center dialog and a
/// future Evidence Pack export without re-probing the system.
/// </summary>
/// <remarks>
/// Every section deliberately distinguishes <i>configured-but-stale</i>,
/// <i>not configured</i>, and <i>unknown</i> states. Calm sysadmin copy is
/// the design rule — no exclamations, no scare colours unless the state is
/// genuinely actionable. The Trust Center never blocks launch.
/// </remarks>
public sealed record TrustCenterReport(
    DateTimeOffset GeneratedAt,
    TrustCenterSafetySection Safety,
    TrustCenterProvenanceSection Provenance,
    TrustCenterDatabaseSection Database,
    TrustCenterMirrorSection Mirror,
    TrustCenterRegistrySection Registry,
    TrustCenterWebApiSection WebApi,
    TrustCenterUsePolicySection UsePolicy);

/// <summary>
/// TeamViewer client + bundled CVE registry verdict. Wraps the existing
/// <see cref="TeamViewerSafetyStatus"/> so the dialog can render its own
/// short headline alongside the per-CVE list.
/// </summary>
public sealed record TrustCenterSafetySection(
    TeamViewerSafetyStatus Status,
    string Headline,
    TrustCenterTone Tone)
{
    public bool HasMatches => !Status.MatchedCves.IsDefaultOrEmpty && Status.MatchedCves.Length > 0;
    public IReadOnlyList<TeamViewerCveEntry> MatchedCves => Status.MatchedCves;
    public string? FixedInRecommendation => Status.RecommendedMinimumSafeVersion?.ToString();
}

/// <summary>
/// <c>TeamViewer.exe</c> path / Authenticode / publisher / install-root verdict.
/// </summary>
public sealed record TrustCenterProvenanceSection(
    TeamViewerBinaryProvenance Provenance,
    string Headline,
    TrustCenterTone Tone)
{
    public string Path => Provenance.Path ?? "(not detected)";
    public string FileVersion => Provenance.FileVersion ?? "(unknown)";
    public string PublisherSubject => Provenance.PublisherSubject ?? "(no signature)";
    public string SignatureStateText => Provenance.SignatureState switch
    {
        TeamViewerSignatureState.Trusted => "Trusted (Authenticode)",
        TeamViewerSignatureState.Untrusted => "Untrusted",
        TeamViewerSignatureState.Unsigned => "Unsigned",
        TeamViewerSignatureState.UnableToVerify => "Unable to verify",
        TeamViewerSignatureState.NotApplicable => "Not applicable",
        _ => Provenance.SignatureState.ToString(),
    };
    public string InstallRootText => Provenance.IsUnderExpectedInstallRoot
        ? "Standard Program Files install root"
        : "Outside standard install root";
}

/// <summary>
/// Local SQLite database health. Reports path, last-write age, file size, and
/// the storage mode (per-user encrypted vs portable Argon2id-wrapped).
/// </summary>
public sealed record TrustCenterDatabaseSection(
    string Path,
    bool Exists,
    long SizeBytes,
    DateTimeOffset? LastWrite,
    bool PortableMode,
    string Headline,
    string SizeText,
    string LastWriteText,
    TrustCenterTone Tone);

/// <summary>
/// Optional encrypted-mirror folder freshness. <see cref="Configured"/> is
/// false when the user has not opted in; the dialog renders a calm
/// "no mirror configured" line in that case rather than a warning.
/// </summary>
public sealed record TrustCenterMirrorSection(
    bool Configured,
    string? Folder,
    string? MirrorFile,
    DateTimeOffset? LastWrite,
    string Headline,
    string DetailText,
    TrustCenterTone Tone);

/// <summary>
/// Bundled CVE registry metadata. Surfaces the source string,
/// <c>last_updated</c> stamp, entry count, and any load diagnostics so the
/// operator can see exactly which advisory dataset is in scope.
/// </summary>
public sealed record TrustCenterRegistrySection(
    int EntryCount,
    string LastUpdated,
    string Source,
    ImmutableArray<string> Diagnostics,
    string Headline,
    TrustCenterTone Tone)
{
    public bool HasDiagnostics => !Diagnostics.IsDefaultOrEmpty && Diagnostics.Length > 0;
}

/// <summary>
/// TeamViewer Web API token presence (never the value). Exists to make the
/// "is cloud sync configured?" answer obvious without leaving the Trust
/// Center surface.
/// </summary>
public sealed record TrustCenterWebApiSection(
    bool TokenConfigured,
    string Headline,
    string DetailText,
    TrustCenterTone Tone);

/// <summary>
/// Local operating posture for abuse-resistance and enterprise review. The
/// section records TeamStation's boundaries rather than probing the host.
/// Keeping it in the report makes the same trust summary available to the
/// dialog and future Evidence Pack exports.
/// </summary>
public sealed record TrustCenterUsePolicySection(
    string Headline,
    string DetailText,
    ImmutableArray<string> Safeguards,
    TrustCenterTone Tone)
{
    public IReadOnlyList<string> SafeguardList => Safeguards;
}

/// <summary>
/// Visual tone used by the dialog to colour each section's status pill.
/// Deliberately small — the Trust Center is calm, not alarmist.
/// </summary>
public enum TrustCenterTone
{
    /// <summary>Healthy / green-light state.</summary>
    Healthy,
    /// <summary>Configured but worth checking — yellow.</summary>
    Caution,
    /// <summary>Actionable issue — red. Reserved for matched CVEs and missing TeamViewer.</summary>
    Action,
    /// <summary>Informational / not configured. Subtext-coloured pill.</summary>
    Info,
}
