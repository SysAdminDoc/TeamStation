using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TeamStation.App.Services;
using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.App.Views;

public partial class BulkProxyDialog : Window
{
    public BulkProxyDialog(ProxySettings? initialProxy)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        Title = "Set proxy on selection";
        DialogTitleText.Text = Title;

        Loaded += (_, _) =>
        {
            ProxyHostBox.Focus();
            ProxyHostBox.SelectAll();
        };

        if (initialProxy is not null)
        {
            ProxyHostBox.Text = initialProxy.Host;
            ProxyPortBox.Text = initialProxy.Port.ToString(CultureInfo.InvariantCulture);
            ProxyUserBox.Text = initialProxy.Username ?? string.Empty;
            ProxyPasswordBox.Password = initialProxy.Password ?? string.Empty;
        }

        UpdateSaveReadiness();
    }

    public ProxySettings? Proxy { get; private set; }

    public static ProxySettings? Prompt(Window? owner, ProxySettings? initialProxy)
    {
        var dialog = new BulkProxyDialog(initialProxy);
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.Proxy : null;
    }

    private void RequiredField_TextChanged(object sender, TextChangedEventArgs e) => UpdateSaveReadiness();

    private void UpdateSaveReadiness()
    {
        if (ApplyButton is null)
            return;

        var hasRequiredFields =
            !string.IsNullOrWhiteSpace(ProxyHostBox.Text) &&
            !string.IsNullOrWhiteSpace(ProxyPortBox.Text);

        ApplyButton.IsEnabled = hasRequiredFields;
        ApplyButton.ToolTip = hasRequiredFields
            ? "Apply this proxy to every selected connection"
            : "Enter a proxy host and port before applying";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ClearValidation();

        var proxyHost = ProxyHostBox.Text.Trim();
        if (string.IsNullOrEmpty(proxyHost))
        {
            ShowValidation("Proxy host is required.");
            ProxyHostBox.Focus();
            return;
        }

        var proxyPortText = ProxyPortBox.Text.Trim();
        if (string.IsNullOrEmpty(proxyPortText) ||
            !int.TryParse(proxyPortText, NumberStyles.None, CultureInfo.InvariantCulture, out var proxyPort))
        {
            ShowValidation("Proxy port must be a number between 1 and 65535.");
            ProxyPortBox.Focus();
            return;
        }

        var proxyUser = ProxyUserBox.Text.Trim();
        var proxyPassword = ProxyPasswordBox.Password;

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

        Proxy = new ProxySettings(
            Host: proxyHost,
            Port: proxyPort,
            Username: string.IsNullOrEmpty(proxyUser) ? null : proxyUser,
            Password: string.IsNullOrEmpty(proxyPassword) ? null : proxyPassword);

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

    private void ClearValidation()
    {
        ValidationText.Text = string.Empty;
        ValidationBorder.Visibility = Visibility.Collapsed;
    }
}
