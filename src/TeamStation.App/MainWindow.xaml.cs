using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm)
            vm.Selected = e.NewValue as TreeNode;
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Only launch if a TreeViewItem was double-clicked (not the empty tree area)
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not TreeViewItem)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is null) return;

        if (vm.LaunchCommand.CanExecute(null))
            vm.LaunchCommand.Execute(null);
    }
}
