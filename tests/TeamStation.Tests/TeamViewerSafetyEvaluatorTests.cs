using System.Collections.Immutable;
using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.4.0: <see cref="TeamViewerSafetyEvaluator"/> turns a detected version
/// + bundled CVE registry into the four-state safety status that drives
/// the status-bar pill, tooltip, and Trust Center copy. Pure logic — no
/// registry hive or filesystem.
/// </summary>
public class TeamViewerSafetyEvaluatorTests
{
    private static TeamViewerCveRegistry RegistryWithBaseline() => TeamViewerCveRegistry.LoadFromJson("""
    {
        "schema_version": 1,
        "entries": [
            {
                "id": "CVE-2026-23572",
                "title": "auth bypass fixture",
                "fixed_in": "15.74.5",
                "remediation_url": "https://example.test/cve",
                "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.74.5" }]
            }
        ]
    }
    """);

    [Fact]
    public void Null_version_yields_NotDetected_state()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(null, RegistryWithBaseline());
        Assert.Equal(TeamViewerSafetyState.NotDetected, status.State);
        Assert.False(status.NeedsUpdate);
        Assert.Equal("TeamViewer not detected", status.ChipText);
        Assert.Empty(status.MatchedCves);
    }

    [Fact]
    public void Null_registry_yields_Unknown_state_for_a_detected_version()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 71, 5), registry: null);
        Assert.Equal(TeamViewerSafetyState.Unknown, status.State);
        Assert.False(status.NeedsUpdate);
        Assert.Contains("registry unavailable", status.TooltipText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Safe_version_yields_Safe_state_with_calm_tooltip()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 74, 5), RegistryWithBaseline());
        Assert.Equal(TeamViewerSafetyState.Safe, status.State);
        Assert.False(status.NeedsUpdate);
        Assert.Equal("TeamViewer 15.74.5", status.ChipText);
        Assert.DoesNotContain("Update available", status.TooltipText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vulnerable_version_yields_Vulnerable_state_and_lists_matched_CVEs()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 71, 5), RegistryWithBaseline());
        Assert.Equal(TeamViewerSafetyState.Vulnerable, status.State);
        Assert.True(status.NeedsUpdate);
        Assert.Single(status.MatchedCves);
        Assert.Equal("CVE-2026-23572", status.MatchedCves[0].Id);
        Assert.Contains("CVE-2026-23572", status.TooltipText);
        Assert.Contains("https://example.test/cve", status.TooltipText);
        Assert.Contains("does not auto-update", status.TooltipText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_registry_yields_Safe_state_for_any_detected_version()
    {
        // An empty (but successfully constructed) registry is the
        // documented "we have data and there are no entries" state — every
        // detected version is treated as safe rather than vulnerable.
        var registry = TeamViewerCveRegistry.LoadFromJson("""{ "schema_version": 1, "entries": [] }""");
        var status = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 0, 0), registry);
        Assert.Equal(TeamViewerSafetyState.Safe, status.State);
        Assert.False(status.NeedsUpdate);
    }

    [Fact]
    public void Tooltip_for_safe_version_mentions_recommended_minimum()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(new Version(16, 0, 0), RegistryWithBaseline());
        Assert.Equal(TeamViewerSafetyState.Safe, status.State);
        Assert.Contains("15.74.5", status.TooltipText);
    }

    [Fact]
    public void NotDetected_tooltip_advises_install_or_settings_path()
    {
        var status = TeamViewerSafetyEvaluator.Evaluate(version: null, RegistryWithBaseline());
        Assert.Contains("Install TeamViewer", status.TooltipText);
        Assert.Contains("Settings", status.TooltipText);
    }
}
