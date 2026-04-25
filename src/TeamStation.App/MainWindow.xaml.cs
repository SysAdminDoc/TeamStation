using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TeamStation.App.Services;
using TeamStation.App.ViewModels;

namespace TeamStation.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        DataContext = viewModel;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "dev";
        VersionBadge.Text = $"v{version}";

        _ = new TreeDragDrop(Tree, viewModel.Reparent);
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

    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not TreeViewItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is TreeViewItem item)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    /// <summary>
    /// v0.3.5: Bulk multi-select accumulator. Plain left-click clears
    /// multi-selection and lets WPF's default TreeView selection take
    /// over (single-select, double-click-launch, arrow-key nav unchanged).
    /// Ctrl+left-click toggles the clicked item's
    /// <see cref="ViewModels.TreeNode.IsMultiSelected"/> flag and is the
    /// ONLY entry point for bulk-selection state — Shift-range-select is
    /// out of scope for v0.3.5 and tracked in ROADMAP for v0.3.6.
    /// </summary>
    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var hit = e.OriginalSource as DependencyObject;
        while (hit is not null and not TreeViewItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is not TreeViewItem item) return;
        if (item.DataContext is not ViewModels.TreeNode node) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl)
        {
            vm.ToggleMultiSelection(node);
            // Keep the clicked item focused so context-menu still anchors to
            // it. Don't mark e.Handled — the standard click should still
            // run so single-select / focus / scrolling all behave as users
            // expect.
            item.Focus();
            e.Handled = true;
        }
        else
        {
            vm.ClearMultiSelection();
            // Don't set Handled — the default click runs and selects this
            // item via the existing Tree_SelectedItemChanged path.
        }
    }
}
