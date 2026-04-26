using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// Tests for <see cref="AuditLogExporter"/> — CSV and NDJSON output.
/// Also verifies <see cref="AuditLogRepository.GetAll"/> ordering and completeness.
/// </summary>
public sealed class AuditLogExporterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly CryptoService _crypto;
    private readonly AuditLogRepository _audit;

    public AuditLogExporterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ts-export-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _crypto = CryptoService.CreateOrLoad(_db);
        _audit = new AuditLogRepository(_db, _crypto);
    }

    public void Dispose()
    {
        _crypto.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private AuditEvent AppendEvent(string action = "Test", string? detail = null)
    {
        var evt = new AuditEvent
        {
            Action = action,
            TargetType = "Machine",
            TargetId = Guid.NewGuid(),
            Summary = $"Summary for {action}",
            Detail = detail,
        };
        _audit.Append(evt);
        return evt;
    }

    // ------------------------------------------------------------------
    // GetAll
    // ------------------------------------------------------------------

    [Fact]
    public void GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        var results = _audit.GetAll();
        Assert.Empty(results);
    }

    [Fact]
    public void GetAll_ReturnsAllRowsOldestFirst()
    {
        var a = AppendEvent("A");
        var b = AppendEvent("B");
        var c = AppendEvent("C");

        var all = _audit.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(a.Id, all[0].Id);
        Assert.Equal(b.Id, all[1].Id);
        Assert.Equal(c.Id, all[2].Id);
    }

    [Fact]
    public void GetAll_ReturnsMoreThanGetRecentLimit()
    {
        // GetRecent default limit is 250; GetAll must return everything.
        for (var i = 0; i < 260; i++)
            AppendEvent($"Bulk_{i}");

        var all = _audit.GetAll();
        Assert.Equal(260, all.Count);
    }

    // ------------------------------------------------------------------
    // NDJSON
    // ------------------------------------------------------------------

    [Fact]
    public void WriteNdjson_EmptyInput_ProducesNoOutput()
    {
        var sb = new StringBuilder();
        AuditLogExporter.WriteNdjson([], new StringWriter(sb));
        Assert.Equal(string.Empty, sb.ToString());
    }

    [Fact]
    public void WriteNdjson_SingleRow_ValidJsonWithSchemaField()
    {
        var evt = AppendEvent("Connect");
        var sb = new StringBuilder();
        AuditLogExporter.WriteNdjson([evt], new StringWriter(sb));
        var json = sb.ToString();

        // Must be exactly one line (no trailing newline after last record)
        Assert.DoesNotContain('\n', json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(AuditLogExporter.NdjsonSchema, root.GetProperty("schema").GetString());
        Assert.Equal(evt.Id.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(evt.Action, root.GetProperty("action").GetString());
        Assert.Equal(evt.TargetType, root.GetProperty("target_type").GetString());
        Assert.Equal(evt.Summary, root.GetProperty("summary").GetString());
        // TargetId is non-null, must be present
        Assert.Equal(evt.TargetId?.ToString("D"), root.GetProperty("target_id").GetString());
    }

    [Fact]
    public void WriteNdjson_NullOptionalFields_OmittedFromOutput()
    {
        var evt = new AuditEvent
        {
            Action = "NoDetail",
            TargetType = "Machine",
            TargetId = null,    // nullable
            Summary = "No target, no detail",
            Detail = null,      // nullable
        };
        _audit.Append(evt);

        var sb = new StringBuilder();
        AuditLogExporter.WriteNdjson([evt], new StringWriter(sb));
        var json = sb.ToString();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("target_id", out _), "target_id should be absent when null");
        Assert.False(root.TryGetProperty("detail", out _), "detail should be absent when null");
    }

    [Fact]
    public void WriteNdjson_MultipleRows_OneJsonObjectPerLine()
    {
        AppendEvent("A");
        AppendEvent("B");
        AppendEvent("C");

        var events = _audit.GetAll();
        var sb = new StringBuilder();
        AuditLogExporter.WriteNdjson(events, new StringWriter(sb));
        var output = sb.ToString();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(AuditLogExporter.NdjsonSchema, doc.RootElement.GetProperty("schema").GetString());
        }
    }

    // ------------------------------------------------------------------
    // CSV
    // ------------------------------------------------------------------

    [Fact]
    public void WriteCsv_EmptyInput_ProducesOnlyHeader()
    {
        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([], new StringWriter(sb));
        var output = sb.ToString().TrimStart('\uFEFF'); // strip BOM

        var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("id,occurred_utc,action,target_type,target_id,summary,detail", lines[0]);
    }

    [Fact]
    public void WriteCsv_StartsWithUtf8Bom()
    {
        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([], new StringWriter(sb));
        Assert.Equal('\uFEFF', sb[0]);
    }

    [Fact]
    public void WriteCsv_SingleRow_CorrectColumnCount()
    {
        var evt = AppendEvent("Export");
        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([evt], new StringWriter(sb));
        var output = sb.ToString().TrimStart('\uFEFF');

        var lines = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // header + 1 data row

        // 7 columns in data row (target_id and detail may be empty but present)
        Assert.Equal(7, lines[1].Split(',').Length);
    }

    [Fact]
    public void WriteCsv_ValuesWithCommas_AreDoubleQuoted()
    {
        var evt = new AuditEvent
        {
            Action = "Export",
            TargetType = "Machine",
            Summary = "Contains, a comma",
            Detail = null,
        };
        _audit.Append(evt);

        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([evt], new StringWriter(sb));
        var output = sb.ToString().TrimStart('\uFEFF');
        var dataLine = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

        Assert.Contains("\"Contains, a comma\"", dataLine);
    }

    [Fact]
    public void WriteCsv_ValuesWithDoubleQuotes_AreEscaped()
    {
        var evt = new AuditEvent
        {
            Action = "Export",
            TargetType = "Machine",
            Summary = "He said \"hello\"",
            Detail = null,
        };
        _audit.Append(evt);

        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([evt], new StringWriter(sb));
        var output = sb.ToString().TrimStart('\uFEFF');
        var dataLine = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

        Assert.Contains("\"He said \"\"hello\"\"\"", dataLine);
    }

    [Fact]
    public void WriteCsv_NullOptionalFields_ProduceEmptyCells()
    {
        var evt = new AuditEvent
        {
            Action = "NoTarget",
            TargetType = "Machine",
            TargetId = null,
            Summary = "No target",
            Detail = null,
        };
        _audit.Append(evt);

        var sb = new StringBuilder();
        AuditLogExporter.WriteCsv([evt], new StringWriter(sb));
        var output = sb.ToString().TrimStart('\uFEFF');
        var dataLine = output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

        // target_id and detail columns must exist but be empty
        var cols = dataLine.Split(',');
        Assert.Equal(7, cols.Length);
        Assert.Equal(string.Empty, cols[4]); // target_id
        Assert.Equal(string.Empty, cols[6]); // detail
    }
}
