using System.Reflection;
using System.Windows;
using System.Windows.Input;
using TeamStation.App.ViewModels;

namespace TeamStation.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "dev";
        VersionBadge.Text = $"v{version}";
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.LaunchCommand.CanExecute(null))
            vm.LaunchCommand.Execute(null);
    }
}
