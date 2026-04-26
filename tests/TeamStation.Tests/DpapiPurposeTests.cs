using System.Security.Cryptography;
using System.Text;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// v0.4.0 hardening: <see cref="DpapiPurposeEntropy"/> domain-separates the
/// credential-store and settings DPAPI wraps so that a blob from one site
/// cannot be replayed against the other site's <c>Unprotect</c> call.
/// </summary>
public class DpapiPurposeTests
{
    // -----------------------------------------------------------------------
    // DpapiPurposeEntropy.Derive — unit tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Derive_is_deterministic_for_same_inputs()
    {
        var base1 = new byte[32];
        RandomNumberGenerator.Fill(base1);

        var d1 = DpapiPurposeEntropy.Derive(base1, DpapiPurposeEntropy.CredentialStore);
        var d2 = DpapiPurposeEntropy.Derive(base1, DpapiPurposeEntropy.CredentialStore);

        Assert.Equal(d1, d2);
    }

    [Fact]
    public void Derive_produces_32_bytes()
    {
        var baseEntropy = RandomNumberGenerator.GetBytes(32);
        var result = DpapiPurposeEntropy.Derive(baseEntropy, "any.purpose");
        Assert.Equal(32, result.Length);
    }

    [Fact]
    public void Derive_differs_for_different_purposes()
    {
        var baseEntropy = RandomNumberGenerator.GetBytes(32);
        var credStore = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.CredentialStore);
        var settings  = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.Settings);

        Assert.NotEqual(credStore, settings);
    }

    [Fact]
    public void Derive_differs_for_different_base_entropy()
    {
        var base1 = RandomNumberGenerator.GetBytes(32);
        var base2 = RandomNumberGenerator.GetBytes(32);

        var d1 = DpapiPurposeEntropy.Derive(base1, DpapiPurposeEntropy.CredentialStore);
        var d2 = DpapiPurposeEntropy.Derive(base2, DpapiPurposeEntropy.CredentialStore);

        Assert.NotEqual(d1, d2);
    }

    // -----------------------------------------------------------------------
    // Domain-separation: cross-component replay must fail
    // -----------------------------------------------------------------------

    [Fact]
    public void CredentialStore_wrap_cannot_be_unprotected_with_Settings_entropy()
    {
        var baseEntropy = RandomNumberGenerator.GetBytes(32);
        var credEntropy = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.CredentialStore);
        var settEntropy = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.Settings);

        var plain   = RandomNumberGenerator.GetBytes(32);
        var wrapped = ProtectedData.Protect(plain, credEntropy, DataProtectionScope.CurrentUser);

        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrapped, settEntropy, DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void Settings_wrap_cannot_be_unprotected_with_CredentialStore_entropy()
    {
        var baseEntropy = RandomNumberGenerator.GetBytes(32);
        var credEntropy = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.CredentialStore);
        var settEntropy = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.Settings);

        var plain   = Encoding.UTF8.GetBytes("super-secret-token");
        var wrapped = ProtectedData.Protect(plain, settEntropy, DataProtectionScope.CurrentUser);

        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrapped, credEntropy, DataProtectionScope.CurrentUser));
    }

    [Fact]
    public void Wrap_under_purpose_entropy_fails_with_raw_base_entropy()
    {
        var baseEntropy = RandomNumberGenerator.GetBytes(32);
        var purposeEntropy = DpapiPurposeEntropy.Derive(baseEntropy, DpapiPurposeEntropy.CredentialStore);

        var plain   = RandomNumberGenerator.GetBytes(32);
        var wrapped = ProtectedData.Protect(plain, purposeEntropy, DataProtectionScope.CurrentUser);

        // Raw base entropy (no derivation) must not decrypt the purpose-bound wrap.
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(wrapped, baseEntropy, DataProtectionScope.CurrentUser));
    }
}
