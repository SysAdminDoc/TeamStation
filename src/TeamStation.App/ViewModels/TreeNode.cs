namespace TeamStation.App.ViewModels;

using TeamStation.App.Mvvm;

public abstract class TreeNode : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isMultiSelected;

    protected TreeNode(FolderNode? parent)
    {
        Parent = parent;
    }

    public FolderNode? Parent { get; internal set; }
    public abstract string Name { get; set; }
    public abstract Guid Id { get; }

    public bool IsExpanded { get => _isExpanded; set => SetField(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

    /// <summary>
    /// v0.3.5: Bulk multi-select infrastructure. WPF TreeView doesn't ship
    /// native multi-select; we track an additional <see cref="IsMultiSelected"/>
    /// flag per node and accumulate via a Ctrl-click handler on the tree.
    /// Plain click clears multi-selection on every node and falls back to the
    /// existing single-select <see cref="IsSelected"/> semantics, so the
    /// double-click-launch / arrow-key-nav / context-menu flows are unchanged.
    /// </summary>
    public bool IsMultiSelected { get => _isMultiSelected; set => SetField(ref _isMultiSelected, value); }
}
