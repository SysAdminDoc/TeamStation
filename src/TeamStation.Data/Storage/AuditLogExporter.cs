using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamStation.Core.Models;

namespace TeamStation.Data.Storage;

/// <summary>
/// Writes audit-log rows to a <see cref="TextWriter"/> in either newline-delimited
/// JSON (<c>--format=ndjson</c>, default) or RFC 4180 CSV (<c>--format=csv</c>).
///
/// Both formats are designed for direct ingestion by log-pipeline tools
/// (Splunk, Elastic/OpenSearch, Datadog, Loki) without any proprietary headers
/// or binary framing.
///
/// <b>NDJSON schema</b>: <c>teamstation.audit.v1</c> — one JSON object per line,
/// UTF-8, no BOM.  Fields: <c>schema</c>, <c>id</c>, <c>occurred_utc</c>,
/// <c>action</c>, <c>target_type</c>, <c>target_id</c>?, <c>summary</c>,
/// <c>detail</c>?.
///
/// <b>CSV schema</b>: RFC 4180, UTF-8 with BOM (for Excel compatibility).
/// First row is the header.  NULL fields are empty cells.  Values containing
/// commas, double-quotes, or line-breaks are double-quoted with internal
/// double-quotes doubled.
/// </summary>
public static class AuditLogExporter
{
    /// <summary>
    /// NDJSON schema identifier embedded in every output record.
    /// Bump this string if the field set changes incompatibly.
    /// </summary>
    public const string NdjsonSchema = "teamstation.audit.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ------------------------------------------------------------------
    // NDJSON
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes one UTF-8 JSON object per line, no trailing newline after the
    /// last record, no BOM. Safe to stream directly to <c>Console.Out</c> or
    /// a <see cref="StreamWriter"/> opened over a file.
    /// </summary>
    public static void WriteNdjson(IEnumerable<AuditEvent> events, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(writer);

        var first = true;
        foreach (var evt in events)
        {
            if (!first) writer.WriteLine();
            first = false;
            var record = AuditNdjsonRecord.From(evt);
            writer.Write(JsonSerializer.Serialize(record, JsonOptions));
        }
    }

    // ------------------------------------------------------------------
    // CSV
    // ------------------------------------------------------------------

    private static readonly string[] CsvHeader =
        ["id", "occurred_utc", "action", "target_type", "target_id", "summary", "detail"];

    /// <summary>
    /// Writes RFC 4180 CSV with a UTF-8 BOM (for Excel compatibility) and
    /// CRLF line endings. The first row is the column header.
    /// </summary>
    public static void WriteCsv(IEnumerable<AuditEvent> events, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(writer);

        // BOM — improves direct-open compatibility with Excel and Power BI
        // without breaking UTF-8-aware tooling.
        writer.Write('\uFEFF');

        writer.WriteLine(string.Join(",", CsvHeader));

        foreach (var evt in events)
        {
            writer.Write(Escape(evt.Id.ToString("D"))); writer.Write(',');
            writer.Write(Escape(evt.OccurredUtc.ToString("O"))); writer.Write(',');
            writer.Write(Escape(evt.Action)); writer.Write(',');
            writer.Write(Escape(evt.TargetType)); writer.Write(',');
            writer.Write(Escape(evt.TargetId?.ToString("D"))); writer.Write(',');
            writer.Write(Escape(evt.Summary)); writer.Write(',');
            writer.WriteLine(Escape(evt.Detail));
        }
    }

    /// <summary>
    /// RFC 4180 field escaping: wrap in double-quotes if the value contains a
    /// comma, double-quote, CR, or LF; double any embedded double-quotes.
    /// Returns an empty string for <see langword="null"/> values.
    /// </summary>
    private static string Escape(string? value)
    {
        if (value is null) return string.Empty;
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // ------------------------------------------------------------------
    // NDJSON DTO
    // ------------------------------------------------------------------

    private sealed class AuditNdjsonRecord
    {
        [JsonPropertyName("schema")]
        public string Schema { get; init; } = NdjsonSchema;

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("occurred_utc")]
        public string OccurredUtc { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; init; } = string.Empty;

        [JsonPropertyName("target_type")]
        public string TargetType { get; init; } = string.Empty;

        [JsonPropertyName("target_id")]
        public string? TargetId { get; init; }

        [JsonPropertyName("summary")]
        public string Summary { get; init; } = string.Empty;

        [JsonPropertyName("detail")]
        public string? Detail { get; init; }

        public static AuditNdjsonRecord From(AuditEvent evt) => new()
        {
            Id = evt.Id.ToString("D"),
            OccurredUtc = evt.OccurredUtc.ToString("O"),
            Action = evt.Action,
            TargetType = evt.TargetType,
            TargetId = evt.TargetId?.ToString("D"),
            Summary = evt.Summary,
            Detail = evt.Detail,
        };
    }
}
