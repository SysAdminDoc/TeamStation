using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.5: <see cref="TeamViewerVersionDetector"/> reads the installed
/// TeamViewer client version from the registry (with WOW6432Node mirror)
/// and falls back to <see cref="System.Diagnostics.FileVersionInfo"/>.
/// The status-bar pill uses the parsed <see cref="System.Version"/> +
/// <see cref="TeamViewerVersionDetector.MinimumSafeVersion"/> threshold
/// (15.74.5 — CVE-2026-23572 baseline) to surface "update available".
///
/// Tests pump fakes via <see cref="ITeamViewerVersionSource"/> so we
/// don't require a live registry hive.
/// </summary>
public class TeamViewerVersionDetectorTests
{
    private sealed class FakeSource(string? raw) : ITeamViewerVersionSource
    {
        public string? ReadVersionString() => raw;
    }

    [Theory]
    [InlineData("15.71.5", 15, 71, 5)]
    [InlineData("15.74.5", 15, 74, 5)]
    [InlineData("15.71.5.0", 15, 71, 5)] // 4-component (build) — System.Version handles it
    [InlineData(" 15.71.5 ", 15, 71, 5)] // surrounding whitespace
    public void TryParseVersion_accepts_well_formed_TeamViewer_strings(string raw, int major, int minor, int build)
    {
        Assert.True(TeamViewerVersionDetector.TryParseVersion(raw, out var v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(build, v.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a version")]
    [InlineData("v15.71.5")] // System.Version doesn't accept leading 'v'
    public void TryParseVersion_rejects_bad_input(string? raw)
    {
        Assert.False(TeamViewerVersionDetector.TryParseVersion(raw, out _));
    }

    [Fact]
    public void Detect_returns_null_when_source_returns_null()
    {
        Assert.Null(TeamViewerVersionDetector.Detect(new FakeSource(null)));
    }

    [Fact]
    public void Detect_returns_parsed_version_from_source()
    {
        var v = TeamViewerVersionDetector.Detect(new FakeSource("15.71.5"));
        Assert.NotNull(v);
        Assert.Equal(new Version(15, 71, 5), v);
    }

    [Theory]
    [InlineData("15.71.5", true)]   // below baseline → update needed
    [InlineData("15.74.4", true)]   // just below
    [InlineData("15.74.5", false)]  // exactly the baseline → safe
    [InlineData("15.74.6", false)]  // above
    [InlineData("16.0.0", false)]   // future major
    public void NeedsUpdate_threshold_against_minimum_safe(string raw, bool expected)
    {
        var v = TeamViewerVersionDetector.Detect(new FakeSource(raw));
        Assert.Equal(expected, TeamViewerVersionDetector.NeedsUpdate(v));
    }

    [Fact]
    public void NeedsUpdate_returns_false_for_null_version()
    {
        // "TeamViewer not detected" should NOT light the update-available pill —
        // the chip's "not detected" message handles that case separately.
        Assert.False(TeamViewerVersionDetector.NeedsUpdate(null));
    }

    [Fact]
    public void MinimumSafeVersion_pins_the_CVE_2026_23572_baseline()
    {
        // 15.74.5 is the minimum patched build per the April 2026 TeamViewer
        // security bulletin. If this constant changes the test should
        // surface the change deliberately.
        Assert.Equal(new Version(15, 74, 5), TeamViewerVersionDetector.MinimumSafeVersion);
    }
}
