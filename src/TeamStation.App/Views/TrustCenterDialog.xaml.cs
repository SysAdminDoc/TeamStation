using System.Runtime.Versioning;
using System.Windows;
using TeamStation.App.Services;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Views;

[SupportedOSPlatform("windows")]
public partial class TrustCenterDialog : Window
{
    public TrustCenterDialog(TrustCenterViewModel viewModel)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
