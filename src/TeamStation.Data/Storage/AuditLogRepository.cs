using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;

namespace TeamStation.Data.Storage;

[SupportedOSPlatform("windows")]
public sealed class AuditLogRepository
{
    private readonly Database _db;
    private readonly CryptoService? _crypto;

    public AuditLogRepository(Database db, CryptoService? crypto = null)
    {
        _db = db;
        _crypto = crypto;
    }

    public void Append(AuditEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var c = _db.OpenConnection();
        using var tx = c.BeginTransaction();

        var id = evt.Id.ToString("D");
        var occurred = evt.OccurredUtc.ToString("O", CultureInfo.InvariantCulture);
        var targetId = evt.TargetId?.ToString("D");

        byte[]? prevHash = null;
        byte[]? rowHash = null;

        if (_crypto is not null)
        {
            // Read the row_hash of the most-recently-inserted HMAC row so we
            // can chain to it.  We order by rowid (physical insertion order)
            // rather than occurred_utc to be immune to clock skew.
            using var hashCmd = c.CreateCommand();
            hashCmd.Transaction = tx;
            hashCmd.CommandText = @"
SELECT row_hash
FROM   audit_log
WHERE  row_hash IS NOT NULL
ORDER  BY rowid DESC
LIMIT  1;";
            var raw = hashCmd.ExecuteScalar();
            // Genesis: if no HMAC row exists yet, prev_hash is 32 zero bytes.
            prevHash = raw is byte[] b ? b : new byte[32];

            var message = BuildCanonicalMessage(
                prevHash, id, occurred,
                evt.Action, evt.TargetType, targetId,
                evt.Summary, evt.Detail);
            rowHash = _crypto.ComputeHmac(message);
        }

        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO audit_log
    (id, occurred_utc, action, target_type, target_id, summary, detail, prev_hash, row_hash)
VALUES
    ($id, $occurred, $action, $target_type, $target_id, $summary, $detail, $prev_hash, $row_hash);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$occurred", occurred);
        cmd.Parameters.AddWithValue("$action", evt.Action);
        cmd.Parameters.AddWithValue("$target_type", evt.TargetType);
        cmd.Parameters.AddWithValue("$target_id", (object?)targetId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$summary", evt.Summary);
        cmd.Parameters.AddWithValue("$detail", (object?)evt.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prev_hash", (object?)prevHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$row_hash", (object?)rowHash ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Returns every row in the audit log in chronological order (oldest first).
    /// Unlike <see cref="GetRecent"/> this has no row cap; use for export paths.
    /// </summary>
    public IReadOnlyList<AuditEvent> GetAll()
    {
        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, occurred_utc, action, target_type, target_id, summary, detail
FROM audit_log
ORDER BY occurred_utc ASC, id ASC;";
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

    public IReadOnlyList<AuditEvent> GetRecent(int limit = 250)    {
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

    /// <summary>
    /// Reads every row in insertion order (<c>rowid ASC</c>) and verifies that
    /// the HMAC chain is intact. Rows without a <c>row_hash</c> (written before
    /// the schema-v4 migration) are counted as <em>legacy skips</em> and do not
    /// invalidate the chain.
    /// </summary>
    /// <returns>
    /// A <see cref="AuditChainVerificationResult"/> indicating pass/fail, counts,
    /// and — on failure — the ID of the first broken row with a reason string.
    /// If no crypto service was provided, returns an immediate failure describing
    /// why verification is unavailable.
    /// </returns>
    public AuditChainVerificationResult VerifyChain()
    {
        if (_crypto is null)
            return new AuditChainVerificationResult(
                IsValid: false, RowsVerified: 0, LegacyRowsSkipped: 0,
                FailedAtId: null,
                Reason: "No CryptoService — HMAC chain cannot be verified. " +
                        "Re-open the repository using App.OnStartup to provide crypto.");

        using var c = _db.OpenConnection();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT id, occurred_utc, action, target_type, target_id, summary, detail,
       prev_hash, row_hash
FROM   audit_log
ORDER  BY rowid ASC;";
        using var reader = cmd.ExecuteReader();

        var rowsVerified = 0;
        var legacySkipped = 0;
        byte[]? expectedPrevHash = null; // null = haven't seen an HMAC row yet

        while (reader.Read())
        {
            var id = reader.GetString(0);
            var occurredUtc = reader.GetString(1);
            var action = reader.GetString(2);
            var targetType = reader.GetString(3);
            var targetId = reader.IsDBNull(4) ? null : reader.GetString(4);
            var summary = reader.GetString(5);
            var detail = reader.IsDBNull(6) ? null : reader.GetString(6);
            var prevHashDb = reader.IsDBNull(7) ? null : (byte[])reader[7];
            var rowHashDb = reader.IsDBNull(8) ? null : (byte[])reader[8];

            if (rowHashDb is null)
            {
                // Pre-v4 row — no HMAC present, skip gracefully.
                legacySkipped++;
                continue;
            }

            // Genesis prev_hash is 32 zero bytes; subsequent rows chain via the
            // previous row's row_hash.
            var genesisHash = new byte[32];
            var myExpectedPrev = expectedPrevHash ?? genesisHash;

            if (prevHashDb is null || !prevHashDb.SequenceEqual(myExpectedPrev))
                return new AuditChainVerificationResult(
                    IsValid: false, RowsVerified: rowsVerified,
                    LegacyRowsSkipped: legacySkipped, FailedAtId: id,
                    Reason: "prev_hash mismatch — a row may have been inserted or deleted from the chain.");

            var message = BuildCanonicalMessage(
                myExpectedPrev, id, occurredUtc,
                action, targetType, targetId,
                summary, detail);
            var expectedRowHash = _crypto.ComputeHmac(message);

            if (!CryptographicOperations.FixedTimeEquals(rowHashDb, expectedRowHash))
                return new AuditChainVerificationResult(
                    IsValid: false, RowsVerified: rowsVerified,
                    LegacyRowsSkipped: legacySkipped, FailedAtId: id,
                    Reason: "row_hash HMAC mismatch — this row's content has been retroactively altered.");

            expectedPrevHash = rowHashDb;
            rowsVerified++;
        }

        return new AuditChainVerificationResult(
            IsValid: true, RowsVerified: rowsVerified,
            LegacyRowsSkipped: legacySkipped,
            FailedAtId: null, Reason: null);
    }

    // -------------------------------------------------------------------------
    // HMAC canonical message helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a deterministic, collision-resistant byte sequence that uniquely
    /// describes one audit row. Format:
    /// <list type="bullet">
    ///   <item>32 bytes — <paramref name="prevHash"/> (all-zero for genesis)</item>
    ///   <item>For each field in fixed order: 4-byte LE int32 length + UTF-8 bytes.
    ///         NULL fields use sentinel length 0xFFFFFFFF with zero content bytes.</item>
    /// </list>
    /// Field order: id, occurred_utc, action, target_type, target_id, summary, detail.
    /// </summary>
    private static byte[] BuildCanonicalMessage(
        ReadOnlySpan<byte> prevHash,
        string id, string occurredUtc,
        string action, string targetType, string? targetId,
        string summary, string? detail)
    {
        using var ms = new MemoryStream(256);
        ms.Write(prevHash);
        WriteField(ms, id);
        WriteField(ms, occurredUtc);
        WriteField(ms, action);
        WriteField(ms, targetType);
        WriteNullableField(ms, targetId);
        WriteField(ms, summary);
        WriteNullableField(ms, detail);
        return ms.ToArray();
    }

    private static void WriteField(MemoryStream s, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        s.Write(BitConverter.GetBytes(bytes.Length));
        s.Write(bytes);
    }

    private static void WriteNullableField(MemoryStream s, string? value)
    {
        if (value is null)
        {
            s.Write(BitConverter.GetBytes(uint.MaxValue)); // sentinel for NULL
            return;
        }
        WriteField(s, value);
    }
}
