using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TeamStation.App.Services;
using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.App.Views;

public partial class EntryEditorWindow : Window
{
    private readonly ConnectionEntry _entry;

    public EntryEditorWindow(ConnectionEntry entry)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        _entry = entry;

        var isNew = string.IsNullOrWhiteSpace(entry.TeamViewerId);
        DialogTitleText.Text = isNew ? "New connection" : "Edit connection";
        Title = DialogTitleText.Text;
        SaveButton.Content = isNew ? "Create connection" : "Save connection";

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };

        Load();
    }

    private void Load()
    {
        NameBox.Text = _entry.Name;
        IdBox.Text = _entry.TeamViewerId;
        ProfileBox.Text = _entry.ProfileName;
        PasswordBox.Password = _entry.Password ?? string.Empty;
        SelectNullableEnum(ModeBox, _entry.Mode);
        SelectNullableEnum(QualityBox, _entry.Quality);
        SelectNullableEnum(AcBox, _entry.AccessControl);
        NotesBox.Text = _entry.Notes ?? string.Empty;
        TagsBox.Text = string.Join(", ", _entry.Tags);
        PathOverrideBox.Text = _entry.TeamViewerPathOverride ?? string.Empty;
        WakeMacBox.Text = _entry.WakeMacAddress ?? string.Empty;
        WakeBroadcastBox.Text = _entry.WakeBroadcastAddress ?? string.Empty;
        PreLaunchScriptBox.Text = _entry.PreLaunchScript ?? string.Empty;
        PostLaunchScriptBox.Text = _entry.PostLaunchScript ?? string.Empty;

        if (_entry.Proxy is { } proxy)
        {
            ProxyHostBox.Text = proxy.Host;
            ProxyPortBox.Text = proxy.Port.ToString(CultureInfo.InvariantCulture);
            ProxyUserBox.Text = proxy.Username ?? string.Empty;
            ProxyPasswordBox.Password = proxy.Password ?? string.Empty;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();

        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowValidation("Friendly name is required.");
            NameBox.Focus();
            return;
        }

        var id = IdBox.Text.Trim();
        try
        {
            LaunchInputValidator.ValidateTeamViewerId(id);
        }
        catch (LaunchValidationException ex)
        {
            ShowValidation(ex.Message);
            IdBox.Focus();
            return;
        }

        // Do not trim — whitespace in saved passwords is a real thing and
        // silent trim would corrupt stored credentials. The validator rejects
        // interior whitespace, so a paste with trailing whitespace surfaces a
        // clear error rather than silently losing characters.
        var password = PasswordBox.Password;
        if (!string.IsNullOrEmpty(password))
        {
            try
            {
                LaunchInputValidator.ValidatePassword(password);
            }
            catch (LaunchValidationException ex)
            {
                ShowValidation(ex.Message);
                PasswordBox.Focus();
                return;
            }
        }

        var pathOverride = PathOverrideBox.Text.Trim();
        if (!string.IsNullOrEmpty(pathOverride) && !File.Exists(pathOverride))
        {
            ShowValidation("TeamViewer.exe override path does not exist.");
            PathOverrideBox.Focus();
            return;
        }

        ProxySettings? proxy = null;
        var proxyHost = ProxyHostBox.Text.Trim();
        var proxyPortText = ProxyPortBox.Text.Trim();
        var proxyUser = ProxyUserBox.Text.Trim();
        // As above: do not trim the proxy password.
        var proxyPassword = ProxyPasswordBox.Password;
        var hasAnyProxyField =
            !string.IsNullOrEmpty(proxyHost) ||
            !string.IsNullOrEmpty(proxyPortText) ||
            !string.IsNullOrEmpty(proxyUser) ||
            !string.IsNullOrEmpty(proxyPassword);

        if (hasAnyProxyField)
        {
            if (string.IsNullOrEmpty(proxyHost))
            {
                ShowValidation("Proxy host is required when proxy routing is enabled.");
                ProxyHostBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(proxyPortText) ||
                !int.TryParse(proxyPortText, NumberStyles.None, CultureInfo.InvariantCulture, out var proxyPort))
            {
                ShowValidation("Proxy port must be a number between 1 and 65535.");
                ProxyPortBox.Focus();
                return;
            }

            try
            {
                LaunchInputValidator.ValidateProxyEndpoint($"{proxyHost}:{proxyPort}");
                LaunchInputValidator.ValidateProxyUsername(proxyUser);
                if (!string.IsNullOrEmpty(proxyPassword))
                    LaunchInputValidator.ValidatePassword(proxyPassword);
            }
            catch (LaunchValidationException ex)
            {
                ShowValidation(ex.Message);
                ProxyHostBox.Focus();
                return;
            }

            proxy = new ProxySettings(
                Host: proxyHost,
                Port: proxyPort,
                Username: string.IsNullOrEmpty(proxyUser) ? null : proxyUser,
                Password: string.IsNullOrEmpty(proxyPassword) ? null : proxyPassword);
        }

        _entry.Name = name;
        _entry.TeamViewerId = id;
        _entry.ProfileName = string.IsNullOrWhiteSpace(ProfileBox.Text) ? "Default" : ProfileBox.Text.Trim();
        _entry.Password = string.IsNullOrEmpty(password) ? null : password;
        _entry.Mode = GetNullableEnum<ConnectionMode>(ModeBox);
        _entry.Quality = GetNullableEnum<ConnectionQuality>(QualityBox);
        _entry.AccessControl = GetNullableEnum<AccessControl>(AcBox);
        _entry.Proxy = proxy;
        _entry.TeamViewerPathOverride = string.IsNullOrWhiteSpace(PathOverrideBox.Text) ? null : PathOverrideBox.Text.Trim();
        _entry.WakeMacAddress = string.IsNullOrWhiteSpace(WakeMacBox.Text) ? null : WakeMacBox.Text.Trim();
        _entry.WakeBroadcastAddress = string.IsNullOrWhiteSpace(WakeBroadcastBox.Text) ? null : WakeBroadcastBox.Text.Trim();
        _entry.PreLaunchScript = string.IsNullOrWhiteSpace(PreLaunchScriptBox.Text) ? null : PreLaunchScriptBox.Text.Trim();
        _entry.PostLaunchScript = string.IsNullOrWhiteSpace(PostLaunchScriptBox.Text) ? null : PostLaunchScriptBox.Text.Trim();
        _entry.Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        _entry.Tags = TagsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationBorder.Visibility = Visibility.Visible;
    }

    private void ClearValidation()
    {
        ValidationText.Text = string.Empty;
        ValidationBorder.Visibility = Visibility.Collapsed;
    }

    private static void SelectNullableEnum<T>(ComboBox combo, T? value) where T : struct, Enum
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (value is null && item.Tag is null) { combo.SelectedItem = item; return; }
            if (value is T v && item.Tag is T tag && tag.Equals(v)) { combo.SelectedItem = item; return; }
        }

        combo.SelectedIndex = 0;
    }

    private static T? GetNullableEnum<T>(ComboBox combo) where T : struct, Enum
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is T tag)
            return tag;

        return null;
    }
}
