using System.Collections.ObjectModel;
using System.Windows;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Views;

public partial class FolderPickerDialog : Window
{
    public ObservableCollection<FolderNode> RootFolders { get; }
    public Guid? SelectedFolderId { get; private set; }
    public bool MoveToRoot { get; private set; }

    public FolderPickerDialog(IEnumerable<FolderNode> roots, Guid? excludeSubtreeOf = null)
    {
        InitializeComponent();
        RootFolders = new ObservableCollection<FolderNode>(
            roots.Where(r => excludeSubtreeOf is null || !IsInSubtree(r, excludeSubtreeOf.Value)));
        DataContext = this;
    }

    private static bool IsInSubtree(FolderNode node, Guid id)
    {
        if (node.Id == id) return true;
        foreach (var child in node.Children)
            if (child is FolderNode f && IsInSubtree(f, id)) return true;
        return false;
    }

    public static (bool ok, Guid? folderId, bool moveToRoot) Pick(Window? owner,
        IEnumerable<FolderNode> roots, Guid? excludeSubtreeOf = null)
    {
        var dlg = new FolderPickerDialog(roots, excludeSubtreeOf);
        if (owner is not null) dlg.Owner = owner;
        var ok = dlg.ShowDialog() == true;
        return (ok, dlg.SelectedFolderId, dlg.MoveToRoot);
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        OkButton.IsEnabled = e.NewValue is FolderNode;
    }

    private void Root_Click(object sender, RoutedEventArgs e)
    {
        SelectedFolderId = null;
        MoveToRoot = true;
        DialogResult = true;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is FolderNode f)
        {
            SelectedFolderId = f.Id;
            MoveToRoot = false;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
