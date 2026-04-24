using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;

namespace TeamStation.Data.Storage;

[SupportedOSPlatform("windows")]
public sealed class SessionRepository
{
    private readonly Database _db;

    public SessionRepository(Database db)
    {
        _db = db;
    }

    public IReadOnlyList<SessionRecord> GetRecent(int limit = 100)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, entry_id, entry_name, tv_id, profile_name, mode, route, process_id,
       started_utc, ended_utc, notes, outcome
FROM session_history
ORDER BY started_utc DESC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var reader = cmd.ExecuteReader();
        var records = new List<SessionRecord>();
        while (reader.Read())
            records.Add(Materialize(reader));
        return records;
    }

    public void Upsert(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO session_history(
    id, entry_id, entry_name, tv_id, profile_name, mode, route, process_id,
    started_utc, ended_utc, notes, outcome)
VALUES (
    $id, $entry_id, $entry_name, $tv_id, $profile, $mode, $route, $pid,
    $started, $ended, $notes, $outcome)
ON CONFLICT(id) DO UPDATE SET
    ended_utc = excluded.ended_utc,
    notes     = excluded.notes,
    outcome   = excluded.outcome;";
        Bind(cmd, record);
        cmd.ExecuteNonQuery();
    }

    public void Complete(Guid id, DateTimeOffset endedUtc, string outcome)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
UPDATE session_history
SET ended_utc = $ended,
    outcome = $outcome
WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.Parameters.AddWithValue("$ended", endedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$outcome", outcome);
        cmd.ExecuteNonQuery();
    }

    public void ExportCsv(string path)
    {
        var lines = new List<string>
        {
            "StartedUtc,EndedUtc,DurationMinutes,EntryName,TeamViewerId,ProfileName,Mode,Route,Outcome,Notes"
        };

        foreach (var r in GetRecent(1000).OrderBy(r => r.StartedUtc))
        {
            var duration = r.Duration?.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
            lines.Add(string.Join(',',
                Csv(r.StartedUtc.ToString("O", CultureInfo.InvariantCulture)),
                Csv(r.EndedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
                Csv(duration),
                Csv(r.EntryName),
                Csv(r.TeamViewerId),
                Csv(r.ProfileName),
                Csv(r.Mode?.ToString() ?? string.Empty),
                Csv(r.Route),
                Csv(r.Outcome ?? string.Empty),
                Csv(r.Notes ?? string.Empty)));
        }

        File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// Deletes session records older than <paramref name="retention"/>.
    /// Safe no-op when the argument is non-positive. Returns the number of
    /// rows removed.
    /// </summary>
    public int Prune(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero) return 0;
        var cutoff = DateTimeOffset.UtcNow - retention;
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM session_history WHERE started_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand cmd, SessionRecord record)
    {
        cmd.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$entry_id", (object?)record.EntryId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$entry_name", record.EntryName);
        cmd.Parameters.AddWithValue("$tv_id", record.TeamViewerId);
        cmd.Parameters.AddWithValue("$profile", record.ProfileName);
        cmd.Parameters.AddWithValue("$mode", (object?)record.Mode is null ? DBNull.Value : (int)record.Mode);
        cmd.Parameters.AddWithValue("$route", record.Route);
        cmd.Parameters.AddWithValue("$pid", (object?)record.ProcessId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$started", record.StartedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$ended", (object?)record.EndedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)record.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$outcome", (object?)record.Outcome ?? DBNull.Value);
    }

    private static SessionRecord Materialize(SqliteDataReader r)
    {
        return new SessionRecord
        {
            Id = Guid.Parse(r.GetString(0)),
            EntryId = r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
            EntryName = r.GetString(2),
            TeamViewerId = r.GetString(3),
            ProfileName = r.GetString(4),
            Mode = r.IsDBNull(5) ? null : (ConnectionMode?)r.GetInt32(5),
            Route = r.GetString(6),
            ProcessId = r.IsDBNull(7) ? null : r.GetInt32(7),
            StartedUtc = DateTimeOffset.Parse(r.GetString(8), CultureInfo.InvariantCulture),
            EndedUtc = r.IsDBNull(9) ? null : DateTimeOffset.Parse(r.GetString(9), CultureInfo.InvariantCulture),
            Notes = r.IsDBNull(10) ? null : r.GetString(10),
            Outcome = r.IsDBNull(11) ? null : r.GetString(11),
        };
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
