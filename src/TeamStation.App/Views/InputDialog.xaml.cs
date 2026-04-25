using System.Windows;
using System.Windows.Controls;
using TeamStation.App.Services;

namespace TeamStation.App.Views;

public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        Title = title;
        DialogTitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        ValidateValue(showError: false);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    public string Value => ValueBox.Text.Trim();

    public static string? Prompt(Window? owner, string title, string prompt, string initial = "")
    {
        var dlg = new InputDialog(title, prompt, initial);
        if (owner is not null) dlg.Owner = owner;
        return dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value)
            ? dlg.Value
            : null;
    }

    private void ValueBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateValue(showError: false);

    private bool ValidateValue(bool showError)
    {
        var valid = !string.IsNullOrWhiteSpace(Value);
        OkButton.IsEnabled = valid;
        ValidationBorder.Visibility = showError && !valid ? Visibility.Visible : Visibility.Collapsed;
        if (!valid)
            ValidationText.Text = "Enter a name before saving.";
        return valid;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateValue(showError: true))
            return;

        DialogResult = true;
        Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
