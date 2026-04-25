using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.4.0: <see cref="TeamViewerCveRegistry"/> generalises the hardcoded
/// <c>MinimumSafeVersion = 15.74.5</c> in <see cref="TeamViewerVersionDetector"/>
/// into a JSON-driven registry so future CVE bulletins are an asset edit
/// (and a retag), not a code change.
///
/// Tests cover the safe / vulnerable / unknown / malformed / missing / future-
/// extensibility shapes called out in the v0.4.0 brief.
/// </summary>
public class TeamViewerCveRegistryTests
{
    private static TeamViewerCveRegistry MinimalRegistry() => TeamViewerCveRegistry.LoadFromJson("""
    {
        "schema_version": 1,
        "last_updated": "2026-04-25",
        "source": "test fixture",
        "entries": [
            {
                "id": "CVE-2026-23572",
                "title": "TeamViewer auth bypass",
                "cvss": 7.2,
                "severity": "high",
                "summary": "fixture summary",
                "remediation_url": "https://example.test/cve",
                "fixed_in": "15.74.5",
                "affected": [
                    { "min_inclusive": "15.0.0", "max_exclusive": "15.74.5" }
                ]
            }
        ]
    }
    """);

    [Fact]
    public void Default_registry_loads_from_embedded_resource()
    {
        var registry = TeamViewerCveRegistry.Default;

        // The bundled registry must load cleanly — if this breaks, the
        // assets/cve/teamviewer-known.json file or the LogicalName mapping
        // in TeamStation.Launcher.csproj has regressed.
        Assert.True(registry.Entries.Length >= 1, "Bundled registry should carry at least one entry.");
        Assert.Empty(registry.LoadDiagnostics);
        Assert.Equal(1, registry.SchemaVersion);
    }

    [Fact]
    public void Default_registry_includes_CVE_2026_23572_baseline()
    {
        var registry = TeamViewerCveRegistry.Default;
        Assert.Contains(registry.Entries, e => e.Id == "CVE-2026-23572");
    }

    // -------- Safe / vulnerable / unknown classification --------

    [Theory]
    [InlineData("15.0.0", true)]
    [InlineData("15.71.5", true)]
    [InlineData("15.74.4", true)]
    public void Match_flags_versions_inside_affected_range_as_vulnerable(string raw, bool expectMatch)
    {
        var registry = MinimalRegistry();
        var matches = registry.Match(Version.Parse(raw));
        Assert.Equal(expectMatch, !matches.IsEmpty);
    }

    [Theory]
    [InlineData("15.74.5")]
    [InlineData("15.74.6")]
    [InlineData("16.0.0")]
    [InlineData("14.99.99")] // below the min_inclusive
    public void Match_returns_empty_for_safe_or_unrelated_versions(string raw)
    {
        var registry = MinimalRegistry();
        Assert.Empty(registry.Match(Version.Parse(raw)));
    }

    [Fact]
    public void Match_returns_empty_for_null_version()
    {
        Assert.Empty(MinimalRegistry().Match(null));
    }

    [Fact]
    public void RecommendedMinimumSafeVersion_returns_highest_fixed_in()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-OLD", "fixed_in": "15.8.3", "affected": [{ "min_inclusive": "8.0.0", "max_exclusive": "15.8.3" }] },
                { "id": "CVE-NEW", "fixed_in": "15.74.5", "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.74.5" }] }
            ]
        }
        """);

        // Highest fixed_in is the conservative "minimum safe across everything we know"
        // value — being at or above it is sufficient for every entry in the registry.
        Assert.Equal(new Version(15, 74, 5), registry.RecommendedMinimumSafeVersion());
    }

    [Fact]
    public void RecommendedMinimumSafeVersion_returns_null_when_no_entry_has_fixed_in()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-NOFIX", "affected": [{ "min_inclusive": "1.0.0" }] }
            ]
        }
        """);
        Assert.Null(registry.RecommendedMinimumSafeVersion());
    }

    // -------- Malformed registry rows --------

    [Fact]
    public void Malformed_JSON_yields_Empty_registry_and_a_diagnostic()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("{ not json");
        Assert.Empty(registry.Entries);
        Assert.Single(registry.LoadDiagnostics);
        Assert.Contains("could not be parsed", registry.LoadDiagnostics[0]);
    }

    [Fact]
    public void Whitespace_or_null_JSON_yields_Empty_registry_and_a_diagnostic()
    {
        var fromNull = TeamViewerCveRegistry.LoadFromJson(null!);
        Assert.Empty(fromNull.Entries);
        Assert.Single(fromNull.LoadDiagnostics);

        var fromBlank = TeamViewerCveRegistry.LoadFromJson("   ");
        Assert.Empty(fromBlank.Entries);
        Assert.Single(fromBlank.LoadDiagnostics);
    }

    [Fact]
    public void Entry_without_id_is_skipped_with_diagnostic()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "", "affected": [{ "min_inclusive": "15.0.0" }] },
                { "id": "CVE-OK", "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" }] }
            ]
        }
        """);
        Assert.Single(registry.Entries);
        Assert.Equal("CVE-OK", registry.Entries[0].Id);
        Assert.Contains(registry.LoadDiagnostics, d => d.Contains("no id"));
    }

    [Fact]
    public void Range_with_no_bounds_is_skipped_and_entry_falls_back_to_other_ranges()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                {
                    "id": "CVE-A",
                    "affected": [
                        { },
                        { "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" }
                    ]
                }
            ]
        }
        """);
        Assert.Single(registry.Entries);
        var entry = registry.Entries[0];
        Assert.Single(entry.Affected); // The empty range was dropped.
        Assert.Contains(registry.LoadDiagnostics, d => d.Contains("CVE-A") && d.Contains("range"));
    }

    [Fact]
    public void Entry_whose_only_range_is_malformed_is_skipped_entirely()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-BROKEN", "affected": [ { "min_inclusive": "garbage" } ] },
                { "id": "CVE-OK", "affected": [ { "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" } ] }
            ]
        }
        """);
        Assert.Single(registry.Entries);
        Assert.Equal("CVE-OK", registry.Entries[0].Id);
        Assert.Contains(registry.LoadDiagnostics, d => d.Contains("CVE-BROKEN"));
    }

    [Fact]
    public void Inverted_range_is_rejected_with_diagnostic()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-INV", "affected": [ { "min_inclusive": "15.5.0", "max_exclusive": "15.0.0" } ] }
            ]
        }
        """);
        Assert.Empty(registry.Entries);
        Assert.Contains(registry.LoadDiagnostics, d => d.Contains("strictly less than"));
    }

    [Fact]
    public void Malformed_fixed_in_is_dropped_to_null_with_diagnostic()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-FIX", "fixed_in": "not-a-version",
                  "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" }] }
            ]
        }
        """);
        Assert.Single(registry.Entries);
        Assert.Null(registry.Entries[0].FixedIn);
        Assert.Contains(registry.LoadDiagnostics, d => d.Contains("fixed_in"));
    }

    // -------- Future extensibility --------

    [Fact]
    public void Unknown_top_level_fields_are_ignored()
    {
        // Adding new top-level metadata in a future schema_version must not
        // break a v1 reader.
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "advisory_format": "future-field",
            "future_section": { "key": "value" },
            "entries": [
                { "id": "CVE-OK", "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" }] }
            ]
        }
        """);
        Assert.Single(registry.Entries);
        Assert.Empty(registry.LoadDiagnostics);
    }

    [Fact]
    public void Multiple_affected_ranges_in_one_entry_match_independently()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                {
                    "id": "CVE-MULTI",
                    "affected": [
                        { "min_inclusive": "10.0.0", "max_exclusive": "10.5.0" },
                        { "min_inclusive": "12.0.0", "max_exclusive": "12.3.0" }
                    ]
                }
            ]
        }
        """);
        Assert.Single(registry.Match(new Version(10, 2, 0)));
        Assert.Single(registry.Match(new Version(12, 1, 0)));
        Assert.Empty(registry.Match(new Version(11, 0, 0)));
        Assert.Empty(registry.Match(new Version(12, 3, 0))); // exclusive bound
    }

    // -------- Missing registry behaviour --------

    [Fact]
    public void Empty_registry_factory_carries_diagnostic()
    {
        var registry = TeamViewerCveRegistry.Empty("registry resource missing");
        Assert.Empty(registry.Entries);
        Assert.Single(registry.LoadDiagnostics);
        Assert.Contains("missing", registry.LoadDiagnostics[0]);
    }

    [Fact]
    public void Empty_registry_returns_no_matches_for_any_version()
    {
        var registry = TeamViewerCveRegistry.Empty("test");
        Assert.Empty(registry.Match(new Version(15, 71, 5)));
        Assert.Empty(registry.Match(new Version(0, 0)));
        Assert.Empty(registry.Match(null));
        Assert.Null(registry.RecommendedMinimumSafeVersion());
    }
}
