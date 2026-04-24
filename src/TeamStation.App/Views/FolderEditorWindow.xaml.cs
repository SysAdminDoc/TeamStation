using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using TeamStation.Core.Models;

namespace TeamStation.App.Views;

public partial class FolderEditorWindow : Window
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private readonly Folder _folder;

    public FolderEditorWindow(Folder folder)
    {
        InitializeComponent();
        _folder = folder;
        Load();
    }

    private void Load()
    {
        NameBox.Text = _folder.Name;
        AccentBox.Text = _folder.AccentColor ?? string.Empty;
        DefaultPasswordBox.Password = _folder.DefaultPassword ?? string.Empty;
        SelectNullableEnum(ModeBox, _folder.DefaultMode);
        SelectNullableEnum(QualityBox, _folder.DefaultQuality);
        SelectNullableEnum(AcBox, _folder.DefaultAccessControl);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, "Folder name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            return;
        }

        var accent = AccentBox.Text.Trim();
        if (!string.IsNullOrEmpty(accent) && !HexColor.IsMatch(accent))
        {
            MessageBox.Show(this, "Accent color must be a hex triplet like #89B4FA.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            AccentBox.Focus();
            return;
        }

        _folder.Name = name;
        _folder.AccentColor = string.IsNullOrEmpty(accent) ? null : accent;
        var pw = DefaultPasswordBox.Password;
        _folder.DefaultPassword = string.IsNullOrEmpty(pw) ? null : pw;
        _folder.DefaultMode = GetNullableEnum<ConnectionMode>(ModeBox);
        _folder.DefaultQuality = GetNullableEnum<ConnectionQuality>(QualityBox);
        _folder.DefaultAccessControl = GetNullableEnum<AccessControl>(AcBox);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is T tag) return tag;
        return null;
    }
}
