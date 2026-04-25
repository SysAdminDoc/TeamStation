using System.Collections.Immutable;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

/// <summary>
/// Pure synthesiser for <see cref="TrustCenterReport"/>. All Win32 / IO
/// probes happen in the caller (<c>TrustCenterViewModel</c>) so this class
/// can be unit-tested with synthetic inputs.
/// </summary>
public static class TrustCenterReportFactory
{
    /// <summary>
    /// Conservative freshness threshold — a mirror that has not been
    /// rewritten in this long is flagged with <see cref="TrustCenterTone.Caution"/>.
    /// </summary>
    public static readonly TimeSpan MirrorStaleThreshold = TimeSpan.FromDays(7);

    /// <summary>
    /// Builds the immutable report from already-collected inputs.
    /// </summary>
    /// <param name="now">The "current time" used for relative-age calculations. Pass <see cref="DateTimeOffset.Now"/> in production; pass a fixed value in tests.</param>
    /// <param name="safetyStatus">From <see cref="TeamViewerVersionDetector.EvaluateSafety"/>.</param>
    /// <param name="provenance">From <see cref="TeamViewerBinaryProvenanceInspector.Inspect"/>.</param>
    /// <param name="registry">Bundled CVE registry, typically <see cref="TeamViewerCveRegistry.Default"/>.</param>
    /// <param name="databasePath">Resolved local SQLite database path.</param>
    /// <param name="databaseSizeBytes">File size if the DB exists; null otherwise.</param>
    /// <param name="databaseLastWrite">Last-write timestamp if the DB exists; null otherwise.</param>
    /// <param name="portableMode">True when running with the portable marker file present.</param>
    /// <param name="cloudSyncFolder">User-configured cloud sync folder; null/blank means not configured.</param>
    /// <param name="mirrorFile">Resolved mirror file path inside the cloud sync folder; null when not yet written.</param>
    /// <param name="mirrorLastWrite">Last-write timestamp for the mirror file; null when missing.</param>
    /// <param name="webApiTokenConfigured">True when an API token is set in settings.</param>
    public static TrustCenterReport Build(
        DateTimeOffset now,
        TeamViewerSafetyStatus safetyStatus,
        TeamViewerBinaryProvenance provenance,
        TeamViewerCveRegistry registry,
        string databasePath,
        long? databaseSizeBytes,
        DateTimeOffset? databaseLastWrite,
        bool portableMode,
        string? cloudSyncFolder,
        string? mirrorFile,
        DateTimeOffset? mirrorLastWrite,
        bool webApiTokenConfigured)
    {
        return new TrustCenterReport(
            GeneratedAt: now,
            Safety: BuildSafety(safetyStatus),
            Provenance: BuildProvenance(provenance),
            Database: BuildDatabase(databasePath, databaseSizeBytes, databaseLastWrite, portableMode, now),
            Mirror: BuildMirror(cloudSyncFolder, mirrorFile, mirrorLastWrite, now),
            Registry: BuildRegistry(registry),
            WebApi: BuildWebApi(webApiTokenConfigured),
            UsePolicy: BuildUsePolicy());
    }

    // -------- Safety --------

    private static TrustCenterSafetySection BuildSafety(TeamViewerSafetyStatus status)
    {
        var (headline, tone) = status.State switch
        {
            TeamViewerSafetyState.NotDetected =>
                ("TeamViewer client not detected", TrustCenterTone.Action),
            TeamViewerSafetyState.Unknown =>
                ($"TeamViewer {status.Version} detected, registry unavailable", TrustCenterTone.Info),
            TeamViewerSafetyState.Safe when status.RecommendedMinimumSafeVersion is { } baseline =>
                ($"TeamViewer {status.Version} is at or above the bundled-registry baseline ({baseline})", TrustCenterTone.Healthy),
            TeamViewerSafetyState.Safe =>
                ($"TeamViewer {status.Version} matches no entries in the bundled CVE registry", TrustCenterTone.Healthy),
            TeamViewerSafetyState.Vulnerable =>
                ($"TeamViewer {status.Version} matches {status.MatchedCves.Length} CVE registry entry{(status.MatchedCves.Length == 1 ? "" : "s")}", TrustCenterTone.Action),
            _ => (status.ChipText, TrustCenterTone.Info),
        };
        return new TrustCenterSafetySection(status, headline, tone);
    }

    // -------- Provenance --------

    private static TrustCenterProvenanceSection BuildProvenance(TeamViewerBinaryProvenance p)
    {
        var (headline, tone) = p.Health switch
        {
            TeamViewerProvenanceHealth.NotFound =>
                ("TeamViewer.exe not located", TrustCenterTone.Action),
            TeamViewerProvenanceHealth.SignedByExpectedPublisher =>
                ("Signed by expected publisher under standard install root", TrustCenterTone.Healthy),
            TeamViewerProvenanceHealth.SignedOutsideExpectedRoot =>
                ("Signed by expected publisher (non-standard install root)", TrustCenterTone.Caution),
            TeamViewerProvenanceHealth.SignedByUnexpectedPublisher =>
                ("Signed but publisher subject is not TeamViewer", TrustCenterTone.Action),
            TeamViewerProvenanceHealth.UnsignedOrUntrusted =>
                ("Authenticode signature missing or untrusted", TrustCenterTone.Action),
            TeamViewerProvenanceHealth.UnableToVerify =>
                ("Signature could not be verified (offline / API failure)", TrustCenterTone.Info),
            _ => (p.Health.ToString(), TrustCenterTone.Info),
        };
        return new TrustCenterProvenanceSection(p, headline, tone);
    }

    // -------- Database --------

    private static TrustCenterDatabaseSection BuildDatabase(
        string path,
        long? sizeBytes,
        DateTimeOffset? lastWrite,
        bool portableMode,
        DateTimeOffset now)
    {
        var exists = sizeBytes is not null;
        var sizeText = sizeBytes is null ? "(no file)" : FormatBytes(sizeBytes.Value);
        var lastWriteText = lastWrite is null
            ? "(no file)"
            : $"{lastWrite.Value:yyyy-MM-dd HH:mm} ({FormatRelative(now - lastWrite.Value)})";
        var modeLabel = portableMode ? "portable (Argon2id-wrapped DEK)" : "per-user (DPAPI-wrapped DEK)";
        var headline = exists
            ? $"Local database, {modeLabel}"
            : "Local database not yet created";
        var tone = exists ? TrustCenterTone.Healthy : TrustCenterTone.Info;

        return new TrustCenterDatabaseSection(
            Path: path,
            Exists: exists,
            SizeBytes: sizeBytes ?? 0,
            LastWrite: lastWrite,
            PortableMode: portableMode,
            Headline: headline,
            SizeText: sizeText,
            LastWriteText: lastWriteText,
            Tone: tone);
    }

    // -------- Mirror --------

    private static TrustCenterMirrorSection BuildMirror(
        string? folder,
        string? mirrorFile,
        DateTimeOffset? lastWrite,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new TrustCenterMirrorSection(
                Configured: false,
                Folder: null,
                MirrorFile: null,
                LastWrite: null,
                Headline: "Encrypted mirror not configured",
                DetailText: "Set a cloud sync folder in Settings to enable an encrypted backup mirror.",
                Tone: TrustCenterTone.Info);
        }

        if (lastWrite is null || mirrorFile is null)
        {
            return new TrustCenterMirrorSection(
                Configured: true,
                Folder: folder,
                MirrorFile: mirrorFile,
                LastWrite: null,
                Headline: "Encrypted mirror configured but not yet written",
                DetailText: $"Mirror folder: {folder}. The mirror is written after database changes.",
                Tone: TrustCenterTone.Caution);
        }

        var age = now - lastWrite.Value;
        var tone = age >= MirrorStaleThreshold ? TrustCenterTone.Caution : TrustCenterTone.Healthy;
        var headline = age >= MirrorStaleThreshold
            ? $"Mirror is {FormatRelative(age)} — stale beyond {(int)MirrorStaleThreshold.TotalDays}-day threshold"
            : $"Mirror written {FormatRelative(age)}";
        var detail = $"{mirrorFile} ({lastWrite.Value:yyyy-MM-dd HH:mm})";

        return new TrustCenterMirrorSection(
            Configured: true,
            Folder: folder,
            MirrorFile: mirrorFile,
            LastWrite: lastWrite,
            Headline: headline,
            DetailText: detail,
            Tone: tone);
    }

    // -------- Registry --------

    private static TrustCenterRegistrySection BuildRegistry(TeamViewerCveRegistry registry)
    {
        var diagnostics = registry.LoadDiagnostics.IsDefault
            ? ImmutableArray<string>.Empty
            : registry.LoadDiagnostics;
        var (headline, tone) = (registry.Entries.Length, diagnostics.Length) switch
        {
            (0, > 0) => ("CVE registry failed to load", TrustCenterTone.Caution),
            (0, _) => ("CVE registry is empty", TrustCenterTone.Info),
            (var n, > 0) => ($"{n} CVE entr{(n == 1 ? "y" : "ies")} loaded with {diagnostics.Length} diagnostic{(diagnostics.Length == 1 ? "" : "s")}", TrustCenterTone.Caution),
            (var n, _) => ($"{n} CVE entr{(n == 1 ? "y" : "ies")} loaded cleanly", TrustCenterTone.Healthy),
        };
        return new TrustCenterRegistrySection(
            EntryCount: registry.Entries.Length,
            LastUpdated: string.IsNullOrWhiteSpace(registry.LastUpdated) ? "(unknown)" : registry.LastUpdated,
            Source: string.IsNullOrWhiteSpace(registry.Source) ? "(no source string)" : registry.Source,
            Diagnostics: diagnostics,
            Headline: headline,
            Tone: tone);
    }

    // -------- Web API --------

    private static TrustCenterWebApiSection BuildWebApi(bool tokenConfigured)
    {
        if (!tokenConfigured)
        {
            return new TrustCenterWebApiSection(
                TokenConfigured: false,
                Headline: "TeamViewer Web API token not configured",
                DetailText: "TeamStation makes no Web API requests without a token. Cloud sync is opt-in.",
                Tone: TrustCenterTone.Info);
        }

        return new TrustCenterWebApiSection(
            TokenConfigured: true,
            Headline: "TeamViewer Web API token configured",
            DetailText: "Read-only group/device sync runs when invoked. Token value is not displayed here.",
            Tone: TrustCenterTone.Healthy);
    }

    // -------- Use policy --------

    private static TrustCenterUsePolicySection BuildUsePolicy()
    {
        return new TrustCenterUsePolicySection(
            Headline: "Transparent local-use posture",
            DetailText: "TeamStation orchestrates the official TeamViewer client and keeps review evidence local. It does not hide sessions, run a relay, or implement the TeamViewer protocol.",
            Safeguards: ImmutableArray.Create(
                "Launch and workflow actions are recorded in the local activity and audit surfaces.",
                "TeamViewer.exe provenance is advisory and visible; failed provenance never becomes a hidden launch blocker.",
                "Credentials and tokens are redacted in status surfaces and documentation examples.",
                "Enterprise allowlisting should key on the TeamViewer publisher/path and the TeamStation release artifact path."),
            Tone: TrustCenterTone.Healthy);
    }

    // -------- Helpers --------

    /// <summary>
    /// Formats a byte count in operator-friendly units. Covers the common
    /// SQLite-DB sizes TeamStation actually produces (a few KB to a few MB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "(unknown)";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";
    }

    /// <summary>
    /// Operator-friendly relative-age string. Negative spans (clock skew) are
    /// rendered as "just now" rather than a misleading future tense.
    /// </summary>
    public static string FormatRelative(TimeSpan age)
    {
        if (age.TotalSeconds < 0) return "just now";
        if (age.TotalSeconds < 60) return "just now";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours} h ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays} d ago";
        if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)} mo ago";
        return $"{(int)(age.TotalDays / 365)} yr ago";
    }
}
