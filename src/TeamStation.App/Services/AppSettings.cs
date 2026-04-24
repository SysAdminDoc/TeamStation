using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string? CloudSyncFolder { get; set; }
    public List<string> SavedSearches { get; set; } = new();
    public List<ExternalToolDefinition> ExternalTools { get; set; } =
    [
        new ExternalToolDefinition { Name = "Ping", Command = "ping", Arguments = "%ID%" },
        new ExternalToolDefinition { Name = "Remote Desktop", Command = "mstsc", Arguments = "/v:%TAG:host%" },
    ];
}

public sealed class ExternalToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
            return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
            settings.TeamViewerApiToken = Unprotect(settings.TeamViewerApiTokenProtected);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.TeamViewerApiTokenProtected = Protect(settings.TeamViewerApiToken);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
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
            return null;
        }
    }
}
