using System.Security.Cryptography;
using System.Text;

namespace TeamStation.Data.Security;

/// <summary>
/// Purpose-string layer on top of the per-database DPAPI entropy salt
/// introduced in v0.3.3.
///
/// <para>
/// Both the credential-store DEK wrap (<see cref="CredentialStore"/>) and the
/// settings API-token wrap (<see cref="Settings"/>) use the same 32-byte
/// per-database base entropy. Without further differentiation a DPAPI blob
/// produced by one component could be replayed against the other component's
/// <c>ProtectedData.Unprotect</c> call.
/// </para>
///
/// <para>
/// <see cref="Derive"/> hashes the base entropy together with a purpose string
/// (<c>baseEntropy ‖ UTF-8(purpose)</c> → SHA-256) to produce a 32-byte
/// derived entropy value that is unique to each site. A blob protected under
/// <see cref="CredentialStore"/> entropy cannot be unprotected using
/// <see cref="Settings"/> entropy, and vice versa.
/// </para>
///
/// <para>
/// <b>Backward-compatibility guarantee</b>: wraps made before this layer
/// existed (v0.3.3 for the DEK, v0.3.4 for the API token) used the raw base
/// entropy. Both callers retain a 3-tier fallback chain:
/// purpose-entropy → base-entropy → null-entropy (pre-v0.3.3).
/// A successful legacy read triggers a silent re-wrap under the new
/// purpose-entropy so subsequent reads take the fast path.
/// </para>
/// </summary>
public static class DpapiPurposeEntropy
{
    /// <summary>
    /// Purpose string for <c>CryptoService</c> — wraps the per-database DEK.
    /// </summary>
    public const string CredentialStore = "TeamStation.CredentialStore";

    /// <summary>
    /// Purpose string for <c>SettingsService</c> — wraps the TeamViewer API token.
    /// </summary>
    public const string Settings = "TeamStation.Settings";

    /// <summary>
    /// Derives a 32-byte purpose-specific DPAPI entropy value from
    /// <paramref name="baseEntropy"/> and <paramref name="purpose"/>.
    /// Computation: <c>SHA-256(baseEntropy ‖ UTF-8(purpose))</c>.
    /// The result is deterministic and reversible only by knowing both inputs.
    /// </summary>
    /// <param name="baseEntropy">The per-database 32-byte random entropy salt.</param>
    /// <param name="purpose">One of the purpose-string constants on this class.</param>
    public static byte[] Derive(byte[] baseEntropy, string purpose)
    {
        ArgumentNullException.ThrowIfNull(baseEntropy);
        ArgumentException.ThrowIfNullOrEmpty(purpose);

        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var combined = new byte[baseEntropy.Length + purposeBytes.Length];
        baseEntropy.CopyTo(combined, 0);
        purposeBytes.CopyTo(combined, baseEntropy.Length);
        return SHA256.HashData(combined);
    }
}
