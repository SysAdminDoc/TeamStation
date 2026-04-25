using System.Runtime.Versioning;

namespace TeamStation.Launcher;

/// <summary>
/// Pure evaluation of <c>TeamViewer.exe</c> trust signals — exists, lives
/// under an expected install root, carries a valid Authenticode signature,
/// and is signed by a publisher whose subject contains the expected
/// "TeamViewer" token. Decoupled from the Win32 surface
/// (<see cref="TeamViewerBinaryProvenanceInspector"/>) so the evaluation
/// shape can be unit-tested without a real signed binary.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT anti-malware. The Authenticode check is a local trust signal
/// that catches accidentally-renamed, stripped, or side-loaded executables —
/// e.g. an operator-pointed override path that resolves to a stub launcher
/// from a different vendor. A determined attacker who can drop a signed
/// binary on the same machine still passes; that is by design and called
/// out in <c>docs/teamviewer-reference.md</c>.
/// </para>
/// <para>
/// Output is advisory only: TeamStation never blocks launch on a failed
/// provenance check. The Trust Center / log line surfaces the result so the
/// operator can investigate.
/// </para>
/// </remarks>
public static class TeamViewerBinaryProvenanceEvaluator
{
    /// <summary>
    /// Default substring that must appear, case-insensitively, in the
    /// signing certificate subject for the publisher to be considered
    /// expected. TeamViewer's release certificate has historically been
    /// issued to "TeamViewer Germany GmbH" / "TeamViewer GmbH" — both
    /// contain the literal "TeamViewer".
    /// </summary>
    public const string DefaultExpectedPublisherSubstring = "TeamViewer";

    /// <summary>
    /// Substrings that an expected install root path must contain
    /// (case-insensitively). Matches the documented Program Files /
    /// Program Files (x86) / per-version layouts handled by
    /// <see cref="TeamViewerPathResolver"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExpectedRootMarkers =
        ["\\Program Files\\TeamViewer", "\\Program Files (x86)\\TeamViewer"];

    /// <summary>
    /// Maps raw probe results into a <see cref="TeamViewerBinaryProvenance"/>
    /// suitable for logging and UI. Null / whitespace
    /// <paramref name="resolvedPath"/> means "no installed client could be
    /// located" — every other field is then irrelevant.
    /// </summary>
    public static TeamViewerBinaryProvenance Evaluate(
        string? resolvedPath,
        bool fileExists,
        TeamViewerSignatureState signatureState,
        string? publisherSubject,
        string? fileVersion,
        string? expectedPublisherSubstring = null,
        IReadOnlyList<string>? expectedRootMarkers = null)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return new TeamViewerBinaryProvenance(
                Path: null,
                Exists: false,
                IsUnderExpectedInstallRoot: false,
                FileVersion: null,
                SignatureState: TeamViewerSignatureState.NotApplicable,
                PublisherSubject: null,
                IsExpectedPublisher: false,
                Health: TeamViewerProvenanceHealth.NotFound,
                Advice: "TeamViewer.exe could not be located. Install TeamViewer or set the path in Settings.");
        }

        var publisher = expectedPublisherSubstring ?? DefaultExpectedPublisherSubstring;
        var roots = expectedRootMarkers ?? DefaultExpectedRootMarkers;

        var underExpectedRoot = IsUnderExpectedRoot(resolvedPath, roots);
        var publisherMatches =
            !string.IsNullOrWhiteSpace(publisherSubject) &&
            publisherSubject.Contains(publisher, StringComparison.OrdinalIgnoreCase);

        var (health, advice) = ClassifyHealth(
            fileExists,
            signatureState,
            publisherMatches,
            underExpectedRoot,
            resolvedPath);

        return new TeamViewerBinaryProvenance(
            Path: resolvedPath,
            Exists: fileExists,
            IsUnderExpectedInstallRoot: underExpectedRoot,
            FileVersion: string.IsNullOrWhiteSpace(fileVersion) ? null : fileVersion,
            SignatureState: signatureState,
            PublisherSubject: string.IsNullOrWhiteSpace(publisherSubject) ? null : publisherSubject,
            IsExpectedPublisher: publisherMatches,
            Health: health,
            Advice: advice);
    }

    public static bool IsUnderExpectedRoot(string path, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(path) || markers.Count == 0) return false;
        // Keep the comparison forgiving: treat both backslash and forward-slash
        // path separators as equivalent so a registry-stored mixed-slash path
        // does not falsely flag a normal install.
        var canonical = path.Replace('/', '\\');
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker)) continue;
            if (canonical.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (TeamViewerProvenanceHealth Health, string Advice) ClassifyHealth(
        bool exists,
        TeamViewerSignatureState sig,
        bool publisherMatches,
        bool underExpectedRoot,
        string path)
    {
        if (!exists)
        {
            return (TeamViewerProvenanceHealth.NotFound,
                $"Resolved TeamViewer path '{path}' does not exist on disk.");
        }

        switch (sig)
        {
            case TeamViewerSignatureState.UnableToVerify:
                return (TeamViewerProvenanceHealth.UnableToVerify,
                    $"Signature on '{path}' could not be verified (Authenticode check failed). " +
                    "This can happen on offline systems where revocation lists cannot be reached.");
            case TeamViewerSignatureState.Unsigned:
                return (TeamViewerProvenanceHealth.UnsignedOrUntrusted,
                    $"'{path}' is not Authenticode signed. Confirm the install came from teamviewer.com.");
            case TeamViewerSignatureState.Untrusted:
                return (TeamViewerProvenanceHealth.UnsignedOrUntrusted,
                    $"Authenticode signature on '{path}' did not validate against the local trust store.");
            case TeamViewerSignatureState.Trusted when !publisherMatches:
                return (TeamViewerProvenanceHealth.SignedByUnexpectedPublisher,
                    $"'{path}' is signed but the publisher does not look like TeamViewer. " +
                    "Confirm the configured TeamViewer path points at the genuine client.");
            case TeamViewerSignatureState.Trusted when !underExpectedRoot:
                return (TeamViewerProvenanceHealth.SignedOutsideExpectedRoot,
                    $"'{path}' is signed by the expected publisher but is not under a standard " +
                    "Program Files \\TeamViewer install root. This is fine for portable / per-user " +
                    "installs; flag any mismatch with your deployment expectations.");
            case TeamViewerSignatureState.Trusted:
                return (TeamViewerProvenanceHealth.SignedByExpectedPublisher,
                    $"'{path}' is Authenticode signed by {DefaultExpectedPublisherSubstring} and lives under a standard install root.");
            default:
                return (TeamViewerProvenanceHealth.UnableToVerify,
                    $"Unhandled signature state '{sig}' for '{path}'.");
        }
    }
}

/// <summary>
/// Classifies the Authenticode state on the resolved <c>TeamViewer.exe</c>.
/// Distinct from <see cref="TeamViewerProvenanceHealth"/> so callers can
/// reason about the underlying signal independent of the operator advice.
/// </summary>
public enum TeamViewerSignatureState
{
    /// <summary>No file to evaluate (resolved path was null or missing).</summary>
    NotApplicable,
    /// <summary>The file is not Authenticode signed at all.</summary>
    Unsigned,
    /// <summary>The file carries a signature that does not validate against local trust.</summary>
    Untrusted,
    /// <summary>The file carries a signature that validates against local trust.</summary>
    Trusted,
    /// <summary>The verifier failed for environmental reasons (offline CRL, API error, etc.).</summary>
    UnableToVerify,
}

/// <summary>
/// Operator-facing health classification surfaced in the log and (in a
/// later v0.4.0 slice) the Trust Center dashboard.
/// </summary>
public enum TeamViewerProvenanceHealth
{
    /// <summary>No installed TeamViewer client was found.</summary>
    NotFound,
    /// <summary>File exists, signed by the expected publisher under a standard install root.</summary>
    SignedByExpectedPublisher,
    /// <summary>Signed by the expected publisher but not under a standard install root.</summary>
    SignedOutsideExpectedRoot,
    /// <summary>Signed but the publisher subject does not contain the expected token.</summary>
    SignedByUnexpectedPublisher,
    /// <summary>File exists but has no signature, or the signature did not validate.</summary>
    UnsignedOrUntrusted,
    /// <summary>Verification failed for environmental reasons; treat as advisory only.</summary>
    UnableToVerify,
}

/// <summary>
/// Snapshot of the <c>TeamViewer.exe</c> trust signals at a point in time.
/// Designed to be cheap to recompute on Settings change and serialise into
/// future Trust Center / Evidence Pack outputs.
/// </summary>
public sealed record TeamViewerBinaryProvenance(
    string? Path,
    bool Exists,
    bool IsUnderExpectedInstallRoot,
    string? FileVersion,
    TeamViewerSignatureState SignatureState,
    string? PublisherSubject,
    bool IsExpectedPublisher,
    TeamViewerProvenanceHealth Health,
    string Advice);

/// <summary>
/// Win32-bound side that probes the actual installed binary. Wraps
/// <c>WinVerifyTrust</c> via <see cref="System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile"/>
/// for the publisher subject and runs <c>WinVerifyTrust</c> for the trust
/// validation. Pure evaluation lives in
/// <see cref="TeamViewerBinaryProvenanceEvaluator"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TeamViewerBinaryProvenanceInspector
{
    /// <summary>
    /// Performs the full provenance probe on <see cref="TeamViewerPathResolver.Resolve"/>'s
    /// result, or on <paramref name="explicitPath"/> when supplied (Settings
    /// override path). Cheap enough to call on app startup; not on every
    /// launch.
    /// </summary>
    public static TeamViewerBinaryProvenance Inspect(string? explicitPath = null)
    {
        var path = !string.IsNullOrWhiteSpace(explicitPath) ? explicitPath : TeamViewerPathResolver.Resolve();
        if (string.IsNullOrWhiteSpace(path))
        {
            return TeamViewerBinaryProvenanceEvaluator.Evaluate(
                resolvedPath: null,
                fileExists: false,
                signatureState: TeamViewerSignatureState.NotApplicable,
                publisherSubject: null,
                fileVersion: null);
        }

        var exists = File.Exists(path);
        if (!exists)
        {
            return TeamViewerBinaryProvenanceEvaluator.Evaluate(
                resolvedPath: path,
                fileExists: false,
                signatureState: TeamViewerSignatureState.NotApplicable,
                publisherSubject: null,
                fileVersion: null);
        }

        string? fileVersion = null;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            fileVersion = info.FileVersion ?? info.ProductVersion;
        }
        catch
        {
            // Best-effort; caller already knows fileExists is true.
        }

        var publisher = TryReadPublisherSubject(path);
        var sigState = WinTrust.Verify(path);

        return TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: path,
            fileExists: true,
            signatureState: sigState,
            publisherSubject: publisher,
            fileVersion: fileVersion);
    }

    private static string? TryReadPublisherSubject(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            // X509Certificate.CreateFromSignedFile is the simplest way to read
            // the signing certificate subject without going through CMS APIs.
            // Replacement guidance is unstable across .NET 9 / 10 — the call
            // here is read-only and runs once at startup, so the deprecation
            // warning is acceptable until we move to a longer-term API.
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }
}
