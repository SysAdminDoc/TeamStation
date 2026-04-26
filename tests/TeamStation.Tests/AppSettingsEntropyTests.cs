using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamStation.App.Services;
using TeamStation.Data.Security;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.4 hardening: AppSettings.TeamViewerApiToken DPAPI wrap binds to a
/// per-database entropy salt held in the SQLite _meta table. Because
/// SettingsService.Load runs BEFORE the Database opens in App.OnStartup,
/// the unwrap is now lazy — Load leaves the protected blob in place; the
/// host calls UnprotectApiToken after pushing the salt in via the
/// Entropy property. Legacy v0.3.3-and-earlier wraps (made under null
/// entropy) keep working through the same fallback pattern as the DEK.
/// </summary>
public class AppSettingsEntropyTests : IDisposable
{
    private readonly string _path;

    public AppSettingsEntropyTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"ts-settings-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_does_not_eagerly_Unprotect_the_token_anymore()
    {
        // Seed a v0.3.3-style settings file: token wrapped under null entropy.
        var legacyToken = "tv_abc123";
        var protectedBlob = ProtectLegacy(legacyToken);
        WriteSettings(new
        {
            Theme = "Dark",
            HasAcceptedLaunchNotice = true,
            TeamViewerApiTokenProtected = protectedBlob,
        });

        var svc = new SettingsService(_path);
        var settings = svc.Load();

        // Load no longer eagerly unwraps; TeamViewerApiToken stays null until
        // the host pushes entropy in and calls UnprotectApiToken.
        Assert.Null(settings.TeamViewerApiToken);
        Assert.Equal(protectedBlob, settings.TeamViewerApiTokenProtected);
    }

    [Fact]
    public void UnprotectApiToken_with_entropy_round_trips_a_freshly_saved_token()
    {
        var entropy = RandomNumberGenerator.GetBytes(32);

        // Save under entropy.
        var saveSvc = new SettingsService(_path) { Entropy = entropy };
        var saveSettings = new AppSettings { TeamViewerApiToken = "tv_fresh_token" };
        saveSvc.Save(saveSettings);

        // Re-load with the same entropy. The protected blob is on disk; the
        // unwrap should succeed under the new entropy on the fast path.
        var loadSvc = new SettingsService(_path) { Entropy = entropy };
        var loadSettings = loadSvc.Load();
        Assert.Null(loadSettings.TeamViewerApiToken); // not yet unwrapped
        loadSvc.UnprotectApiToken(loadSettings);
        Assert.Equal("tv_fresh_token", loadSettings.TeamViewerApiToken);

        // The underlying ProtectedData wrap must be purpose-entropy-bound:
        // unwrapping with null entropy raises CryptographicException.
        var rawWrap = Convert.FromBase64String(loadSettings.TeamViewerApiTokenProtected!);
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(rawWrap, optionalEntropy: null, DataProtectionScope.CurrentUser));
        // Base entropy (without purpose derivation) must also fail.
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(rawWrap, entropy, DataProtectionScope.CurrentUser));
        // Succeeds with the purpose-derived entropy:
        var purposeEntropy = DpapiPurposeEntropy.Derive(entropy, DpapiPurposeEntropy.Settings);
        var unwrapped = ProtectedData.Unprotect(rawWrap, purposeEntropy, DataProtectionScope.CurrentUser);
        Assert.Equal("tv_fresh_token", Encoding.UTF8.GetString(unwrapped));
    }

    [Fact]
    public void Legacy_null_entropy_token_unwraps_via_fallback()
    {
        // Seed a v0.3.3-style settings file: token wrapped under null entropy.
        var legacyToken = "tv_legacy_token";
        WriteSettings(new
        {
            Theme = "Dark",
            HasAcceptedLaunchNotice = true,
            TeamViewerApiTokenProtected = ProtectLegacy(legacyToken),
        });

        // Load + UnprotectApiToken with NEW entropy (post-upgrade state).
        var entropy = RandomNumberGenerator.GetBytes(32);
        var svc = new SettingsService(_path) { Entropy = entropy };
        var settings = svc.Load();
        svc.UnprotectApiToken(settings);

        Assert.Equal(legacyToken, settings.TeamViewerApiToken);
    }

    [Fact]
    public void Save_after_legacy_load_re_wraps_the_token_under_entropy()
    {
        // Seed legacy state.
        WriteSettings(new
        {
            HasAcceptedLaunchNotice = true,
            TeamViewerApiTokenProtected = ProtectLegacy("legacy_token"),
        });

        var entropy = RandomNumberGenerator.GetBytes(32);
        var svc = new SettingsService(_path) { Entropy = entropy };
        var settings = svc.Load();
        svc.UnprotectApiToken(settings);

        // Mutate something other than the token + Save — the existing token
        // should be re-wrapped under the new entropy on this Save.
        settings.Theme = "System";
        svc.Save(settings);

        // Verify the wrap on disk is now purpose-entropy-bound.
        var settings2 = svc.Load();
        var rawWrap = Convert.FromBase64String(settings2.TeamViewerApiTokenProtected!);
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(rawWrap, optionalEntropy: null, DataProtectionScope.CurrentUser));
        // Base entropy (no purpose derivation) must also fail.
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedData.Unprotect(rawWrap, entropy, DataProtectionScope.CurrentUser));
        // Purpose-derived entropy succeeds.
        var purposeEntropy = DpapiPurposeEntropy.Derive(entropy, DpapiPurposeEntropy.Settings);
        var unwrapped = ProtectedData.Unprotect(rawWrap, purposeEntropy, DataProtectionScope.CurrentUser);
        Assert.Equal("legacy_token", Encoding.UTF8.GetString(unwrapped));
    }

    [Fact]
    public void UnprotectApiToken_is_a_noop_when_no_token_is_present()
    {
        // Fresh settings file: no TeamViewerApiTokenProtected field.
        WriteSettings(new { HasAcceptedLaunchNotice = true });

        var svc = new SettingsService(_path) { Entropy = RandomNumberGenerator.GetBytes(32) };
        var settings = svc.Load();
        svc.UnprotectApiToken(settings); // must not throw
        Assert.Null(settings.TeamViewerApiToken);
        Assert.Null(settings.TeamViewerApiTokenProtected);
    }

    [Fact]
    public void Token_encrypted_under_a_different_entropy_returns_null_not_throws()
    {
        // Save under entropy_a, then load under entropy_b. This mirrors the
        // "settings file copied between machines" scenario.
        var entropyA = RandomNumberGenerator.GetBytes(32);
        var entropyB = RandomNumberGenerator.GetBytes(32);

        var svcA = new SettingsService(_path) { Entropy = entropyA };
        svcA.Save(new AppSettings { TeamViewerApiToken = "machine_a_token" });

        var svcB = new SettingsService(_path) { Entropy = entropyB };
        var settingsB = svcB.Load();
        svcB.UnprotectApiToken(settingsB);
        // Both entropies fail on B's machine; the legacy null-entropy fallback
        // also fails; UnprotectInternal returns null instead of throwing so
        // the user gets a fresh "enter your API token" UI rather than a crash.
        Assert.Null(settingsB.TeamViewerApiToken);
    }

    private static string ProtectLegacy(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private void WriteSettings(object payload)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
