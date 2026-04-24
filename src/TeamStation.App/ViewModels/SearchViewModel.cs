using TeamStation.App.Mvvm;
using TeamStation.App.Services;

namespace TeamStation.App.ViewModels;

/// <summary>
/// Owns the tree search string and saved-search list. Emits
/// <see cref="SearchTextChanged"/> whenever the bound text changes so that the
/// MainViewModel can re-run its tree visibility pass without SearchViewModel
/// reaching into the tree itself.
/// </summary>
public sealed class SearchViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private string _searchText = string.Empty;

    public SearchViewModel(AppSettings settings, SettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
        SaveCommand = new RelayCommand(SaveCurrent, () => HasText);
        ApplyCommand = new RelayCommand(ApplySaved, parameter => parameter is string { Length: > 0 });
        ClearCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public event EventHandler? SearchTextChanged;
    public event Action<string>? SavedSearchApplied;
    public event Action<string>? SavedSearchAdded;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasText));
                SaveCommand.RaiseCanExecuteChanged();
                SearchTextChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool HasText => !string.IsNullOrWhiteSpace(_searchText);

    public IReadOnlyList<string> SavedSearches => _settings.SavedSearches;

    public bool HasSavedSearches => SavedSearches.Count > 0;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ClearCommand { get; }

    public void RaiseSavedSearchesChanged()
    {
        OnPropertyChanged(nameof(SavedSearches));
        OnPropertyChanged(nameof(HasSavedSearches));
    }

    private void SaveCurrent()
    {
        var value = SearchText.Trim();
        if (value.Length == 0) return;

        if (!_settings.SavedSearches.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            _settings.SavedSearches.Add(value);
            _settingsService.Save(_settings);
            RaiseSavedSearchesChanged();
        }

        SavedSearchAdded?.Invoke(value);
    }

    private void ApplySaved(object? parameter)
    {
        if (parameter is not string search || string.IsNullOrWhiteSpace(search))
            return;

        SearchText = search;
        SavedSearchApplied?.Invoke(search);
    }
}
