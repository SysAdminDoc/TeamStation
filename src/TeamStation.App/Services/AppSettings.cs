using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TeamStation.Core.Io;
using TeamStation.Core.Models;

namespace TeamStation.App.Services;

public sealed class AppSettings
{
    public const int DefaultSlowQueryThresholdMs = 100;
    public const int MinSlowQueryThresholdMs = 1;
    public const int MaxSlowQueryThresholdMs = 60_000;

    public string? TeamViewerPathOverride { get; set; }
    [JsonIgnore]
    public string? TeamViewerApiToken { get; set; }
    public string? TeamViewerApiTokenProtected { get; set; }
    public string Theme { get; set; } = "Dark";
    public bool HasAcceptedLaunchNotice { get; set; }
    public bool WakeOnLanBeforeLaunch { get; set; }
    public bool PreferProtocolLaunch { get; set; }
    public bool PreferClipboardPasswordLaunch { get; set; }
    public bool OptimizeDatabaseOnClose { get; set; } = true;
    public int SlowQueryThresholdMs { get; set; } = DefaultSlowQueryThresholdMs;
    public int HistoryRetentionDays { get; set; } = 90;
    public string? CloudSyncFolder { get; set; }
    public List<string> SavedSearches { get; set; } = new();
    public List<ExternalToolDefinition> ExternalTools { get; set; } =
    [
        new ExternalToolDefinition { Name = "Ping", Command = "ping", Arguments = "%ID%" },
        new ExternalToolDefinition { Name = "Remote Desktop", Command = "mstsc", Arguments = "/v:%TAG:host%" },
    ];

    public static int NormalizeSlowQueryThresholdMs(int value) =>
        Math.Clamp(value, MinSlowQueryThresholdMs, MaxSlowQueryThresholdMs);
}

/// <summary>
/// Persists <see cref="AppSettings"/> to a JSON file. Writes are atomic via
/// write-to-temp + <see cref="File.Move"/> so a crash or power loss never
/// leaves a truncated file. When a load fails, the previous file is moved
/// aside as a <c>.broken</c> snapshot and <see cref="LastLoadError"/> is set
/// so callers can surface the problem to the user instead of silently
/// replacing configuration with defaults.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly string _path;

    public SettingsService(string path)
    {
        _path = path;
    }

    /// <summary>Populated after <see cref="Load"/> when the settings file existed but could not be parsed.</summary>
    public string? LastLoadError { get; private set; }

    public string Path => _path;

    /// <summary>
    /// Per-database DPAPI entropy salt for the API-token wrap. Set by the
    /// host (App.xaml.cs) AFTER the SQLite database is opened — the salt
    /// lives in <c>_meta.dpapi_entropy_v1</c>, not in <c>settings.json</c>,
    /// because storing the salt next to the wrap defeats the trust-boundary
    /// move that the salt provides. Lazy <see cref="UnprotectApiToken"/> is
    /// the architectural fix for the v0.3.3 startup-order constraint where
    /// <see cref="Load"/> runs before <c>CryptoService.CreateOrLoad</c>:
    /// <see cref="Load"/> no longer eagerly Unprotects the token; the host
    /// calls <see cref="UnprotectApiToken"/> after opening the DB and
    /// pushing the salt in via this property.
    /// </summary>
    public byte[]? Entropy { get; set; }

    public AppSettings Load()
    {
        LastLoadError = null;
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var text = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(text, Options) ?? new AppSettings();
            // Token is NOT unprotected here — the entropy salt lives in the
            // SQLite _meta table which has not been opened yet at first Load.
            // The host calls UnprotectApiToken after opening the DB.
            return settings;
        }
        catch (Exception ex)
        {
            LastLoadError = $"Settings file at {_path} was unreadable ({ex.GetType().Name}: {ex.Message}). " +
                            "The bad copy has been set aside; starting with defaults.";
            TryQuarantineBadFile();
            return new AppSettings();
        }
    }

    /// <summary>
    /// Decrypts <see cref="AppSettings.TeamViewerApiTokenProtected"/> into
    /// <see cref="AppSettings.TeamViewerApiToken"/> using the per-database
    /// entropy currently bound to this service. Tries the new entropy
    /// first; on a <see cref="CryptographicException"/> retries with
    /// <c>optionalEntropy: null</c> for legacy v0.3.0 / v0.3.1 / v0.3.2 /
    /// v0.3.3 wraps. Idempotent — safe to call multiple times. No-op
    /// when no protected blob is present (fresh install or token cleared).
    /// </summary>
    public void UnprotectApiToken(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TeamViewerApiToken = UnprotectInternal(settings.TeamViewerApiTokenProtected, Entropy);
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TeamViewerApiTokenProtected = ProtectInternal(settings.TeamViewerApiToken, Entropy);
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    private void TryQuarantineBadFile()
    {
        try
        {
            var quarantine = _path + $".broken.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(_path, quarantine, overwrite: false);
        }
        catch
        {
            // Leaving the original in place is acceptable; the user will see a fresh attempt next save.
        }
    }

    private static string? ProtectInternal(string? value, byte[]? entropy)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, entropy, scope: DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            // The cleartext UTF-8 byte buffer is ours to zero — the source
            // String can't be (CLR-interned), but we don't need to leak the
            // intermediate byte buffer to the heap on top of that.
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string? UnprotectInternal(string? value, byte[]? entropy)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        byte[] wrapped;
        try { wrapped = Convert.FromBase64String(value); }
        catch { return null; } // malformed base64 — token is unrecoverable

        try
        {
            byte[] unprotected;
            try
            {
                unprotected = ProtectedData.Unprotect(wrapped, entropy, scope: DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException) when (entropy is not null)
            {
                // Legacy v0.3.3-and-earlier wrap (made under null entropy).
                // Falling back lets the existing user keep their saved token
                // across the upgrade; the next Save re-wraps under entropy.
                unprotected = ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            }
            try
            {
                return Encoding.UTF8.GetString(unprotected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(unprotected);
            }
        }
        catch
        {
            // Token was encrypted under a different user/machine — expected on a portable DB move.
            return null;
        }
    }
}
