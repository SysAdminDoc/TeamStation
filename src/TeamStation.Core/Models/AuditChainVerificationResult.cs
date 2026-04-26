namespace TeamStation.Core.Models;

/// <summary>
/// Result of a full HMAC-chain scan of the <c>audit_log</c> table.
/// A clean chain means no row has been retroactively modified or deleted
/// since the HMAC columns were introduced (schema v4).
///
/// Rows written before the v4 migration do not carry HMACs and are reported
/// via <see cref="LegacyRowsSkipped"/> — they cannot be verified but their
/// presence is expected and does not invalidate newer rows.
/// </summary>
public sealed record AuditChainVerificationResult(
    bool IsValid,
    int RowsVerified,
    int LegacyRowsSkipped,
    string? FailedAtId,
    string? Reason)
{
    /// <summary>
    /// Returns a human-readable one-line summary suitable for log output or
    /// the <c>--verify-audit-chain</c> CLI report.
    /// </summary>
    public string Summary =>
        IsValid
            ? $"PASS — {RowsVerified} row(s) verified, {LegacyRowsSkipped} legacy row(s) skipped (no HMAC)."
            : $"FAIL — tampered row detected at id={FailedAtId ?? "unknown"}: {Reason}";
}
