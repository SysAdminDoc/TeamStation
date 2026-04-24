using System.Windows;

namespace TeamStation.App.Views;

public partial class MasterPasswordWindow : Window
{
    private readonly bool _createNew;

    public MasterPasswordWindow(bool createNew)
    {
        InitializeComponent();
        _createNew = createNew;
        TitleText.Text = createNew ? "Create portable master password" : "Unlock TeamStation";
        SubtitleText.Text = createNew
            ? "Portable mode stores the database next to the app. A master password is required so the database can move between machines without relying on Windows DPAPI."
            : "Enter the master password for this portable database.";
        ConfirmPanel.Visibility = createNew ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string Password { get; private set; } = string.Empty;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        if (password.Length < 10)
        {
            ShowValidation("Use at least 10 characters.");
            return;
        }

        if (_createNew && password != ConfirmBox.Password)
        {
            ShowValidation("The passwords do not match.");
            return;
        }

        Password = password;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationBorder.Visibility = Visibility.Visible;
    }
}
