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
    public string ProfileName => string.IsNullOrWhiteSpace(Model.ProfileName) ? "Default" : Model.ProfileName;
    public ConnectionMode? Mode => Model.Mode;
    public bool IsPinned => Model.IsPinned;
    public string PinDisplay => IsPinned ? "Pinned" : "Not pinned";
    public string RouteBadge => DisplayText.RouteBadge(Model.Mode);
    public string RouteDisplay => DisplayText.RouteDescription(Model.Mode);
    public string ModeDisplay => DisplayText.ModeLabel(Model.Mode);
    public string QualityDisplay => DisplayText.QualityLabel(Model.Quality);
    public string AccessControlDisplay => DisplayText.AccessLabel(Model.AccessControl);
    public bool HasPassword => !string.IsNullOrWhiteSpace(Model.Password);
    public string PasswordStateDisplay => HasPassword ? "Saved on this connection" : "Inherited from a folder or entered in TeamViewer";
    public bool HasProxy => Model.Proxy is not null;
    public string ProxyDisplay => Model.Proxy is null
        ? "No proxy configured"
        : string.IsNullOrWhiteSpace(Model.Proxy.Username)
            ? Model.Proxy.Endpoint
            : $"{Model.Proxy.Endpoint} as {Model.Proxy.Username}";
    public bool HasNotes => !string.IsNullOrWhiteSpace(Model.Notes);
    public string NotesDisplay => HasNotes ? Model.Notes!.Trim() : "No internal notes";
    public bool HasTags => Model.Tags.Count > 0;
    public string ParentPathDisplay => DisplayText.FormatPath(Parent);
    public string TagsSummary => HasTags ? string.Join("  ", Model.Tags.Select(tag => $"#{tag}")) : "No tags";
    public string CreatedDisplay => Model.CreatedUtc.LocalDateTime.ToString("g");
    public string ModifiedDisplay => Model.ModifiedUtc.LocalDateTime.ToString("g");
    public bool HasLastConnected => Model.LastConnectedUtc is not null;

    public string Summary =>
        $"{Model.TeamViewerId} • {ModeDisplay}";

    public string LastConnectedDisplay =>
        Model.LastConnectedUtc is null ? "Never launched" : Model.LastConnectedUtc.Value.LocalDateTime.ToString("g");
    public string WakeDisplay => string.IsNullOrWhiteSpace(Model.WakeMacAddress)
        ? "No Wake-on-LAN"
        : string.IsNullOrWhiteSpace(Model.WakeBroadcastAddress)
            ? Model.WakeMacAddress
            : $"{Model.WakeMacAddress} via {Model.WakeBroadcastAddress}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TeamViewerId));
        OnPropertyChanged(nameof(ProfileName));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinDisplay));
        OnPropertyChanged(nameof(RouteBadge));
        OnPropertyChanged(nameof(RouteDisplay));
        OnPropertyChanged(nameof(ModeDisplay));
        OnPropertyChanged(nameof(QualityDisplay));
        OnPropertyChanged(nameof(AccessControlDisplay));
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordStateDisplay));
        OnPropertyChanged(nameof(HasProxy));
        OnPropertyChanged(nameof(ProxyDisplay));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(NotesDisplay));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(ParentPathDisplay));
        OnPropertyChanged(nameof(TagsSummary));
        OnPropertyChanged(nameof(CreatedDisplay));
        OnPropertyChanged(nameof(ModifiedDisplay));
        OnPropertyChanged(nameof(HasLastConnected));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(LastConnectedDisplay));
        OnPropertyChanged(nameof(WakeDisplay));
    }
}
