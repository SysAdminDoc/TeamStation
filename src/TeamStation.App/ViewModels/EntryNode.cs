using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

public sealed class EntryNode : TreeNode
{
    public EntryNode(ConnectionEntry model, FolderNode? parent) : base(parent)
    {
        Model = model;
    }

    public ConnectionEntry Model { get; }

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

    public string TeamViewerId => Model.TeamViewerId;
    public ConnectionMode Mode => Model.Mode;

    public string Summary => $"{Model.TeamViewerId} — {Model.Mode}";

    public string LastConnectedDisplay =>
        Model.LastConnectedUtc is null ? "never" : Model.LastConnectedUtc.Value.LocalDateTime.ToString("g");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TeamViewerId));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(LastConnectedDisplay));
    }
}
