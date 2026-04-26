using System.Windows;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;
using TeamStation.App.Services;
using TeamStation.Core.Models;

namespace TeamStation.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<(int entries, int folders)>? _rotateDek;

    public SettingsWindow(AppSettings settings, Func<(int entries, int folders)>? rotateDek = null)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        _settings = settings;
        _rotateDek = rotateDek;
        ThemeBox.ItemsSource = ThemeManager.Themes;
        ThemeBox.SelectedValue = ThemeManager.Normalize(settings.Theme);
        TeamViewerPathBox.Text = settings.TeamViewerPathOverride ?? string.Empty;
        ApiTokenBox.Password = settings.TeamViewerApiToken ?? string.Empty;
        WakeBox.IsChecked = settings.WakeOnLanBeforeLaunch;
        ProtocolLaunchBox.IsChecked = settings.PreferProtocolLaunch;
        ClipboardPasswordBox.IsChecked = settings.PreferClipboardPasswordLaunch;
        CloudFolderBox.Text = settings.CloudSyncFolder ?? string.Empty;
        RetentionDaysBox.Text = settings.HistoryRetentionDays.ToString();
        OptimizeDatabaseBox.IsChecked = settings.OptimizeDatabaseOnClose;
        SlowQueryThresholdBox.Text = AppSettings.NormalizeSlowQueryThresholdMs(settings.SlowQueryThresholdMs).ToString();
        SavedSearchesBox.Text = string.Join(Environment.NewLine, settings.SavedSearches);
        ExternalToolsBox.Text = string.Join(Environment.NewLine,
            settings.ExternalTools.Select(t => $"{t.Name}|{t.Command}|{t.Arguments}"));

        RotateDekButton.IsEnabled = rotateDek is not null;
        if (rotateDek is null)
            RotateDekHint.Text = "Key rotation is not available in portable (master-password) mode.";
    }

    private void RotateDek_Click(object sender, RoutedEventArgs e)
    {
        if (_rotateDek is null) return;

        var confirmed = ThemedMessageDialog.Confirm(
            this,
            "Rotate encryption key",
            "This generates a new 256-bit AES-GCM key and re-encrypts every stored password under it. " +
            "The operation is atomic — if anything fails, your existing passwords are unaffected.\n\nContinue?",
            ThemedMessageKind.Warning,
            "Rotate");
        if (!confirmed) return;

        RotateDekButton.IsEnabled = false;
        RotateDekStatus.Visibility = Visibility.Collapsed;

        try
        {
            var (entries, folders) = _rotateDek();
            var parts = new List<string>();
            if (entries > 0) parts.Add($"{entries} connection password{(entries == 1 ? "" : "s")}");
            if (folders > 0) parts.Add($"{folders} folder default{(folders == 1 ? "" : "s")}");
            var what = parts.Count > 0 ? string.Join(" and ", parts) : "0 passwords";
            RotateDekStatus.Text = $"Done. Re-encrypted {what} under a fresh key.";
            RotateDekStatus.Foreground = (Brush)FindResource("GreenBrush");
            RotateDekStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            RotateDekStatus.Text = $"Rotation failed: {ex.Message}";
            RotateDekStatus.Foreground = (Brush)FindResource("RedBrush");
            RotateDekStatus.Visibility = Visibility.Visible;
            RotateDekButton.IsEnabled = true;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Select TeamViewer.exe",
            Filter = "TeamViewer.exe|TeamViewer.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (ofd.ShowDialog(this) == true)
            TeamViewerPathBox.Text = ofd.FileName;
    }

    private void BrowseCloud_Click(object sender, RoutedEventArgs e)
    {
        var selectedPath = FileSystemFolderDialog.Pick(this, "Select cloud sync folder", CloudFolderBox.Text);
        if (selectedPath is not null)
            CloudFolderBox.Text = selectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateBeforeSave())
            return;

        _settings.TeamViewerPathOverride = BlankToNull(TeamViewerPathBox.Text);
        _settings.Theme = ThemeBox.SelectedValue as string ?? "Dark";
        _settings.TeamViewerApiToken = BlankToNull(ApiTokenBox.Password);
        _settings.WakeOnLanBeforeLaunch = WakeBox.IsChecked == true;
        _settings.PreferProtocolLaunch = ProtocolLaunchBox.IsChecked == true;
        _settings.PreferClipboardPasswordLaunch = ClipboardPasswordBox.IsChecked == true;
        _settings.CloudSyncFolder = BlankToNull(CloudFolderBox.Text);
        _settings.HistoryRetentionDays = int.Parse(RetentionDaysBox.Text.Trim());
        _settings.OptimizeDatabaseOnClose = OptimizeDatabaseBox.IsChecked == true;
        _settings.SlowQueryThresholdMs = AppSettings.NormalizeSlowQueryThresholdMs(int.Parse(SlowQueryThresholdBox.Text.Trim()));
        _settings.SavedSearches = SavedSearchesBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.ExternalTools = ParseTools(ExternalToolsBox.Text);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool ValidateBeforeSave()
    {
        var teamViewerPath = BlankToNull(TeamViewerPathBox.Text);
        if (teamViewerPath is not null && !File.Exists(teamViewerPath))
        {
            ShowValidation("TeamViewer executable path does not exist.");
            TeamViewerPathBox.Focus();
            return false;
        }

        var cloudFolder = BlankToNull(CloudFolderBox.Text);
        if (cloudFolder is not null && !Directory.Exists(cloudFolder))
        {
            ShowValidation("Cloud sync folder does not exist.");
            CloudFolderBox.Focus();
            return false;
        }

        if (!int.TryParse(RetentionDaysBox.Text.Trim(), out var retentionDays) ||
            retentionDays is < 0 or > 3650)
        {
            ShowValidation("History retention must be a whole number from 0 to 3650 days.");
            RetentionDaysBox.Focus();
            return false;
        }

        if (!int.TryParse(SlowQueryThresholdBox.Text.Trim(), out var slowQueryThresholdMs) ||
            slowQueryThresholdMs is < AppSettings.MinSlowQueryThresholdMs or > AppSettings.MaxSlowQueryThresholdMs)
        {
            ShowValidation($"Slow query threshold must be a whole number from {AppSettings.MinSlowQueryThresholdMs} to {AppSettings.MaxSlowQueryThresholdMs} ms.");
            SlowQueryThresholdBox.Focus();
            return false;
        }

        var invalidToolLine = FindInvalidExternalToolLine(ExternalToolsBox.Text);
        if (invalidToolLine is not null)
        {
            ShowValidation($"External tool line {invalidToolLine.Value} must use Name|Command with an optional |Arguments segment.");
            ExternalToolsBox.Focus();
            return false;
        }

        ValidationBorder.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationBorder.Visibility = Visibility.Visible;
    }

    private static int? FindInvalidExternalToolLine(string text)
    {
        var lineNumber = 0;
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return lineNumber;
        }

        return null;
    }

    private static List<ExternalToolDefinition> ParseTools(string text)
    {
        var tools = new List<ExternalToolDefinition>();
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;
            tools.Add(new ExternalToolDefinition
            {
                Name = parts[0],
                Command = parts[1],
                Arguments = parts.Length == 3 ? parts[2] : string.Empty,
            });
        }

        return tools;
    }

    private static string? BlankToNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
