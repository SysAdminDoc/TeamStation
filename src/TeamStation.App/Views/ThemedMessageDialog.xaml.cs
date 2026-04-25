using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TeamStation.App.Services;

namespace TeamStation.App.Views;

public enum ThemedMessageKind
{
    Info,
    Warning,
    Danger,
    Error
}

public partial class ThemedMessageDialog : Window
{
    private readonly ThemedMessageKind _kind;
    private readonly bool _isConfirmation;

    public ThemedMessageDialog(
        string title,
        string message,
        ThemedMessageKind kind,
        bool isConfirmation,
        string confirmText = "OK",
        string cancelText = "Cancel")
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        _kind = kind;
        _isConfirmation = isConfirmation;

        Title = title;
        AutomationProperties.SetName(this, title);
        DialogTitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        AutomationProperties.SetName(ConfirmButton, confirmText);
        AutomationProperties.SetName(CancelButton, cancelText);
        CancelButton.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;
        ApplyKind(kind);

        Loaded += (_, _) =>
        {
            if (_isConfirmation && _kind == ThemedMessageKind.Danger)
                CancelButton.Focus();
            else
                ConfirmButton.Focus();
        };
    }

    public static void Show(Window? owner, string title, string message, ThemedMessageKind kind = ThemedMessageKind.Info)
    {
        var dialog = new ThemedMessageDialog(title, message, kind, isConfirmation: false, confirmText: "Close");
        SetOwner(dialog, owner);
        _ = dialog.ShowDialog();
    }

    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        ThemedMessageKind kind = ThemedMessageKind.Warning,
        string confirmText = "OK",
        string cancelText = "Cancel")
    {
        var dialog = new ThemedMessageDialog(title, message, kind, isConfirmation: true, confirmText, cancelText);
        SetOwner(dialog, owner);
        return dialog.ShowDialog() == true;
    }

    private static void SetOwner(ThemedMessageDialog dialog, Window? owner)
    {
        if (owner is { IsLoaded: true, IsVisible: true })
        {
            dialog.Owner = owner;
            return;
        }

        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void ApplyKind(ThemedMessageKind kind)
    {
        var (label, brushKey, summary) = kind switch
        {
            ThemedMessageKind.Error => ("ERROR", "RedBrush", "The operation did not complete. Review the details before trying again."),
            ThemedMessageKind.Danger => ("DESTRUCTIVE ACTION", "RedBrush", "This changes saved TeamStation data. Continue only if the action is intentional."),
            ThemedMessageKind.Warning => ("CONFIRM ACTION", "YellowBrush", "Review the details before continuing."),
            _ => ("TEAMSTATION", "BlueBrush", string.Empty)
        };

        KindText.Text = label;
        KindText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        AccentBar.SetResourceReference(Border.BackgroundProperty, brushKey);
        ToneSummaryBorder.SetResourceReference(Border.BorderBrushProperty, brushKey);

        ToneSummaryText.Text = summary;
        ToneSummaryBorder.Visibility = string.IsNullOrWhiteSpace(summary)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (kind == ThemedMessageKind.Danger)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            AutomationProperties.SetHelpText(ConfirmButton, "Destructive action. Review the dialog before continuing.");
            AutomationProperties.SetHelpText(CancelButton, "Recommended default for destructive actions.");
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
