using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Views;

public partial class FolderPickerDialog : Window
{
    public ObservableCollection<PickerFolderItem> RootFolders { get; }
    public Guid? SelectedFolderId { get; private set; }
    public bool MoveToRoot { get; private set; }

    private FolderPickerDialog(IEnumerable<FolderNode> roots, Guid? excludeSubtreeOf)
    {
        InitializeComponent();
        RootFolders = new ObservableCollection<PickerFolderItem>(
            roots.Select(r => PickerFolderItem.Build(r, excludeSubtreeOf))
                 .Where(item => item is not null)
                 .Select(item => item!));
        DataContext = this;
    }

    public static (bool ok, Guid? folderId, bool moveToRoot) Pick(
        Window? owner,
        IEnumerable<FolderNode> roots,
        Guid? excludeSubtreeOf = null)
    {
        var dlg = new FolderPickerDialog(roots, excludeSubtreeOf);
        if (owner is not null) dlg.Owner = owner;
        var ok = dlg.ShowDialog() == true;
        return (ok, dlg.SelectedFolderId, dlg.MoveToRoot);
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        OkButton.IsEnabled = e.NewValue is PickerFolderItem;
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
        if (Tree.SelectedItem is PickerFolderItem f)
        {
            SelectedFolderId = f.Id;
            MoveToRoot = false;
            DialogResult = true;
            Close();
        }
    }

    private void Tree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OkButton.IsEnabled)
            Ok_Click(sender, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

/// <summary>
/// A lightweight view model used only by <see cref="FolderPickerDialog"/>.
/// Mirrors the ID / name / children of a <see cref="FolderNode"/> but with
/// the excluded subtree already pruned, so the picker can't show invalid
/// targets. Mutating this tree does not affect the main view.
/// </summary>
public sealed class PickerFolderItem : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public Guid Id { get; }
    public string Name { get; }
    public Brush AccentBrush { get; }
    public ObservableCollection<PickerFolderItem> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    private PickerFolderItem(Guid id, string name, Brush accent, ObservableCollection<PickerFolderItem> children)
    {
        Id = id;
        Name = name;
        AccentBrush = accent;
        Children = children;
    }

    /// <summary>
    /// Returns a picker item mirroring <paramref name="node"/> with the entire
    /// subtree rooted at <paramref name="excludeSubtreeOf"/> removed. Returns
    /// <c>null</c> if <paramref name="node"/> itself is the excluded folder.
    /// </summary>
    public static PickerFolderItem? Build(FolderNode node, Guid? excludeSubtreeOf)
    {
        if (excludeSubtreeOf is { } x && node.Id == x) return null;

        var children = new ObservableCollection<PickerFolderItem>();
        foreach (var child in node.Children)
        {
            if (child is not FolderNode sub) continue;
            var picked = Build(sub, excludeSubtreeOf);
            if (picked is not null) children.Add(picked);
        }

        return new PickerFolderItem(node.Id, node.Name, node.AccentBrush, children);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
