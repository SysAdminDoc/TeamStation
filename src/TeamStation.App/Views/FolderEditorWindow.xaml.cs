using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.App.Views;

public partial class FolderEditorWindow : Window
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private readonly Folder _folder;

    public FolderEditorWindow(Folder folder)
    {
        InitializeComponent();
        _folder = folder;

        var isNew = string.IsNullOrWhiteSpace(folder.Name);
        DialogTitleText.Text = isNew ? "New folder" : "Edit folder";
        Title = DialogTitleText.Text;
        SaveButton.Content = isNew ? "Create folder" : "Save folder";

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };

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
        UpdateAccentPreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();

        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowValidation("Folder name is required.");
            NameBox.Focus();
            return;
        }

        var accent = AccentBox.Text.Trim();
        if (!string.IsNullOrEmpty(accent) && !HexColor.IsMatch(accent))
        {
            ShowValidation("Accent color must be a six-digit hex value such as #7CB8FF.");
            AccentBox.Focus();
            return;
        }

        _folder.Name = name;
        _folder.AccentColor = string.IsNullOrEmpty(accent) ? null : accent;
        var password = DefaultPasswordBox.Password.Trim();
        if (!string.IsNullOrEmpty(password))
        {
            try
            {
                LaunchInputValidator.ValidatePassword(password);
            }
            catch (LaunchValidationException ex)
            {
                ShowValidation(ex.Message);
                DefaultPasswordBox.Focus();
                return;
            }
        }

        _folder.DefaultPassword = string.IsNullOrEmpty(password) ? null : password;
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

    private void AccentPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string accent)
            AccentBox.Text = accent;
    }

    private void ClearAccent_Click(object sender, RoutedEventArgs e)
    {
        AccentBox.Text = string.Empty;
    }

    private void AccentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateAccentPreview();
    }

    private void UpdateAccentPreview()
    {
        var accent = AccentBox.Text.Trim();
        if (!string.IsNullOrEmpty(accent) && HexColor.IsMatch(accent))
        {
            AccentPreview.Background = (Brush)new BrushConverter().ConvertFromString(accent)!;
        }
        else
        {
            AccentPreview.Background = Application.Current?.TryFindResource("MauveBrush") as Brush ?? Brushes.MediumPurple;
        }
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
