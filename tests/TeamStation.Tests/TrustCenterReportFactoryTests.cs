using System.Collections.Immutable;
using TeamStation.App.ViewModels;
using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.4.0: <see cref="TrustCenterReportFactory"/> synthesises the immutable
/// <see cref="TrustCenterReport"/> consumed by the Trust Center dialog from
/// already-collected probes. Pure logic — no Win32 / IO. Tests cover every
/// section's tone classifier plus the byte / relative-time formatters.
/// </summary>
public class TrustCenterReportFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 25, 12, 0, 0, TimeSpan.Zero);

    private static TeamViewerCveRegistry HealthyRegistry() => TeamViewerCveRegistry.LoadFromJson("""
    {
        "schema_version": 1,
        "last_updated": "2026-04-25",
        "source": "test fixture",
        "entries": [
            { "id": "CVE-OK", "fixed_in": "15.74.5",
              "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.74.5" }] }
        ]
    }
    """);

    private static TrustCenterReport BuildBaseline(
        TeamViewerSafetyStatus? safety = null,
        TeamViewerBinaryProvenance? provenance = null,
        TeamViewerCveRegistry? registry = null,
        long? dbSize = 64 * 1024,
        DateTimeOffset? dbWrite = null,
        bool portable = false,
        string? cloudFolder = null,
        string? mirrorFile = null,
        DateTimeOffset? mirrorWrite = null,
        bool tokenConfigured = false)
    {
        safety ??= TeamViewerSafetyEvaluator.Evaluate(new Version(15, 74, 5), HealthyRegistry());
        provenance ??= TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TeamViewer Germany GmbH",
            fileVersion: "15.74.5.0");

        // When the caller didn't supply a dbWrite, default to a recent
        // timestamp — but only if the file is also reported as existing.
        // Tests that pass dbSize: null are asserting "no file" semantics
        // and rely on dbWrite staying null too.
        var resolvedWrite = dbWrite ?? (dbSize is null ? (DateTimeOffset?)null : Now.AddMinutes(-15));

        return TrustCenterReportFactory.Build(
            now: Now,
            safetyStatus: safety,
            provenance: provenance,
            registry: registry ?? HealthyRegistry(),
            databasePath: @"C:\Users\op\AppData\Local\TeamStation\teamstation.db",
            databaseSizeBytes: dbSize,
            databaseLastWrite: resolvedWrite,
            portableMode: portable,
            cloudSyncFolder: cloudFolder,
            mirrorFile: mirrorFile,
            mirrorLastWrite: mirrorWrite,
            webApiTokenConfigured: tokenConfigured);
    }

    // -------- Safety section --------

    [Fact]
    public void Safety_section_is_healthy_for_safe_version_above_baseline()
    {
        var safety = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 74, 5), HealthyRegistry());
        var report = BuildBaseline(safety: safety);
        Assert.Equal(TrustCenterTone.Healthy, report.Safety.Tone);
        Assert.False(report.Safety.HasMatches);
        Assert.Contains("at or above", report.Safety.Headline);
    }

    [Fact]
    public void Safety_section_is_action_for_vulnerable_version()
    {
        var safety = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 71, 5), HealthyRegistry());
        var report = BuildBaseline(safety: safety);
        Assert.Equal(TrustCenterTone.Action, report.Safety.Tone);
        Assert.True(report.Safety.HasMatches);
        Assert.Single(report.Safety.MatchedCves);
        Assert.Equal("15.74.5", report.Safety.FixedInRecommendation);
    }

    [Fact]
    public void Safety_section_is_action_when_TeamViewer_not_detected()
    {
        var safety = TeamViewerSafetyEvaluator.Evaluate(version: null, HealthyRegistry());
        var report = BuildBaseline(safety: safety);
        Assert.Equal(TrustCenterTone.Action, report.Safety.Tone);
        Assert.Contains("not detected", report.Safety.Headline);
    }

    [Fact]
    public void Safety_section_is_info_when_registry_unavailable()
    {
        var safety = TeamViewerSafetyEvaluator.Evaluate(new Version(15, 71, 5), registry: null);
        var report = BuildBaseline(safety: safety);
        Assert.Equal(TrustCenterTone.Info, report.Safety.Tone);
        Assert.Contains("registry unavailable", report.Safety.Headline);
    }

    // -------- Provenance section --------

    [Fact]
    public void Provenance_section_is_healthy_for_signed_under_program_files()
    {
        var report = BuildBaseline();
        Assert.Equal(TrustCenterTone.Healthy, report.Provenance.Tone);
        Assert.Equal("Trusted (Authenticode)", report.Provenance.SignatureStateText);
        Assert.Contains("Program Files", report.Provenance.InstallRootText);
    }

    [Fact]
    public void Provenance_section_is_action_for_unsigned_binary()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Unsigned,
            publisherSubject: null,
            fileVersion: "15.0.0");
        var report = BuildBaseline(provenance: p);
        Assert.Equal(TrustCenterTone.Action, report.Provenance.Tone);
        Assert.Equal("(no signature)", report.Provenance.PublisherSubject);
    }

    [Fact]
    public void Provenance_section_is_caution_for_signed_outside_expected_root()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"D:\Portable\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.Trusted,
            publisherSubject: "CN=TeamViewer GmbH",
            fileVersion: "15.74.5");
        var report = BuildBaseline(provenance: p);
        Assert.Equal(TrustCenterTone.Caution, report.Provenance.Tone);
    }

    [Fact]
    public void Provenance_section_is_info_for_unable_to_verify()
    {
        var p = TeamViewerBinaryProvenanceEvaluator.Evaluate(
            resolvedPath: @"C:\Program Files\TeamViewer\TeamViewer.exe",
            fileExists: true,
            signatureState: TeamViewerSignatureState.UnableToVerify,
            publisherSubject: null,
            fileVersion: null);
        var report = BuildBaseline(provenance: p);
        Assert.Equal(TrustCenterTone.Info, report.Provenance.Tone);
    }

    // -------- Database section --------

    [Fact]
    public void Database_section_reports_per_user_label_when_not_portable()
    {
        var report = BuildBaseline(portable: false);
        Assert.Contains("per-user", report.Database.Headline);
        Assert.True(report.Database.Exists);
    }

    [Fact]
    public void Database_section_reports_portable_label_when_marker_present()
    {
        var report = BuildBaseline(portable: true);
        Assert.Contains("portable", report.Database.Headline);
        Assert.Contains("Argon2id", report.Database.Headline);
    }

    [Fact]
    public void Database_section_is_info_when_file_does_not_exist()
    {
        var report = BuildBaseline(dbSize: null, dbWrite: null);
        Assert.Equal(TrustCenterTone.Info, report.Database.Tone);
        Assert.False(report.Database.Exists);
        Assert.Equal("(no file)", report.Database.SizeText);
        Assert.Equal("(no file)", report.Database.LastWriteText);
    }

    // -------- Mirror section --------

    [Fact]
    public void Mirror_section_is_info_when_not_configured()
    {
        var report = BuildBaseline(cloudFolder: null);
        Assert.False(report.Mirror.Configured);
        Assert.Equal(TrustCenterTone.Info, report.Mirror.Tone);
        Assert.Contains("not configured", report.Mirror.Headline);
    }

    [Fact]
    public void Mirror_section_is_caution_when_configured_but_never_written()
    {
        var report = BuildBaseline(cloudFolder: @"C:\Users\op\OneDrive\TeamStation", mirrorFile: null, mirrorWrite: null);
        Assert.True(report.Mirror.Configured);
        Assert.Equal(TrustCenterTone.Caution, report.Mirror.Tone);
        Assert.Contains("not yet written", report.Mirror.Headline);
    }

    [Fact]
    public void Mirror_section_is_healthy_when_recently_written()
    {
        var report = BuildBaseline(
            cloudFolder: @"C:\Sync",
            mirrorFile: @"C:\Sync\teamstation.db",
            mirrorWrite: Now.AddDays(-1));
        Assert.Equal(TrustCenterTone.Healthy, report.Mirror.Tone);
    }

    [Fact]
    public void Mirror_section_is_caution_when_older_than_threshold()
    {
        var report = BuildBaseline(
            cloudFolder: @"C:\Sync",
            mirrorFile: @"C:\Sync\teamstation.db",
            mirrorWrite: Now - TrustCenterReportFactory.MirrorStaleThreshold - TimeSpan.FromHours(1));
        Assert.Equal(TrustCenterTone.Caution, report.Mirror.Tone);
        Assert.Contains("stale", report.Mirror.Headline);
    }

    // -------- Registry section --------

    [Fact]
    public void Registry_section_is_healthy_for_clean_load()
    {
        var report = BuildBaseline();
        Assert.Equal(TrustCenterTone.Healthy, report.Registry.Tone);
        Assert.Equal(1, report.Registry.EntryCount);
        Assert.False(report.Registry.HasDiagnostics);
    }

    [Fact]
    public void Registry_section_is_caution_when_diagnostics_present()
    {
        var registry = TeamViewerCveRegistry.LoadFromJson("""
        {
            "schema_version": 1,
            "entries": [
                { "id": "CVE-OK", "affected": [{ "min_inclusive": "15.0.0", "max_exclusive": "15.5.0" }] },
                { "id": "CVE-BAD", "affected": [{ "min_inclusive": "garbage" }] }
            ]
        }
        """);
        var report = BuildBaseline(registry: registry);
        Assert.Equal(TrustCenterTone.Caution, report.Registry.Tone);
        Assert.True(report.Registry.HasDiagnostics);
        Assert.Equal(1, report.Registry.EntryCount);
    }

    [Fact]
    public void Registry_section_is_caution_when_registry_failed_to_load()
    {
        var registry = TeamViewerCveRegistry.Empty("simulated load failure");
        var report = BuildBaseline(registry: registry);
        Assert.Equal(TrustCenterTone.Caution, report.Registry.Tone);
        Assert.Equal(0, report.Registry.EntryCount);
        Assert.Contains("failed to load", report.Registry.Headline);
    }

    // -------- Web API section --------

    [Fact]
    public void WebApi_section_is_info_when_token_not_configured()
    {
        var report = BuildBaseline(tokenConfigured: false);
        Assert.Equal(TrustCenterTone.Info, report.WebApi.Tone);
        Assert.False(report.WebApi.TokenConfigured);
        Assert.Contains("not configured", report.WebApi.Headline);
        Assert.Contains("opt-in", report.WebApi.DetailText);
    }

    [Fact]
    public void WebApi_section_is_healthy_when_token_configured()
    {
        var report = BuildBaseline(tokenConfigured: true);
        Assert.Equal(TrustCenterTone.Healthy, report.WebApi.Tone);
        Assert.True(report.WebApi.TokenConfigured);
        // Token value is never displayed.
        Assert.DoesNotContain("Bearer", report.WebApi.DetailText);
    }

    // -------- Use policy section --------

    [Fact]
    public void UsePolicy_section_records_local_transparency_boundaries()
    {
        var report = BuildBaseline();
        Assert.Equal(TrustCenterTone.Healthy, report.UsePolicy.Tone);
        Assert.Contains("local-use", report.UsePolicy.Headline);
        Assert.Contains("official TeamViewer client", report.UsePolicy.DetailText);
        Assert.Contains("does not hide sessions", report.UsePolicy.DetailText);
        Assert.Contains(report.UsePolicy.SafeguardList, item => item.Contains("recorded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.UsePolicy.SafeguardList, item => item.Contains("allowlisting", StringComparison.OrdinalIgnoreCase));
    }

    // -------- Formatters --------

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(2_500L, "2.4 KB")]
    [InlineData(1_500_000L, "1.43 MB")]
    [InlineData(2L * 1024 * 1024 * 1024, "2 GB")]
    public void FormatBytes_renders_operator_friendly_units(long bytes, string expected)
    {
        Assert.Equal(expected, TrustCenterReportFactory.FormatBytes(bytes));
    }

    [Fact]
    public void FormatBytes_renders_unknown_for_negative()
    {
        Assert.Equal("(unknown)", TrustCenterReportFactory.FormatBytes(-1));
    }

    [Theory]
    [InlineData(-5, "just now")]
    [InlineData(0, "just now")]
    [InlineData(45, "just now")]
    [InlineData(120, "2 min ago")]
    public void FormatRelative_handles_seconds_and_minutes(int seconds, string expected)
    {
        Assert.Equal(expected, TrustCenterReportFactory.FormatRelative(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatRelative_handles_hours_days_months_years()
    {
        Assert.Equal("3 h ago", TrustCenterReportFactory.FormatRelative(TimeSpan.FromHours(3)));
        Assert.Equal("5 d ago", TrustCenterReportFactory.FormatRelative(TimeSpan.FromDays(5)));
        Assert.Equal("2 mo ago", TrustCenterReportFactory.FormatRelative(TimeSpan.FromDays(60)));
        Assert.Equal("2 yr ago", TrustCenterReportFactory.FormatRelative(TimeSpan.FromDays(800)));
    }

    // -------- Top-level --------

    [Fact]
    public void Build_stamps_GeneratedAt_with_passed_in_now()
    {
        var report = BuildBaseline();
        Assert.Equal(Now, report.GeneratedAt);
    }
}
