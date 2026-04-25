using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamStation.Launcher;

/// <summary>
/// Static, offline-only registry of publicly disclosed TeamViewer client CVEs
/// that affect operator launch decisions. The data ships embedded in the
/// <c>TeamStation.Launcher</c> assembly (<c>assets/cve/teamviewer-known.json</c>)
/// and is matched against the version returned by
/// <see cref="TeamViewerVersionDetector"/> to drive the status-bar safety
/// surface. Maintainers update the bundled JSON; TeamStation never phones home
/// to fetch it.
/// </summary>
/// <remarks>
/// <para>
/// Defensive parsing: malformed entries are skipped (with their <see cref="Id"/>
/// captured in <see cref="LoadDiagnostics"/>) rather than throwing — a single
/// bad row in a future bulletin update must not stop the rest of the registry
/// from loading. A completely missing or unparseable file degrades to an
/// empty registry plus a single diagnostic line; callers should treat that
/// as <see cref="TeamViewerSafetyState.Unknown"/> rather than <see cref="TeamViewerSafetyState.Safe"/>.
/// </para>
/// <para>
/// Version-range matching uses half-open intervals
/// (<c>min_inclusive &lt;= version &lt; max_exclusive</c>). Either bound may be
/// omitted; an entry with no bounds at all is treated as malformed and
/// skipped. The advisory model is "an installed client whose detected
/// version falls in any affected range is considered vulnerable" — the
/// status bar then renders the matched CVE summary and remediation URL.
/// </para>
/// </remarks>
public sealed class TeamViewerCveRegistry
{
    /// <summary>Embedded resource name shipped inside <c>TeamStation.Launcher.dll</c>.</summary>
    public const string EmbeddedResourceName = "TeamStation.Launcher.assets.cve.teamviewer-known.json";

    private static readonly Lazy<TeamViewerCveRegistry> _default = new(LoadDefault, isThreadSafe: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Process-wide registry loaded from the embedded resource. Computed once
    /// and reused. Tests should construct their own registries via
    /// <see cref="LoadFromJson"/> instead of touching <see cref="Default"/>.
    /// </summary>
    public static TeamViewerCveRegistry Default => _default.Value;

    /// <summary>Schema version declared in the JSON.</summary>
    public int SchemaVersion { get; }

    /// <summary>Free-text source/maintainer note for the registry.</summary>
    public string Source { get; }

    /// <summary>Last-updated date string from the registry header (e.g. <c>"2026-04-25"</c>).</summary>
    public string LastUpdated { get; }

    /// <summary>All successfully parsed entries, in source order.</summary>
    public ImmutableArray<TeamViewerCveEntry> Entries { get; }

    /// <summary>
    /// Diagnostics emitted while loading — typically a one-line message per
    /// skipped row plus a single message if the registry file itself was
    /// missing or unparseable. Empty on a clean load.
    /// </summary>
    public ImmutableArray<string> LoadDiagnostics { get; }

    private TeamViewerCveRegistry(
        int schemaVersion,
        string source,
        string lastUpdated,
        ImmutableArray<TeamViewerCveEntry> entries,
        ImmutableArray<string> diagnostics)
    {
        SchemaVersion = schemaVersion;
        Source = source;
        LastUpdated = lastUpdated;
        Entries = entries;
        LoadDiagnostics = diagnostics;
    }

    /// <summary>
    /// Builds an empty registry. Callers consuming this should surface a
    /// "TeamViewer safety status: unknown (registry unavailable)" message —
    /// it is NOT the same as "safe". <see cref="LoadDiagnostics"/> carries
    /// the explanation passed in.
    /// </summary>
    public static TeamViewerCveRegistry Empty(string diagnostic) =>
        new(0, string.Empty, string.Empty, ImmutableArray<TeamViewerCveEntry>.Empty,
            string.IsNullOrWhiteSpace(diagnostic)
                ? ImmutableArray<string>.Empty
                : ImmutableArray.Create(diagnostic));

    /// <summary>
    /// Parses a JSON document with the bundled schema. Malformed individual
    /// entries are skipped and recorded in <see cref="LoadDiagnostics"/>;
    /// a malformed top-level document yields an <see cref="Empty"/> registry
    /// with a single diagnostic line.
    /// </summary>
    public static TeamViewerCveRegistry LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty("Registry JSON was null or empty.");

        RegistryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegistryDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Empty($"Registry JSON could not be parsed: {ex.Message}");
        }

        if (dto is null)
            return Empty("Registry JSON deserialized to null.");

        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var entries = ImmutableArray.CreateBuilder<TeamViewerCveEntry>();

        if (dto.Entries is { Length: > 0 })
        {
            for (var i = 0; i < dto.Entries.Length; i++)
            {
                var raw = dto.Entries[i];
                if (raw is null)
                {
                    diagnostics.Add($"CVE entry at index {i} was null — skipped.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(raw.Id))
                {
                    diagnostics.Add($"CVE entry at index {i} has no id — skipped.");
                    continue;
                }

                var ranges = ImmutableArray.CreateBuilder<TeamViewerVersionRange>();
                if (raw.Affected is { Length: > 0 })
                {
                    foreach (var rawRange in raw.Affected)
                    {
                        if (rawRange is null) continue;
                        var (parsed, parseError) = TryParseRange(rawRange);
                        if (parsed is null)
                        {
                            diagnostics.Add($"{raw.Id}: skipped affected range — {parseError}");
                            continue;
                        }
                        ranges.Add(parsed.Value);
                    }
                }

                if (ranges.Count == 0)
                {
                    diagnostics.Add($"{raw.Id}: no usable affected ranges — entry skipped.");
                    continue;
                }

                Version? fixedIn = null;
                if (!string.IsNullOrWhiteSpace(raw.FixedIn) && !Version.TryParse(raw.FixedIn, out fixedIn))
                {
                    diagnostics.Add($"{raw.Id}: fixed_in '{raw.FixedIn}' is not a valid version — ignored.");
                    fixedIn = null;
                }

                entries.Add(new TeamViewerCveEntry(
                    raw.Id!.Trim(),
                    raw.Title?.Trim() ?? string.Empty,
                    raw.Cvss,
                    raw.Severity?.Trim() ?? string.Empty,
                    raw.Published?.Trim() ?? string.Empty,
                    raw.Summary?.Trim() ?? string.Empty,
                    raw.Remediation?.Trim() ?? string.Empty,
                    raw.RemediationUrl?.Trim() ?? string.Empty,
                    fixedIn,
                    ranges.ToImmutable()));
            }
        }

        return new TeamViewerCveRegistry(
            dto.SchemaVersion,
            dto.Source ?? string.Empty,
            dto.LastUpdated ?? string.Empty,
            entries.ToImmutable(),
            diagnostics.ToImmutable());
    }

    /// <summary>
    /// Returns every entry whose affected ranges contain
    /// <paramref name="version"/>. Empty when the version is null or no
    /// entries match.
    /// </summary>
    public ImmutableArray<TeamViewerCveEntry> Match(Version? version)
    {
        if (version is null || Entries.IsDefaultOrEmpty)
            return ImmutableArray<TeamViewerCveEntry>.Empty;

        var matches = ImmutableArray.CreateBuilder<TeamViewerCveEntry>();
        foreach (var entry in Entries)
        {
            foreach (var range in entry.Affected)
            {
                if (range.Contains(version))
                {
                    matches.Add(entry);
                    break;
                }
            }
        }

        return matches.ToImmutable();
    }

    /// <summary>
    /// Highest <c>fixed_in</c> across all parsed entries, or null when no
    /// entry declared one. Treated as the minimum version that clears every
    /// CVE in the bundled registry — being at or above it is sufficient for
    /// "safe against everything we know about". Surfaced in status-bar copy
    /// and the log line.
    /// </summary>
    public Version? RecommendedMinimumSafeVersion()
    {
        Version? highest = null;
        foreach (var entry in Entries)
        {
            if (entry.FixedIn is null) continue;
            if (highest is null || entry.FixedIn > highest) highest = entry.FixedIn;
        }
        return highest;
    }

    private static TeamViewerCveRegistry LoadDefault()
    {
        var asm = typeof(TeamViewerCveRegistry).Assembly;
        using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return Empty($"Embedded CVE registry resource '{EmbeddedResourceName}' not found in {asm.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    private static (TeamViewerVersionRange? Range, string? Error) TryParseRange(VersionRangeDto raw)
    {
        Version? min = null;
        Version? max = null;

        if (!string.IsNullOrWhiteSpace(raw.MinInclusive))
        {
            if (!Version.TryParse(raw.MinInclusive, out min))
                return (null, $"min_inclusive '{raw.MinInclusive}' is not a valid version.");
        }

        if (!string.IsNullOrWhiteSpace(raw.MaxExclusive))
        {
            if (!Version.TryParse(raw.MaxExclusive, out max))
                return (null, $"max_exclusive '{raw.MaxExclusive}' is not a valid version.");
        }

        if (min is null && max is null)
            return (null, "range had neither min_inclusive nor max_exclusive.");

        if (min is not null && max is not null && min >= max)
            return (null, $"min_inclusive '{min}' is not strictly less than max_exclusive '{max}'.");

        return (new TeamViewerVersionRange(min, max), null);
    }

    // -------- DTOs --------

    private sealed class RegistryDto
    {
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("last_updated")] public string? LastUpdated { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("entries")] public CveEntryDto?[]? Entries { get; set; }
    }

    private sealed class CveEntryDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("cvss")] public double Cvss { get; set; }
        [JsonPropertyName("severity")] public string? Severity { get; set; }
        [JsonPropertyName("published")] public string? Published { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("remediation")] public string? Remediation { get; set; }
        [JsonPropertyName("remediation_url")] public string? RemediationUrl { get; set; }
        [JsonPropertyName("fixed_in")] public string? FixedIn { get; set; }
        [JsonPropertyName("affected")] public VersionRangeDto?[]? Affected { get; set; }
    }

    private sealed class VersionRangeDto
    {
        [JsonPropertyName("min_inclusive")] public string? MinInclusive { get; set; }
        [JsonPropertyName("max_exclusive")] public string? MaxExclusive { get; set; }
    }
}

/// <summary>
/// Single TeamViewer CVE entry parsed from the registry. Affected versions
/// are expressed as one or more half-open <see cref="TeamViewerVersionRange"/>s.
/// </summary>
public sealed record TeamViewerCveEntry(
    string Id,
    string Title,
    double Cvss,
    string Severity,
    string Published,
    string Summary,
    string Remediation,
    string RemediationUrl,
    Version? FixedIn,
    ImmutableArray<TeamViewerVersionRange> Affected);

/// <summary>
/// Half-open version range. <see cref="MinInclusive"/> may be null
/// (no lower bound); <see cref="MaxExclusive"/> may be null (no upper bound);
/// at least one is present (enforced at parse time).
/// </summary>
public readonly record struct TeamViewerVersionRange(Version? MinInclusive, Version? MaxExclusive)
{
    public bool Contains(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (MinInclusive is not null && version < MinInclusive) return false;
        if (MaxExclusive is not null && version >= MaxExclusive) return false;
        return true;
    }
}
