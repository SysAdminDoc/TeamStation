using TeamStation.App.Mvvm;
using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

public sealed class EntryViewModel : ViewModelBase
{
    public EntryViewModel(ConnectionEntry model)
    {
        Model = model;
    }

    public ConnectionEntry Model { get; }

    public Guid Id => Model.Id;
    public string Name => Model.Name;
    public string TeamViewerId => Model.TeamViewerId;
    public ConnectionMode Mode => Model.Mode;
    public string LastConnectedDisplay =>
        Model.LastConnectedUtc is null ? "never" : Model.LastConnectedUtc.Value.LocalDateTime.ToString("g");

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TeamViewerId));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(LastConnectedDisplay));
    }
}
