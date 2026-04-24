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
    public string? TeamViewerPathOverride { get; set; }
    [JsonIgnore]
    public string? TeamViewerApiToken { get; set; }
    public string? TeamViewerApiTokenProtected { get; set; }
    public string Theme { get; set; } = "Dark";
    public bool HasAcceptedLaunchNotice { get; set; }
    public bool WakeOnLanBeforeLaunch { get; set; }
    public bool PreferClipboardPasswordLaunch { get; set; }
    public int HistoryRetentionDays { get; set; } = 90;
    public string? CloudSyncFolder { get; set; }
    public List<string> SavedSearches { get; set; } = new();
    public List<ExternalToolDefinition> ExternalTools { get; set; } =
    [
        new ExternalToolDefinition { Name = "Ping", Command = "ping", Arguments = "%ID%" },
        new ExternalToolDefinition { Name = "Remote Desktop", Command = "mstsc", Arguments = "/v:%TAG:host%" },
    ];
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

    public AppSettings Load()
    {
        LastLoadError = null;
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var text = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(text, Options) ?? new AppSettings();
            settings.TeamViewerApiToken = Unprotect(settings.TeamViewerApiTokenProtected);
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

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TeamViewerApiTokenProtected = Protect(settings.TeamViewerApiToken);
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

    private static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var bytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var bytes = Convert.FromBase64String(value);
            var unprotected = ProtectedData.Unprotect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotected);
        }
        catch
        {
            // Token was encrypted under a different user/machine — expected on a portable DB move.
            return null;
        }
    }
}
