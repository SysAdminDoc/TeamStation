using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Services;

/// <summary>
/// Serializes the transient UI activity log as newline-delimited JSON for
/// operators who want to hand the panel contents to log pipelines.
/// </summary>
public static class ActivityLogJsonExporter
{
    public const string Schema = "teamstation.activity.v1";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildNdjson(IEnumerable<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        var sequence = 0;
        foreach (var entry in entries)
        {
            var record = ActivityLogRecord.From(++sequence, entry);
            builder.Append(JsonSerializer.Serialize(record, Options));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private sealed class ActivityLogRecord
    {
        [JsonPropertyName("schema")]
        public string Schema { get; init; } = ActivityLogJsonExporter.Schema;

        [JsonPropertyName("sequence")]
        public int Sequence { get; init; }

        [JsonPropertyName("@timestamp")]
        public DateTimeOffset TimestampUtc { get; init; }

        [JsonPropertyName("level")]
        public string Level { get; init; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; init; } = "TeamStation.ActivityLog";

        public static ActivityLogRecord From(int sequence, LogEntry entry) => new()
        {
            Sequence = sequence,
            TimestampUtc = entry.At.ToUniversalTime(),
            Level = NormalizeLevel(entry.Level),
            Message = entry.Message,
        };

        private static string NormalizeLevel(LogLevel level) => level switch
        {
            LogLevel.Success => "success",
            LogLevel.Warning => "warning",
            LogLevel.Error => "error",
            _ => "info",
        };
    }
}
