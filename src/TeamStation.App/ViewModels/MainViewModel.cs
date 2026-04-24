using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using TeamStation.App.Mvvm;
using TeamStation.App.Views;
using TeamStation.Core.Models;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : ViewModelBase
{
    private readonly EntryRepository _entries;
    private readonly TeamViewerLauncher _launcher;
    private readonly Func<ConnectionEntry, Window?, bool> _editDialog;

    private EntryViewModel? _selected;
    private string _status = string.Empty;
    private string _tvExePath = string.Empty;

    public MainViewModel(EntryRepository entries, TeamViewerLauncher launcher,
        Func<ConnectionEntry, Window?, bool> editDialog, string? tvExePath)
    {
        _entries = entries;
        _launcher = launcher;
        _editDialog = editDialog;
        _tvExePath = tvExePath ?? "TeamViewer.exe not found — install TeamViewer before launching";

        AddCommand = new RelayCommand(AddEntry);
        EditCommand = new RelayCommand(EditEntry, () => Selected is not null);
        DeleteCommand = new RelayCommand(DeleteEntry, () => Selected is not null);
        LaunchCommand = new RelayCommand(LaunchEntry, () => Selected is not null);

        Reload();
    }

    public ObservableCollection<EntryViewModel> Entries { get; } = new();

    public EntryViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                ((RelayCommand)LaunchCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string TvExePath { get => _tvExePath; private set => SetField(ref _tvExePath, value); }

    public System.Windows.Input.ICommand AddCommand { get; }
    public System.Windows.Input.ICommand EditCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand LaunchCommand { get; }

    private void Reload()
    {
        Entries.Clear();
        foreach (var e in _entries.GetAll())
            Entries.Add(new EntryViewModel(e));
        Status = Entries.Count == 0
            ? "No entries yet — click Add to create your first one."
            : $"{Entries.Count} entr{(Entries.Count == 1 ? "y" : "ies")}";
    }

    private void AddEntry()
    {
        var draft = new ConnectionEntry { Name = "New connection" };
        if (_editDialog(draft, Application.Current?.MainWindow))
        {
            _entries.Upsert(draft);
            Reload();
            Selected = Entries.FirstOrDefault(e => e.Id == draft.Id);
        }
    }

    private void EditEntry()
    {
        if (Selected is null) return;
        if (_editDialog(Selected.Model, Application.Current?.MainWindow))
        {
            _entries.Upsert(Selected.Model);
            Selected.Refresh();
            Status = $"Saved \"{Selected.Name}\".";
        }
    }

    private void DeleteEntry()
    {
        if (Selected is null) return;
        var choice = MessageBox.Show(
            Application.Current?.MainWindow!,
            $"Delete \"{Selected.Name}\"?\n\nThis cannot be undone.",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;

        _entries.Delete(Selected.Id);
        Reload();
    }

    private void LaunchEntry()
    {
        if (Selected is null) return;
        var outcome = _launcher.Launch(Selected.Model);
        if (outcome.Success)
        {
            _entries.TouchLastConnected(Selected.Id, DateTimeOffset.UtcNow);
            Selected.Model.LastConnectedUtc = DateTimeOffset.UtcNow;
            Selected.Refresh();
            Status = outcome.Uri is not null
                ? $"Launched {Selected.Name} via URI handler (pid {outcome.ProcessId?.ToString() ?? "?"})."
                : $"Launched {Selected.Name} (pid {outcome.ProcessId?.ToString() ?? "?"}).";
        }
        else
        {
            Status = $"Launch failed: {outcome.Error}";
            MessageBox.Show(
                Application.Current?.MainWindow!,
                outcome.Error ?? "Unknown error.",
                "Launch failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
