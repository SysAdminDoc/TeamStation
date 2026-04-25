using TeamStation.App.Services;

namespace TeamStation.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _workDir;

    public SettingsServiceTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"ts-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private string SettingsPath => Path.Combine(_workDir, "ts.settings.json");

    [Fact]
    public void Load_returns_defaults_when_file_is_missing()
    {
        var svc = new SettingsService(SettingsPath);
        var loaded = svc.Load();
        Assert.Null(svc.LastLoadError);
        Assert.Equal(90, loaded.HistoryRetentionDays);
        Assert.True(loaded.OptimizeDatabaseOnClose);
        Assert.False(loaded.HasAcceptedLaunchNotice);
    }

    [Fact]
    public void Round_trip_preserves_new_scalar_settings()
    {
        var svc = new SettingsService(SettingsPath);
        var original = new AppSettings
        {
            HasAcceptedLaunchNotice = true,
            WakeOnLanBeforeLaunch = true,
            PreferProtocolLaunch = true,
            PreferClipboardPasswordLaunch = true,
            OptimizeDatabaseOnClose = false,
            HistoryRetentionDays = 14,
            Theme = "Light",
        };

        svc.Save(original);
        var loaded = svc.Load();

        Assert.True(loaded.HasAcceptedLaunchNotice);
        Assert.True(loaded.WakeOnLanBeforeLaunch);
        Assert.True(loaded.PreferProtocolLaunch);
        Assert.True(loaded.PreferClipboardPasswordLaunch);
        Assert.False(loaded.OptimizeDatabaseOnClose);
        Assert.Equal(14, loaded.HistoryRetentionDays);
        Assert.Equal("Light", loaded.Theme);
        Assert.Null(svc.LastLoadError);
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_partial_files()
    {
        var svc = new SettingsService(SettingsPath);
        svc.Save(new AppSettings { Theme = "Dark" });

        var partials = Directory.GetFiles(_workDir, "*.tmp");
        Assert.Empty(partials);
    }

    [Fact]
    public void Load_quarantines_corrupt_file_and_sets_LastLoadError()
    {
        File.WriteAllText(SettingsPath, "{ not json");
        var svc = new SettingsService(SettingsPath);
        var loaded = svc.Load();

        Assert.NotNull(svc.LastLoadError);
        Assert.Contains("unreadable", svc.LastLoadError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(90, loaded.HistoryRetentionDays); // defaults returned

        // The bad copy is set aside so the user's next save starts from a clean slate.
        Assert.False(File.Exists(SettingsPath));
        var quarantined = Directory.GetFiles(_workDir, "ts.settings.json.broken.*");
        Assert.Single(quarantined);
    }
}
