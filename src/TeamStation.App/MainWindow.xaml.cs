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
        {
            vm.Selected = e.NewValue as TreeNode;
            if (Keyboard.Modifiers == ModifierKeys.None && e.NewValue is TreeNode node)
                vm.SetMultiSelectionAnchor(node);
        }
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
    /// Bulk multi-select accumulator. Plain left-click clears
    /// multi-selection and lets WPF's default TreeView selection take
    /// over (single-select, double-click-launch, arrow-key nav unchanged).
    /// Ctrl+left-click toggles the clicked item's
    /// <see cref="ViewModels.TreeNode.IsMultiSelected"/> flag. Shift+click
    /// selects a contiguous visible range; Ctrl+Shift adds that range to the
    /// existing bulk selection.
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
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (shift)
        {
            vm.SelectMultiSelectionRange(node, append: ctrl);
            item.Focus();
            item.IsSelected = true;
            e.Handled = true;
        }
        else if (ctrl)
        {
            vm.ToggleMultiSelection(node);
            // Keep the clicked item focused so context-menu and the detail
            // pane still anchor to the row the user just acted on.
            item.Focus();
            item.IsSelected = true;
            e.Handled = true;
        }
        else
        {
            vm.ClearMultiSelection();
            vm.SetMultiSelectionAnchor(node);
            // Don't set Handled — the default click runs and selects this
            // item via the existing Tree_SelectedItemChanged path.
        }
    }
}
