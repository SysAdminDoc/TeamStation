using System.Windows;
using Microsoft.Win32;
using TeamStation.App.Services;
using TeamStation.Core.Models;

namespace TeamStation.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemeBox.ItemsSource = ThemeManager.Themes;
        ThemeBox.SelectedValue = ThemeManager.Normalize(settings.Theme);
        TeamViewerPathBox.Text = settings.TeamViewerPathOverride ?? string.Empty;
        ApiTokenBox.Password = settings.TeamViewerApiToken ?? string.Empty;
        WakeBox.IsChecked = settings.WakeOnLanBeforeLaunch;
        ClipboardPasswordBox.IsChecked = settings.PreferClipboardPasswordLaunch;
        CloudFolderBox.Text = settings.CloudSyncFolder ?? string.Empty;
        SavedSearchesBox.Text = string.Join(Environment.NewLine, settings.SavedSearches);
        ExternalToolsBox.Text = string.Join(Environment.NewLine,
            settings.ExternalTools.Select(t => $"{t.Name}|{t.Command}|{t.Arguments}"));
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
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select cloud sync folder",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            CloudFolderBox.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.TeamViewerPathOverride = BlankToNull(TeamViewerPathBox.Text);
        _settings.Theme = ThemeBox.SelectedValue as string ?? "Dark";
        _settings.TeamViewerApiToken = BlankToNull(ApiTokenBox.Password);
        _settings.WakeOnLanBeforeLaunch = WakeBox.IsChecked == true;
        _settings.PreferClipboardPasswordLaunch = ClipboardPasswordBox.IsChecked == true;
        _settings.CloudSyncFolder = BlankToNull(CloudFolderBox.Text);
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
