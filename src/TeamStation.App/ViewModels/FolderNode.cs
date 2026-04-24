using System.Collections.ObjectModel;
using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

public sealed class FolderNode : TreeNode
{
    public FolderNode(Folder model, FolderNode? parent) : base(parent)
    {
        Model = model;
    }

    public Folder Model { get; }
    public override Guid Id => Model.Id;

    public override string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<TreeNode> Children { get; } = new();
}
