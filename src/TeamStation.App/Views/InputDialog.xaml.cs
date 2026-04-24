using System.Windows;

namespace TeamStation.App.Views;

public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        DialogTitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
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

    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
