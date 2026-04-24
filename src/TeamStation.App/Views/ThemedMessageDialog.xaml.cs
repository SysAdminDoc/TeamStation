using System.Windows;
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

        Title = title;
        DialogTitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;
        ApplyKind(kind);

        Loaded += (_, _) => ConfirmButton.Focus();
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
        var (label, brushKey) = kind switch
        {
            ThemedMessageKind.Error => ("ERROR", "RedBrush"),
            ThemedMessageKind.Danger => ("DESTRUCTIVE ACTION", "RedBrush"),
            ThemedMessageKind.Warning => ("CONFIRM ACTION", "YellowBrush"),
            _ => ("TEAMSTATION", "BlueBrush")
        };

        KindText.Text = label;
        KindText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        AccentBar.SetResourceReference(Border.BackgroundProperty, brushKey);

        if (kind == ThemedMessageKind.Danger)
            ConfirmButton.Style = (Style)FindResource("DangerButton");
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
