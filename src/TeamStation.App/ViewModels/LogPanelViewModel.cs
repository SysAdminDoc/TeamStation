using System.Collections.ObjectModel;
using System.Windows.Data;
using TeamStation.App.Mvvm;

namespace TeamStation.App.ViewModels;

/// <summary>
/// The embedded 500-entry log panel: rolling append, severity-tagged, thread-safe.
/// Exposed on MainViewModel as <see cref="MainViewModel.LogPanel"/> and mirrored
/// through legacy top-level properties so XAML bindings keep working.
/// </summary>
public sealed class LogPanelViewModel : ViewModelBase
{
    private const int MaxLogEntries = 500;
    private readonly object _sync = new();
    private bool _isVisible;

    public LogPanelViewModel()
    {
        BindingOperations.EnableCollectionSynchronization(Entries, _sync);
        ClearCommand = new RelayCommand(Clear, () => Entries.Count > 0);
        ToggleCommand = new RelayCommand(() => IsVisible = !IsVisible);
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public bool HasEntries => Entries.Count > 0;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetField(ref _isVisible, value))
                OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string ButtonText => IsVisible ? "Hide activity" : "Show activity";

    public string Summary => Entries.Count == 0
        ? "No activity yet."
        : $"Showing the latest {Entries.Count} event{(Entries.Count == 1 ? string.Empty : "s")}.";

    public string ClearTooltip => HasEntries
        ? "Clear activity log"
        : "No activity to clear";

    public string ExportTooltip => HasEntries
        ? "Export activity log as newline-delimited JSON."
        : "No activity to export";

    public RelayCommand ClearCommand { get; }
    public RelayCommand ToggleCommand { get; }

    public void Append(LogLevel level, string message)
    {
        Entries.Add(new LogEntry(DateTimeOffset.Now, level, message));
        while (Entries.Count > MaxLogEntries) Entries.RemoveAt(0);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ClearTooltip));
        OnPropertyChanged(nameof(ExportTooltip));
        ClearCommand.RaiseCanExecuteChanged();
    }

    public void Clear()
    {
        Entries.Clear();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ClearTooltip));
        OnPropertyChanged(nameof(ExportTooltip));
        ClearCommand.RaiseCanExecuteChanged();
    }
}
