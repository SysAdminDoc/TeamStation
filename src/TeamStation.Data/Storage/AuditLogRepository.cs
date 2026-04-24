using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;

namespace TeamStation.Data.Storage;

[SupportedOSPlatform("windows")]
public sealed class AuditLogRepository
{
    private readonly Database _db;

    public AuditLogRepository(Database db)
    {
        _db = db;
    }

    public void Append(AuditEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_log(id, occurred_utc, action, target_type, target_id, summary, detail)
VALUES ($id, $occurred, $action, $target_type, $target_id, $summary, $detail);";
        cmd.Parameters.AddWithValue("$id", evt.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$occurred", evt.OccurredUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$action", evt.Action);
        cmd.Parameters.AddWithValue("$target_type", evt.TargetType);
        cmd.Parameters.AddWithValue("$target_id", (object?)evt.TargetId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$summary", evt.Summary);
        cmd.Parameters.AddWithValue("$detail", (object?)evt.Detail ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEvent> GetRecent(int limit = 250)
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        // Secondary sort on id so events that share a millisecond (common in
        // fast bulk-import flows) still return in a stable, deterministic order.
        cmd.CommandText = @"
SELECT id, occurred_utc, action, target_type, target_id, summary, detail
FROM audit_log
ORDER BY occurred_utc DESC, id DESC
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        using var reader = cmd.ExecuteReader();
        var events = new List<AuditEvent>();
        while (reader.Read())
        {
            events.Add(new AuditEvent
            {
                Id = Guid.Parse(reader.GetString(0)),
                OccurredUtc = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                Action = reader.GetString(2),
                TargetType = reader.GetString(3),
                TargetId = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                Summary = reader.GetString(5),
                Detail = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }

        return events;
    }

    /// <summary>
    /// Deletes audit events older than <paramref name="retention"/>. Returns the
    /// number of rows removed. Safe no-op for non-positive retention.
    /// </summary>
    public int Prune(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero) return 0;
        var cutoff = DateTimeOffset.UtcNow - retention;
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM audit_log WHERE occurred_utc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        return cmd.ExecuteNonQuery();
    }
}
