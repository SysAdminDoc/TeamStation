using System.ComponentModel;
using TeamStation.App.ViewModels;
using TeamStation.Core.Models;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.5: Bulk multi-select infrastructure on the connection tree.
/// WPF TreeView has no native multi-select, so TreeNode tracks an
/// <see cref="TreeNode.IsMultiSelected"/> flag; MainViewModel walks
/// RootNodes for the SelectedNodes projection, BulkPin / BulkUnpin
/// commands operate on the multi-selection.
///
/// Tests scope to the testable surface — TreeNode property change
/// notifications + the flatten-walk that drives SelectedNodes —
/// without spinning up a full MainViewModel (which depends on real
/// repos + dialogs + launcher). The XAML-side wiring + bulk-action
/// behaviour are exercised by build + integration smoke.
/// </summary>
public class BulkMultiSelectTests
{
    [Fact]
    public void IsMultiSelected_default_is_false()
    {
        var node = new EntryNode(new ConnectionEntry { Name = "x", TeamViewerId = "111222333" }, parent: null);
        Assert.False(node.IsMultiSelected);
    }

    [Fact]
    public void IsMultiSelected_setter_raises_PropertyChanged()
    {
        var node = new EntryNode(new ConnectionEntry { Name = "x", TeamViewerId = "111222333" }, parent: null);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.IsMultiSelected = true;
        Assert.True(node.IsMultiSelected);
        Assert.Contains("IsMultiSelected", raised);
    }

    [Fact]
    public void IsMultiSelected_setter_does_not_raise_PropertyChanged_when_value_unchanged()
    {
        // Idempotent setter — important because Ctrl-click toggles run
        // through ToggleMultiSelection which always assigns; consumers
        // that assign an already-equal value should not receive a
        // spurious notification.
        var node = new EntryNode(new ConnectionEntry { Name = "x", TeamViewerId = "111222333" }, parent: null) { IsMultiSelected = true };
        var raised = 0;
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsMultiSelected") raised++;
        };
        node.IsMultiSelected = true; // already true
        Assert.Equal(0, raised);
    }

    [Fact]
    public void IsMultiSelected_independent_of_IsSelected()
    {
        var node = new EntryNode(new ConnectionEntry { Name = "x", TeamViewerId = "111222333" }, parent: null);
        node.IsSelected = true;
        Assert.False(node.IsMultiSelected);
        node.IsMultiSelected = true;
        Assert.True(node.IsSelected);
        Assert.True(node.IsMultiSelected);
        node.IsSelected = false;
        Assert.True(node.IsMultiSelected); // still set
    }

    [Fact]
    public void Flatten_walk_collects_only_multi_selected_nodes()
    {
        // Synthesises the same tree shape RootNodes carries: folder with
        // two entries plus a sibling folder, multi-select two entries
        // across the two folders, walk like SelectedNodes does.
        var f1 = new FolderNode(new Folder { Name = "F1" }, parent: null);
        var e1a = new EntryNode(new ConnectionEntry { Name = "E1A", TeamViewerId = "111111111" }, parent: f1);
        var e1b = new EntryNode(new ConnectionEntry { Name = "E1B", TeamViewerId = "222222222" }, parent: f1);
        f1.Children.Add(e1a);
        f1.Children.Add(e1b);

        var f2 = new FolderNode(new Folder { Name = "F2" }, parent: null);
        var e2a = new EntryNode(new ConnectionEntry { Name = "E2A", TeamViewerId = "333333333" }, parent: f2);
        f2.Children.Add(e2a);

        e1a.IsMultiSelected = true;
        e2a.IsMultiSelected = true;

        // Mirrors MainViewModel.SelectedNodes flatten logic.
        var roots = new List<TreeNode> { f1, f2 };
        var collected = new List<TreeNode>();
        foreach (var r in roots) Walk(r, collected);

        Assert.Equal(2, collected.Count);
        Assert.Contains(e1a, collected);
        Assert.Contains(e2a, collected);
        Assert.DoesNotContain(e1b, collected);
        Assert.DoesNotContain(f1, collected);
        Assert.DoesNotContain(f2, collected);

        static void Walk(TreeNode node, List<TreeNode> sink)
        {
            if (node.IsMultiSelected) sink.Add(node);
            if (node is FolderNode folder)
                foreach (var c in folder.Children) Walk(c, sink);
        }
    }

    [Fact]
    public void Flatten_walk_includes_a_multi_selected_folder_when_selected()
    {
        // Folder multi-select is allowed (the bulk Pin/Unpin commands
        // filter via OfType<EntryNode>, so a multi-selected folder is
        // a no-op for those commands but visually highlighted). This
        // pins that contract: the flatten walk does not silently drop
        // folder nodes.
        var f1 = new FolderNode(new Folder { Name = "F1" }, parent: null);
        var e1 = new EntryNode(new ConnectionEntry { Name = "E1", TeamViewerId = "111111111" }, parent: f1);
        f1.Children.Add(e1);

        f1.IsMultiSelected = true;
        e1.IsMultiSelected = true;

        var collected = new List<TreeNode>();
        Walk(f1, collected);

        Assert.Equal(2, collected.Count);
        Assert.Contains(f1, collected);
        Assert.Contains(e1, collected);

        // OfType<EntryNode> filter (mirrors BulkPinCommand.CanExecute).
        Assert.Single(collected.OfType<EntryNode>());
        Assert.Single(collected.OfType<FolderNode>());

        static void Walk(TreeNode node, List<TreeNode> sink)
        {
            if (node.IsMultiSelected) sink.Add(node);
            if (node is FolderNode folder)
                foreach (var c in folder.Children) Walk(c, sink);
        }
    }
}
