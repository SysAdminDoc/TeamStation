using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Services;

/// <summary>
/// Self-contained drag-and-drop controller for the main TreeView. Instances
/// are wired from MainWindow code-behind; drop results are reported via
/// <paramref name="onDrop"/> which the view model persists and reloads.
/// </summary>
public sealed class TreeDragDrop
{
    private const string DragFormat = "TeamStation.TreeNode";
    private static readonly double MinDistance = SystemParameters.MinimumHorizontalDragDistance;
    private static readonly double MinDistanceY = SystemParameters.MinimumVerticalDragDistance;

    private readonly TreeView _tree;
    private readonly Action<TreeNode, FolderNode?> _onDrop;

    private Point? _dragStart;
    private TreeNode? _dragSource;

    public TreeDragDrop(TreeView tree, Action<TreeNode, FolderNode?> onDrop)
    {
        _tree = tree;
        _onDrop = onDrop;

        tree.AllowDrop = true;
        tree.PreviewMouseLeftButtonDown += OnMouseDown;
        tree.PreviewMouseMove += OnMouseMove;
        tree.PreviewMouseLeftButtonUp += OnMouseUp;
        tree.DragOver += OnDragOver;
        tree.Drop += OnDrop;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(_tree);
        _dragSource = HitNode(e.OriginalSource);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragSource = null;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragStart is not { } start || _dragSource is null) return;

        var p = e.GetPosition(_tree);
        if (Math.Abs(p.X - start.X) < MinDistance && Math.Abs(p.Y - start.Y) < MinDistanceY) return;

        var data = new DataObject(DragFormat, _dragSource);
        try { DragDrop.DoDragDrop(_tree, data, DragDropEffects.Move); }
        finally { _dragStart = null; _dragSource = null; }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ResolveDrop(e) is { } _ ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var resolution = ResolveDrop(e);
        if (resolution is not { } r) { e.Handled = true; return; }
        _onDrop(r.source, r.target);
        e.Handled = true;
    }

    private DropResolution? ResolveDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat)) return null;
        if (e.Data.GetData(DragFormat) is not TreeNode source) return null;

        var target = HitNode(e.OriginalSource);

        // Folder target: reparent into it (if not dropping on self / into own subtree)
        if (target is FolderNode folder)
        {
            if (source == folder) return null;
            if (source is FolderNode srcFolder && IsDescendant(folder, srcFolder)) return null;
            if (NodeParentId(source) == folder.Id) return null;
            return new DropResolution(source, folder);
        }

        // Entry target: reparent into the entry's parent folder (sibling move)
        if (target is EntryNode entry)
        {
            var destParent = entry.Parent;
            if (source is FolderNode srcFolder && destParent is not null && IsDescendant(destParent, srcFolder)) return null;
            if (NodeParentId(source) == destParent?.Id) return null;
            return new DropResolution(source, destParent);
        }

        // No item under cursor -> move to root
        if (NodeParentId(source) is null) return null;
        return new DropResolution(source, null);
    }

    private static Guid? NodeParentId(TreeNode node) => node.Parent?.Id;

    private static bool IsDescendant(FolderNode candidate, FolderNode ancestor)
    {
        for (var cursor = candidate; cursor is not null; cursor = cursor.Parent)
            if (cursor.Id == ancestor.Id) return true;
        return false;
    }

    private static TreeNode? HitNode(object originalSource)
    {
        var hit = originalSource as DependencyObject;
        while (hit is not null and not TreeViewItem)
            hit = VisualTreeHelper.GetParent(hit);
        return (hit as TreeViewItem)?.DataContext as TreeNode;
    }

    private readonly record struct DropResolution(TreeNode source, FolderNode? target);
}
