using TeamStation.App.Mvvm;
using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

/// <summary>
/// Quick-connect bar state and fire-and-forget launch. Owns its own fields so
/// that MainViewModel is not also juggling the one-off launch surface.
/// </summary>
public sealed class QuickConnectViewModel : ViewModelBase
{
    private readonly Action<ConnectionEntry, bool> _launch;
    private readonly Func<bool> _canLaunch;
    private string _name = string.Empty;
    private string _teamViewerId = string.Empty;
    private string _password = string.Empty;
    private bool _saveConnection;

    public QuickConnectViewModel(Action<ConnectionEntry, bool> launch, Func<bool> canLaunch)
    {
        _launch = launch;
        _canLaunch = canLaunch;
        ConnectCommand = new RelayCommand(Connect, () => _canLaunch() && !string.IsNullOrWhiteSpace(TeamViewerId));
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    public string TeamViewerId
    {
        get => _teamViewerId;
        set
        {
            if (SetField(ref _teamViewerId, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasTeamViewerId));
                OnPropertyChanged(nameof(ConnectTooltip));
                ConnectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value ?? string.Empty);
    }

    public bool SaveConnection
    {
        get => _saveConnection;
        set => SetField(ref _saveConnection, value);
    }

    public RelayCommand ConnectCommand { get; }
    public bool HasTeamViewerId => !string.IsNullOrWhiteSpace(TeamViewerId);
    public string ConnectTooltip
    {
        get
        {
            if (!_canLaunch())
                return "Install or configure TeamViewer before launching.";
            return HasTeamViewerId
                ? "Launch this TeamViewer ID now."
                : "Enter a TeamViewer ID to connect.";
        }
    }

    public void RaiseCanLaunchChanged()
    {
        OnPropertyChanged(nameof(ConnectTooltip));
        ConnectCommand.RaiseCanExecuteChanged();
    }

    private void Connect()
    {
        var id = TeamViewerId.Trim();
        var entry = new ConnectionEntry
        {
            Name = string.IsNullOrWhiteSpace(Name) ? $"Quick {id}" : Name.Trim(),
            TeamViewerId = id,
            ProfileName = "Quick connect",
            Password = string.IsNullOrWhiteSpace(Password) ? null : Password.Trim(),
            Mode = ConnectionMode.RemoteControl,
            Quality = ConnectionQuality.AutoSelect,
            AccessControl = AccessControl.Undefined,
        };

        _launch(entry, SaveConnection);
        Password = string.Empty;
        if (!SaveConnection) Name = string.Empty;
    }
}
