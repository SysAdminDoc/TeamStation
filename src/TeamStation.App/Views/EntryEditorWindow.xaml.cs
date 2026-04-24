using System.Windows;
using System.Windows.Controls;
using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.App.Views;

public partial class EntryEditorWindow : Window
{
    private readonly ConnectionEntry _entry;

    public EntryEditorWindow(ConnectionEntry entry)
    {
        InitializeComponent();
        _entry = entry;
        Load();
    }

    private void Load()
    {
        NameBox.Text = _entry.Name;
        IdBox.Text = _entry.TeamViewerId;
        PasswordBox.Password = _entry.Password ?? string.Empty;
        SelectNullableEnum(ModeBox, _entry.Mode);
        SelectNullableEnum(QualityBox, _entry.Quality);
        SelectNullableEnum(AcBox, _entry.AccessControl);
        NotesBox.Text = _entry.Notes ?? string.Empty;
        TagsBox.Text = string.Join(", ", _entry.Tags);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ValidationError("Friendly name is required.");
            NameBox.Focus();
            return;
        }

        var id = IdBox.Text.Trim();
        try { LaunchInputValidator.ValidateTeamViewerId(id); }
        catch (LaunchValidationException ex)
        {
            ValidationError(ex.Message);
            IdBox.Focus();
            return;
        }

        var password = PasswordBox.Password;
        if (!string.IsNullOrEmpty(password))
        {
            try { LaunchInputValidator.ValidatePassword(password); }
            catch (LaunchValidationException ex)
            {
                ValidationError(ex.Message);
                PasswordBox.Focus();
                return;
            }
        }

        _entry.Name = name;
        _entry.TeamViewerId = id;
        _entry.Password = string.IsNullOrEmpty(password) ? null : password;
        _entry.Mode = GetNullableEnum<ConnectionMode>(ModeBox);
        _entry.Quality = GetNullableEnum<ConnectionQuality>(QualityBox);
        _entry.AccessControl = GetNullableEnum<AccessControl>(AcBox);
        _entry.Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        _entry.Tags = TagsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ValidationError(string message)
    {
        MessageBox.Show(this, message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
