using System.IO;
using System.Text.Json;
using TeamStation.App.Services;
using TeamStation.App.ViewModels;

namespace TeamStation.Tests;

public class ActivityLogJsonExporterTests
{
    [Fact]
    public void BuildNdjson_writes_one_single_line_json_object_per_activity_event()
    {
        var entries = new[]
        {
            new LogEntry(new DateTimeOffset(2026, 4, 25, 16, 30, 45, TimeSpan.FromHours(-4)), LogLevel.Info, "Started"),
            new LogEntry(new DateTimeOffset(2026, 4, 25, 16, 31, 10, TimeSpan.FromHours(-4)), LogLevel.Warning, "Line one\nLine two"),
        };

        var ndjson = ActivityLogJsonExporter.BuildNdjson(entries);

        using var reader = new StringReader(ndjson);
        var firstLine = reader.ReadLine();
        var secondLine = reader.ReadLine();
        var end = reader.ReadLine();

        Assert.NotNull(firstLine);
        Assert.NotNull(secondLine);
        Assert.Null(end);

        using var first = JsonDocument.Parse(firstLine!);
        using var second = JsonDocument.Parse(secondLine!);

        Assert.Equal(ActivityLogJsonExporter.Schema, first.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1, first.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal("2026-04-25T20:30:45+00:00", first.RootElement.GetProperty("@timestamp").GetString());
        Assert.Equal("info", first.RootElement.GetProperty("level").GetString());
        Assert.Equal("Started", first.RootElement.GetProperty("message").GetString());
        Assert.Equal("TeamStation.ActivityLog", first.RootElement.GetProperty("source").GetString());

        Assert.Equal(2, second.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal("warning", second.RootElement.GetProperty("level").GetString());
        Assert.Equal("Line one\nLine two", second.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void BuildNdjson_supports_empty_activity_logs()
    {
        Assert.Equal(string.Empty, ActivityLogJsonExporter.BuildNdjson(Array.Empty<LogEntry>()));
    }
}
