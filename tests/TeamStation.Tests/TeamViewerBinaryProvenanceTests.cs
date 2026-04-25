using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.4.0: <see cref="TeamViewerBinaryProvenanceEvaluator"/> classifies the
/// trust signals on the resolved <c>TeamViewer.exe</c> into an operator-
/// facing health bucket. The Win32 probe lives in
/// <see cref="TeamViewerBinaryProvenanceInspector"/>; these tests pump
/// synthetic inputs through the pure evaluator so we don't need a real
/// signed binary to assert the classification logic.
/// </summary>
public class TeamViewerBinaryProvenanceTests
{
    [Fact]
    public void Null_path_yields_NotFound_health()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: null,
            fileExists: false,
            signatureState: TeamViewerSignatureState.NotApplicable,
            publisherSubject: null,
            fileVersion: null);

        Assert.Equal(TeamViewerProvenanceHealth.NotFound, p.Health);
        Assert.False(p.Exists);
        Assert.Null(p.Path);
        Assert.Contains("could not be located", p.Advice);
    }

    [Fact]
    public void Existing_path_with_no_file_yields_NotFound_health()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: false,
            signatureState: TeamViewerSignatureState.NotApplicable,
            publisherSubject: null,
            fileVersion: null);

        Assert.Equal(TeamViewerProvenanceHealth.NotFound, p.Health);
        Assert.Contains("does not exist", p.Advice);
    }

    [Fact]
    public void Trusted_signature_with_TeamViewer_publisher_under_program_files_is_healthy()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TeamViewer Germany GmbH, O=TeamViewer Germany GmbH, L=Goeppingen, S=Baden-Wuerttemberg, C=DE",
            fileVersion: "15.74.5.0");

        Assert.Equal(TeamViewerProvenanceHealth.SignedByExpectedPublisher, p.Health);
        Assert.True(p.IsExpectedPublisher);
        Assert.True(p.IsUnderExpectedInstallRoot);
        Assert.Equal("15.74.5.0", p.FileVersion);
    }

    [Fact]
    public void Trusted_signature_under_x86_install_root_is_also_healthy()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files (x86)\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TeamViewer GmbH",
            fileVersion: "15.50.0");

        Assert.Equal(TeamViewerProvenanceHealth.SignedByExpectedPublisher, p.Health);
        Assert.True(p.IsUnderExpectedInstallRoot);
    }

    [Fact]
    public void Trusted_signature_outside_expected_root_warns_softly()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"D:\Portable\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TeamViewer Germany GmbH",
            fileVersion: "15.74.5");

        Assert.Equal(TeamViewerProvenanceHealth.SignedOutsideExpectedRoot, p.Health);
        Assert.True(p.IsExpectedPublisher);
        Assert.False(p.IsUnderExpectedInstallRoot);
        Assert.Contains("portable", p.Advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trusted_signature_with_unexpected_publisher_is_flagged()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=Some Other Vendor",
            fileVersion: "15.0.0");

        Assert.Equal(TeamViewerProvenanceHealth.SignedByUnexpectedPublisher, p.Health);
        Assert.False(p.IsExpectedPublisher);
        Assert.Contains("publisher does not look like TeamViewer", p.Advice);
    }

    [Fact]
    public void Unsigned_binary_is_flagged_as_unsigned_or_untrusted()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Unsigned,
            publisherSubject: null,
            fileVersion: "15.0.0");

        Assert.Equal(TeamViewerProvenanceHealth.UnsignedOrUntrusted, p.Health);
        Assert.Contains("not Authenticode signed", p.Advice);
    }

    [Fact]
    public void Untrusted_signature_is_flagged_as_unsigned_or_untrusted()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Untrusted,
            publisherSubject: "CN=Compromised CA",
            fileVersion: "15.0.0");

        Assert.Equal(TeamViewerProvenanceHealth.UnsignedOrUntrusted, p.Health);
        Assert.Contains("did not validate", p.Advice);
    }

    [Fact]
    public void UnableToVerify_is_advisory_not_alarming()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.UnableToVerify,
            publisherSubject: null,
            fileVersion: null);

        Assert.Equal(TeamViewerProvenanceHealth.UnableToVerify, p.Health);
        Assert.Contains("offline systems", p.Advice);
    }

    [Theory]
    [InlineData(@"C:\Program Files\TeamViewer\TeamViewer.exe", true)]
    [InlineData(@"C:\Program Files (x86)\TeamViewer\TeamViewer.exe", true)]
    [InlineData(@"C:\Program Files\TeamViewer\Version15\TeamViewer.exe", true)]
    [InlineData(@"D:\Portable\TeamViewer\TeamViewer.exe", false)]
    [InlineData(@"C:/Program Files/TeamViewer/TeamViewer.exe", true)] // forward-slash tolerant
    [InlineData(@"C:\Users\op\Downloads\TeamViewer.exe", false)]
    public void IsUnderExpectedRoot_matches_known_install_paths(string path, bool expected)
    {
        Assert.Equal(expected, TeamViewerBinaryProvenanceEvaluator.IsUnderExpectedRoot(
            path,
            TeamViewerBinaryProvenanceEvaluator.DefaultExpectedRootMarkers));
    }

    [Fact]
    public void Custom_publisher_substring_is_honoured()
    {
        // Maintainers may rotate the expected publisher token if TeamViewer
        // changes its certificate subject.
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TV Operations GmbH",
            fileVersion: "15.0.0",
            expectedPublisherSubstring: "TV Operations");

        Assert.Equal(TeamViewerProvenanceHealth.SignedByExpectedPublisher, p.Health);
        Assert.True(p.IsExpectedPublisher);
    }
}
