namespace TeamStation.App.ViewModels;

using TeamStation.App.Mvvm;

public abstract class TreeNode : ViewModelBase
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;

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
}
