using System.Windows;
using System.Windows.Controls;
using TeamStation.App.Services;

namespace TeamStation.App.Views;

public sealed class ChoiceDialogOption
{
    public ChoiceDialogOption(string label, string description, object? value)
    {
        Label = label;
        Description = description;
        Value = value;
    }

    public string Label { get; }
    public string Description { get; }
    public object? Value { get; }
}

public partial class ChoiceDialog : Window
{
    public ChoiceDialog(
        string title,
        string prompt,
        string noSelectionText,
        IReadOnlyList<ChoiceDialogOption> options,
        bool hasInitialValue,
        object? initialValue)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        Title = title;
        DialogTitleText.Text = title;
        PromptText.Text = prompt;
        ChoiceDescriptionText.Text = noSelectionText;
        ChoiceBox.ItemsSource = options;

        if (hasInitialValue)
        {
            ChoiceBox.SelectedItem = options.FirstOrDefault(option => Equals(option.Value, initialValue));
        }

        if (ChoiceBox.SelectedItem is null && options.Count == 1)
        {
            ChoiceBox.SelectedIndex = 0;
        }

        UpdateSelectionState();
    }

    public object? SelectedValue { get; private set; }

    public static bool Pick<T>(
        Window? owner,
        string title,
        string prompt,
        string noSelectionText,
        IReadOnlyList<ChoiceDialogOption> options,
        bool hasInitialValue,
        T? initialValue,
        out T? selectedValue)
        where T : struct
    {
        var dlg = new ChoiceDialog(title, prompt, noSelectionText, options, hasInitialValue, initialValue);
        if (owner is not null) dlg.Owner = owner;

        if (dlg.ShowDialog() == true)
        {
            selectedValue = dlg.SelectedValue is T typedValue ? typedValue : null;
            return true;
        }

        selectedValue = null;
        return false;
    }

    private void ChoiceBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        var option = ChoiceBox.SelectedItem as ChoiceDialogOption;
        ApplyButton.IsEnabled = option is not null;

        if (option is not null)
        {
            ChoiceDescriptionText.Text = option.Description;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ChoiceBox.SelectedItem is not ChoiceDialogOption option)
            return;

        SelectedValue = option.Value;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
