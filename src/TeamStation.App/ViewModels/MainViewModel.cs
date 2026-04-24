using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using TeamStation.App.Mvvm;
using TeamStation.App.Services;
using TeamStation.App.Views;
using TeamStation.Core.Models;
using TeamStation.Core.Serialization;
using TeamStation.Core.Services;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : ViewModelBase
{
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;
    private readonly SessionRepository _sessions;
    private readonly AuditLogRepository _auditLog;
    private readonly TeamViewerLauncher _launcher;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly TeamViewerCloudSyncService _cloudSync = new();
    private readonly Func<ConnectionEntry, Window?, bool> _editEntryDialog;
    private readonly Func<Folder, Window?, bool> _editFolderDialog;
    private readonly Func<Window?, string?> _chooseExportPath;
    private readonly Func<Window?, string?> _chooseImportPath;
    private readonly Func<Window?, string?> _chooseImportCsvPath;
    private readonly Func<Window?, string, bool> _confirmDialog;

    private TreeNode? _selected;
    private string _status = string.Empty;
    private string _tvExePath;
    private string _searchText = string.Empty;
    private bool _isLogVisible;
    private Dictionary<Guid, Folder> _foldersById = new();
    private const int MaxLogEntries = 500;
    private readonly string _startupVersion;
    private readonly string? _startupDbPath;
    private readonly object _logLock = new();
    private bool _isTeamViewerReady;
    private Brush _statusBrush = Brushes.Transparent;
    private string _statusTag = "Ready";
    private int _folderCount;
    private int _entryCount;
    private int _visibleFolderCount;
    private int _visibleEntryCount;
    private string _quickName = string.Empty;
    private string _quickTeamViewerId = string.Empty;
    private string _quickPassword = string.Empty;
    private bool _quickSaveConnection;

    public MainViewModel(
        EntryRepository entries,
        FolderRepository folders,
        TeamViewerLauncher launcher,
        Func<ConnectionEntry, Window?, bool> editEntryDialog,
        Func<Folder, Window?, bool> editFolderDialog,
        Func<Window?, string?> chooseExportPath,
        Func<Window?, string?> chooseImportPath,
        Func<Window?, string?> chooseImportCsvPath,
        Func<Window?, string, bool> confirmDialog,
        AppSettings settings,
        SettingsService settingsService,
        SessionRepository sessions,
        AuditLogRepository auditLog,
        string? tvExePath,
        string? startupVersion = null,
        string? startupDbPath = null)
    {
        _entries = entries;
        _folders = folders;
        _sessions = sessions;
        _auditLog = auditLog;
        _launcher = launcher;
        _settings = settings;
        _settingsService = settingsService;
        _editEntryDialog = editEntryDialog;
        _editFolderDialog = editFolderDialog;
        _chooseExportPath = chooseExportPath;
        _chooseImportPath = chooseImportPath;
        _chooseImportCsvPath = chooseImportCsvPath;
        _confirmDialog = confirmDialog;
        _isTeamViewerReady = !string.IsNullOrWhiteSpace(tvExePath);
        _tvExePath = tvExePath ?? "TeamViewer.exe not found — install TeamViewer before launching";
        _startupVersion = startupVersion ?? "dev";
        _startupDbPath = startupDbPath;

        AddEntryCommand = new RelayCommand(AddEntry);
        AddFolderCommand = new RelayCommand(AddFolder);
        AddSubfolderCommand = new RelayCommand(AddSubfolder, () => Selected is FolderNode);
        RenameCommand = new RelayCommand(Rename, () => Selected is not null);
        MoveCommand = new RelayCommand(Move, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        EditCommand = new RelayCommand(EditSelected, () => Selected is not null);
        LaunchCommand = new RelayCommand(Launch, () => Selected is EntryNode && IsTeamViewerReady);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ImportCsvCommand = new RelayCommand(ImportCsvFile);
        QuickConnectCommand = new RelayCommand(QuickConnect, () => IsTeamViewerReady && !string.IsNullOrWhiteSpace(QuickTeamViewerId));
        TogglePinCommand = new RelayCommand(TogglePin, () => Selected is EntryNode);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ImportTeamViewerHistoryCommand = new RelayCommand(ImportTeamViewerHistory);
        SyncTeamViewerCloudCommand = new RelayCommand(() => _ = SyncTeamViewerCloudAsync());
        ExportSessionsCommand = new RelayCommand(ExportSessions);
        SaveSearchCommand = new RelayCommand(SaveSearch, () => HasSearchText);
        ApplySavedSearchCommand = new RelayCommand(ApplySavedSearch, parameter => parameter is string { Length: > 0 });
        RunExternalToolCommand = new RelayCommand(RunExternalTool, parameter => Selected is EntryNode && parameter is ExternalToolDefinition);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ClearLogCommand = new RelayCommand(ClearLog, () => Log.Count > 0);
        ToggleLogCommand = new RelayCommand(() => IsLogVisible = !IsLogVisible);

        // Enable cross-thread access for collection bindings; all *mutations*
        // still happen on the UI thread, but this makes future async writes safe.
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Log, _logLock);
        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(RootNodes, _logLock);

        Reload();
        AppendLog(LogLevel.Info, $"TeamStation v{_startupVersion} started.");
        if (!string.IsNullOrEmpty(_startupDbPath))
            AppendLog(LogLevel.Info, $"Database: {_startupDbPath}");
        AppendLog(LogLevel.Info, tvExePath is null
            ? "TeamViewer.exe not found — launches will be disabled until TeamViewer is installed."
            : $"TeamViewer.exe: {tvExePath}");
    }

    public event EventHandler? TrayMenuInvalidated;

    public ObservableCollection<TreeNode> RootNodes { get; } = new();

    public TreeNode? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                foreach (var cmd in new[] { AddSubfolderCommand, RenameCommand, MoveCommand, DeleteCommand, EditCommand, LaunchCommand, TogglePinCommand, RunExternalToolCommand })
                    ((RelayCommand)cmd).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedIsEntry));
                OnPropertyChanged(nameof(SelectedIsFolder));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowSelectionPlaceholder));
                OnPropertyChanged(nameof(SelectedPinText));
            }
        }
    }

    public bool SelectedIsEntry => Selected is EntryNode;
    public bool SelectedIsFolder => Selected is FolderNode;
    public bool HasSelection => Selected is not null;
    public bool ShowSelectionPlaceholder => Selected is null;
    public string SelectedPinText => Selected is EntryNode { Model.IsPinned: true } ? "Unpin" : "Pin";

    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string StatusTag { get => _statusTag; private set => SetField(ref _statusTag, value); }
    public Brush StatusBrush { get => _statusBrush; private set => SetField(ref _statusBrush, value); }
    public bool IsTeamViewerReady
    {
        get => _isTeamViewerReady;
        private set
        {
            if (SetField(ref _isTeamViewerReady, value))
            {
                OnPropertyChanged(nameof(TeamViewerStatusText));
                ((RelayCommand)LaunchCommand).RaiseCanExecuteChanged();
                ((RelayCommand)QuickConnectCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public string TeamViewerStatusText => _isTeamViewerReady ? "TeamViewer ready" : "Install TeamViewer";
    public string DatabasePathDisplay => _startupDbPath ?? "Database path unavailable";
    public string DatabaseLocationDisplay => _startupDbPath is null
        ? "Portable mode"
        : Path.GetDirectoryName(_startupDbPath) ?? _startupDbPath;
    public int FolderCount { get => _folderCount; private set => SetField(ref _folderCount, value); }
    public int EntryCount { get => _entryCount; private set => SetField(ref _entryCount, value); }
    public int VisibleFolderCount { get => _visibleFolderCount; private set => SetField(ref _visibleFolderCount, value); }
    public int VisibleEntryCount { get => _visibleEntryCount; private set => SetField(ref _visibleEntryCount, value); }
    public bool HasAnyItems => FolderCount + EntryCount > 0;
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    public bool ShowWelcomeState => !HasAnyItems;
    public bool ShowNoSearchResultsState => HasAnyItems && HasSearchText && VisibleFolderCount + VisibleEntryCount == 0;
    public string TreeSummary => HasSearchText
        ? $"{DisplayText.Count(VisibleEntryCount, "matching connection")}, {DisplayText.Count(VisibleFolderCount, "visible folder")}"
        : $"{DisplayText.Count(EntryCount, "connection")}, {DisplayText.Count(FolderCount, "folder")}";
    public string SearchHintText => HasSearchText
        ? $"Filtering names, IDs, notes, and tags for \"{SearchText.Trim()}\"."
        : "Search names, TeamViewer IDs, notes, or tags. Double-click a connection to launch it.";
    public string LogSummary => Log.Count == 0
        ? "No activity yet."
        : $"Showing the latest {Log.Count} event{(Log.Count == 1 ? string.Empty : "s")}.";
    public string ActivityButtonText => IsLogVisible ? "Hide activity" : "Show activity";
    public IReadOnlyList<ExternalToolDefinition> ExternalTools => _settings.ExternalTools;
    public bool HasExternalTools => ExternalTools.Count > 0;
    public IReadOnlyList<string> SavedSearches => _settings.SavedSearches;
    public bool HasSavedSearches => SavedSearches.Count > 0;

    public string QuickName
    {
        get => _quickName;
        set => SetField(ref _quickName, value ?? string.Empty);
    }

    public string QuickTeamViewerId
    {
        get => _quickTeamViewerId;
        set
        {
            if (SetField(ref _quickTeamViewerId, value ?? string.Empty))
                ((RelayCommand)QuickConnectCommand).RaiseCanExecuteChanged();
        }
    }

    public string QuickPassword
    {
        get => _quickPassword;
        set => SetField(ref _quickPassword, value ?? string.Empty);
    }

    public bool QuickSaveConnection
    {
        get => _quickSaveConnection;
        set => SetField(ref _quickSaveConnection, value);
    }

    private void AppendLog(LogLevel level, string message)
    {
        Log.Add(new LogEntry(DateTimeOffset.Now, level, message));
        while (Log.Count > MaxLogEntries) Log.RemoveAt(0);
        OnPropertyChanged(nameof(LogSummary));
        ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
    }

    private void ReportStatus(LogLevel level, string message)
    {
        Status = message;
        ApplyStatusTone(level);
        AppendLog(level, message);
    }

    private void Audit(string action, string targetType, Guid? targetId, string summary, string? detail = null)
    {
        _auditLog.Append(new AuditEvent
        {
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary,
            Detail = detail,
        });
    }

    private void MirrorDatabase()
    {
        try
        {
            CloudMirrorService.MirrorDatabase(_startupDbPath, _settings.CloudSyncFolder);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, $"Cloud mirror skipped: {ex.Message}");
        }
    }

    private void NotifyRecentsChanged()
    {
        TrayMenuInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public string TvExePath { get => _tvExePath; private set => SetField(ref _tvExePath, value); }

    public System.Windows.Input.ICommand AddEntryCommand { get; }
    public System.Windows.Input.ICommand AddFolderCommand { get; }
    public System.Windows.Input.ICommand AddSubfolderCommand { get; }
    public System.Windows.Input.ICommand RenameCommand { get; }
    public System.Windows.Input.ICommand MoveCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand EditCommand { get; }
    public System.Windows.Input.ICommand LaunchCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }
    public System.Windows.Input.ICommand ImportCommand { get; }
    public System.Windows.Input.ICommand ImportCsvCommand { get; }
    public System.Windows.Input.ICommand QuickConnectCommand { get; }
    public System.Windows.Input.ICommand TogglePinCommand { get; }
    public System.Windows.Input.ICommand OpenSettingsCommand { get; }
    public System.Windows.Input.ICommand ImportTeamViewerHistoryCommand { get; }
    public System.Windows.Input.ICommand SyncTeamViewerCloudCommand { get; }
    public System.Windows.Input.ICommand ExportSessionsCommand { get; }
    public System.Windows.Input.ICommand SaveSearchCommand { get; }
    public System.Windows.Input.ICommand ApplySavedSearchCommand { get; }
    public System.Windows.Input.ICommand RunExternalToolCommand { get; }
    public System.Windows.Input.ICommand ClearSearchCommand { get; }
    public System.Windows.Input.ICommand ClearLogCommand { get; }
    public System.Windows.Input.ICommand ToggleLogCommand { get; }

    public ObservableCollection<LogEntry> Log { get; } = new();

    public bool IsLogVisible
    {
        get => _isLogVisible;
        set
        {
            if (SetField(ref _isLogVisible, value))
                OnPropertyChanged(nameof(ActivityButtonText));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
                NotifySurfacePropertyChanges();
            }
        }
    }

    public void Reload()
    {
        var selectedId = Selected?.Id;
        var folders = _folders.GetAll();
        var entries = _entries.GetAll();

        _foldersById = folders.ToDictionary(f => f.Id, f => f);

        var folderNodes = folders.ToDictionary(
            f => f.Id,
            f => new FolderNode(f, parent: null));

        foreach (var f in folders)
        {
            if (f.ParentFolderId is { } pid && folderNodes.TryGetValue(pid, out var parent))
            {
                folderNodes[f.Id].Parent = parent;
                parent.Children.Add(folderNodes[f.Id]);
            }
        }

        var roots = new List<TreeNode>();
        foreach (var node in folderNodes.Values.Where(n => n.Parent is null))
            roots.Add(node);

        foreach (var entry in entries)
        {
            FolderNode? parent = null;
            if (entry.ParentFolderId is { } fid && folderNodes.TryGetValue(fid, out var p))
                parent = p;
            var node = new EntryNode(entry, parent);
            if (parent is null) roots.Add(node);
            else parent.Children.Add(node);
        }

        SortRoots(roots);
        foreach (var folder in folderNodes.Values)
            SortChildren(folder);

        RootNodes.Clear();
        foreach (var r in roots) RootNodes.Add(r);

        FolderCount = folders.Count;
        EntryCount = entries.Count;
        ApplyFilter();

        if (selectedId is { } id && FindById(RootNodes, id) is not null)
        {
            SelectById(id);
        }
        else if (FindFirstVisibleNode(RootNodes) is { } firstVisible)
        {
            SelectById(firstVisible.Id);
        }
        else
        {
            Selected = null;
        }

        if (!HasAnyItems)
        {
            Status = "Create a folder or connection to start building your TeamViewer workspace.";
            ApplyStatusTone(LogLevel.Info);
        }
        else if (string.IsNullOrWhiteSpace(Status))
        {
            Status = "Workspace ready. Select a connection to inspect it or double-click to launch.";
            ApplyStatusTone(LogLevel.Info);
        }

        NotifySurfacePropertyChanges();
    }

    private static int CompareNodes(TreeNode a, TreeNode b)
    {
        // Folders first, then entries — each sorted case-insensitively by name
        var aIsFolder = a is FolderNode;
        var bIsFolder = b is FolderNode;
        if (aIsFolder != bIsFolder) return aIsFolder ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static void SortRoots(List<TreeNode> roots) => roots.Sort(CompareNodes);

    private static void SortChildren(FolderNode folder)
    {
        var ordered = folder.Children.OrderBy(c => c, Comparer<TreeNode>.Create(CompareNodes)).ToList();
        folder.Children.Clear();
        foreach (var c in ordered) folder.Children.Add(c);
    }

    private void UpdateVisibleCounts()
    {
        VisibleFolderCount = EnumerateAll(RootNodes).Count(node => node.IsVisible && node is FolderNode);
        VisibleEntryCount = EnumerateAll(RootNodes).Count(node => node.IsVisible && node is EntryNode);
    }

    private void NotifySurfacePropertyChanges()
    {
        OnPropertyChanged(nameof(HasAnyItems));
        OnPropertyChanged(nameof(HasSearchText));
        OnPropertyChanged(nameof(ShowWelcomeState));
        OnPropertyChanged(nameof(ShowNoSearchResultsState));
        OnPropertyChanged(nameof(TreeSummary));
        OnPropertyChanged(nameof(SearchHintText));
        OnPropertyChanged(nameof(HasSavedSearches));
        ((RelayCommand)SaveSearchCommand).RaiseCanExecuteChanged();
    }

    private void ApplyStatusTone(LogLevel level)
    {
        StatusTag = level switch
        {
            LogLevel.Success => "Saved",
            LogLevel.Warning => "Attention",
            LogLevel.Error => "Problem",
            _ => "Ready",
        };

        StatusBrush = level switch
        {
            LogLevel.Success => TryBrush("GreenBrush", Brushes.LightGreen),
            LogLevel.Warning => TryBrush("YellowBrush", Brushes.Khaki),
            LogLevel.Error => TryBrush("RedBrush", Brushes.Salmon),
            _ => TryBrush("BlueBrush", Brushes.LightSkyBlue),
        };
    }

    private static Brush TryBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    // ---- Entry commands ----

    private void AddEntry()
    {
        var parentFolder = Selected switch
        {
            FolderNode f => f,
            EntryNode e => e.Parent,
            _ => null
        };
        // When created inside a folder, start with all inheritable fields set to
        // null so the entry defers to the folder chain at launch time. Users can
        // override any field in the editor.
        var draft = new ConnectionEntry
        {
            Name = "New connection",
            ProfileName = "Default",
            ParentFolderId = parentFolder?.Id,
            Password = null,
            Mode = parentFolder is null ? ConnectionMode.RemoteControl : null,
            Quality = parentFolder is null ? ConnectionQuality.AutoSelect : null,
            AccessControl = parentFolder is null ? AccessControl.Undefined : null,
        };
        if (_editEntryDialog(draft, Application.Current?.MainWindow))
        {
            _entries.Upsert(draft);
            Reload();
            SelectById(draft.Id);
            Audit("create", "connection", draft.Id, $"Created connection \"{draft.Name}\".");
            MirrorDatabase();
            ReportStatus(LogLevel.Success, $"Created connection \"{draft.Name}\".");
        }
    }

    private void QuickConnect()
    {
        var id = QuickTeamViewerId.Trim();
        var entry = new ConnectionEntry
        {
            Name = string.IsNullOrWhiteSpace(QuickName) ? $"Quick {id}" : QuickName.Trim(),
            TeamViewerId = id,
            ProfileName = "Quick connect",
            Password = string.IsNullOrWhiteSpace(QuickPassword) ? null : QuickPassword.Trim(),
            Mode = ConnectionMode.RemoteControl,
            Quality = ConnectionQuality.AutoSelect,
            AccessControl = AccessControl.Undefined,
        };

        if (QuickSaveConnection)
        {
            _entries.Upsert(entry);
            Reload();
            SelectById(entry.Id);
            Audit("create", "connection", entry.Id, $"Saved quick connection \"{entry.Name}\".");
            MirrorDatabase();
        }

        LaunchEntry(entry, persistLastConnected: QuickSaveConnection);
        QuickPassword = string.Empty;
        if (!QuickSaveConnection)
            QuickName = string.Empty;
    }

    private void EditSelected()
    {
        switch (Selected)
        {
            case EntryNode entry:
                if (_editEntryDialog(entry.Model, Application.Current?.MainWindow))
                {
                    _entries.Upsert(entry.Model);
                    entry.Refresh();
                    Reload();
                    SelectById(entry.Id);
                    Audit("edit", "connection", entry.Id, $"Saved connection \"{entry.Name}\".");
                    MirrorDatabase();
                    ReportStatus(LogLevel.Success, $"Saved \"{entry.Name}\".");
                }
                break;

            case FolderNode folder:
                if (_editFolderDialog(folder.Model, Application.Current?.MainWindow))
                {
                    _folders.Upsert(folder.Model);
                    Reload();
                    SelectById(folder.Id);
                    Audit("edit", "folder", folder.Id, $"Saved folder \"{folder.Name}\".");
                    MirrorDatabase();
                    ReportStatus(LogLevel.Success, $"Saved folder \"{folder.Name}\".");
                }
                break;
        }
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        if (query.Length == 0)
        {
            foreach (var node in EnumerateAll(RootNodes))
                node.IsVisible = true;
            UpdateVisibleCounts();
            return;
        }

        foreach (var root in RootNodes)
            _ = ComputeVisibility(root, query);

        UpdateVisibleCounts();
    }

    private static bool ComputeVisibility(TreeNode node, string query)
    {
        var selfMatches = NodeMatches(node, query);

        if (node is FolderNode folder)
        {
            var anyChildVisible = false;
            foreach (var child in folder.Children)
                if (ComputeVisibility(child, query))
                    anyChildVisible = true;

            var visible = selfMatches || anyChildVisible;
            node.IsVisible = visible;
            if (visible && anyChildVisible) folder.IsExpanded = true;
            return visible;
        }

        node.IsVisible = selfMatches;
        return selfMatches;
    }

    private static bool NodeMatches(TreeNode node, string query)
    {
        if (Contains(node.Name, query)) return true;
        if (node is EntryNode entry)
        {
            if (Contains(entry.Model.TeamViewerId, query)) return true;
            if (Contains(entry.Model.Notes, query)) return true;
            foreach (var tag in entry.Model.Tags)
                if (Contains(tag, query)) return true;
        }
        return false;
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<TreeNode> EnumerateAll(IEnumerable<TreeNode> roots)
    {
        foreach (var n in roots)
        {
            yield return n;
            if (n is FolderNode f)
                foreach (var child in EnumerateAll(f.Children))
                    yield return child;
        }
    }

    private static TreeNode? FindFirstVisibleNode(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsVisible)
                return node;

            if (node is FolderNode folder && FindFirstVisibleNode(folder.Children) is { } child)
                return child;
        }

        return null;
    }

    private void Export()
    {
        var path = _chooseExportPath(Application.Current?.MainWindow);
        if (path is null) return;

        var anyPasswords = _entries.GetAll().Any(e => !string.IsNullOrEmpty(e.Password))
                           || _folders.GetAll().Any(f => !string.IsNullOrEmpty(f.DefaultPassword));
        if (anyPasswords && !_confirmDialog(Application.Current?.MainWindow,
                "This backup contains saved passwords in plain text.\n\n" +
                "Anyone who can open the file can read those credentials. Store it only where you would store a password vault export.\n\n" +
                "Continue?"))
        {
            ReportStatus(LogLevel.Warning, "Backup cancelled.");
            return;
        }

        try
        {
            var backup = JsonBackup.Build(_folders.GetAll(), _entries.GetAll());
            AtomicWriteAllText(path, backup);
            Audit("export", "json-backup", null, $"Wrote backup to {path}.");
            ReportStatus(LogLevel.Success, $"Backup written to {path}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"Backup failed: {ex.Message}");
            MessageBox.Show(Application.Current?.MainWindow!, ex.ToString(), "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="destination"/> via a
    /// sibling temp file + <see cref="File.Move(string,string,bool)"/> so that
    /// a crash or disk-full mid-write cannot leave a truncated backup on disk.
    /// </summary>
    private static void AtomicWriteAllText(string destination, string contents)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".";
        var temp = Path.Combine(dir, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup; ignore cleanup failures so the original error surfaces.
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* swallow */ }
            throw;
        }
    }

    private void Import()
    {
        var path = _chooseImportPath(Application.Current?.MainWindow);
        if (path is null) return;

        try
        {
            var text = File.ReadAllText(path);
            var (folders, entries) = JsonBackup.Parse(text);

            if (!_confirmDialog(Application.Current?.MainWindow,
                $"Restore {DisplayText.Count(folders.Count, "folder")} and {DisplayText.Count(entries.Count, "connection")} from\n\n{path}\n\n" +
                "Existing items with matching IDs will be overwritten. Continue?"))
            {
                ReportStatus(LogLevel.Warning, "Restore cancelled.");
                return;
            }

            // Upsert folders before entries so foreign keys resolve. Union of
            // in-import folder IDs with current-DB IDs is the full set of valid
            // parent targets; anything outside that set is a dangling pointer
            // that would trip foreign_keys=ON.
            var knownFolderIds = new HashSet<Guid>(
                _folders.GetAll().Select(f => f.Id).Concat(folders.Select(f => f.Id)));
            foreach (var f in folders)
                if (f.ParentFolderId is { } pid && !knownFolderIds.Contains(pid))
                    f.ParentFolderId = null;
            foreach (var e in entries)
                if (e.ParentFolderId is { } pid && !knownFolderIds.Contains(pid))
                    e.ParentFolderId = null;

            foreach (var f in folders) _folders.Upsert(f);
            foreach (var e in entries) _entries.Upsert(e);
            Reload();
            Audit("import", "json-backup", null, $"Restored {folders.Count} folders and {entries.Count} connections from {path}.");
            MirrorDatabase();
            ReportStatus(LogLevel.Success, $"Restored {DisplayText.Count(folders.Count, "folder")} and {DisplayText.Count(entries.Count, "connection")}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"Restore failed: {ex.Message}");
            MessageBox.Show(Application.Current?.MainWindow!, ex.ToString(), "Restore failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLog()
    {
        Log.Clear();
        OnPropertyChanged(nameof(LogSummary));
        ((RelayCommand)ClearLogCommand).RaiseCanExecuteChanged();
        Status = "Activity cleared.";
        ApplyStatusTone(LogLevel.Info);
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        _settingsService.Save(_settings);
        TvExePath = !string.IsNullOrWhiteSpace(_settings.TeamViewerPathOverride)
            ? _settings.TeamViewerPathOverride
            : TeamViewerPathResolver.Resolve() ?? "TeamViewer.exe not found - install TeamViewer before launching";
        IsTeamViewerReady = File.Exists(TvExePath);
        OnPropertyChanged(nameof(ExternalTools));
        OnPropertyChanged(nameof(HasExternalTools));
        OnPropertyChanged(nameof(SavedSearches));
        OnPropertyChanged(nameof(HasSavedSearches));
        Audit("settings", "application", null, "Updated settings.");
        ReportStatus(LogLevel.Success, "Settings saved.");
    }

    private void ImportTeamViewerHistory()
    {
        var paths = TeamViewerHistoryImport.DefaultPaths();
        var entries = TeamViewerHistoryImport.ParseFiles(paths, _entries.GetAll());
        if (entries.Count == 0)
        {
            ReportStatus(LogLevel.Warning, "No new TeamViewer history entries were found.");
            return;
        }

        foreach (var entry in entries)
            _entries.Upsert(entry);

        Reload();
        Audit("import", "teamviewer-history", null, $"Imported {DisplayText.Count(entries.Count, "connection")} from TeamViewer history.");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Imported {DisplayText.Count(entries.Count, "connection")} from TeamViewer history.");
    }

    private async Task SyncTeamViewerCloudAsync()
    {
        try
        {
            var result = await _cloudSync.PullAsync(_settings.TeamViewerApiToken ?? string.Empty);
            var existingByTvId = _entries.GetAll().ToDictionary(e => e.TeamViewerId, e => e, StringComparer.Ordinal);

            foreach (var folder in result.Folders)
                _folders.Upsert(folder);

            var imported = 0;
            foreach (var entry in result.Entries)
            {
                if (entry.TeamViewerId.Length is < 8 or > 12)
                    continue;

                if (existingByTvId.TryGetValue(entry.TeamViewerId, out var existing))
                {
                    _entries.Upsert(MergeCloudEntry(entry, existing));
                }
                else
                {
                    _entries.Upsert(entry);
                }
                imported++;
            }

            Reload();
            Audit("sync", "teamviewer-cloud", null, $"Synced {DisplayText.Count(imported, "connection")} from TeamViewer cloud.");
            MirrorDatabase();
            ReportStatus(LogLevel.Success, $"Synced {DisplayText.Count(imported, "connection")} from TeamViewer cloud.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"TeamViewer cloud sync failed: {ex.Message}");
            MessageBox.Show(Application.Current?.MainWindow!, ex.Message, "TeamViewer cloud sync failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSessions()
    {
        if (string.IsNullOrWhiteSpace(_startupDbPath))
        {
            ReportStatus(LogLevel.Error, "Session export path is unavailable.");
            return;
        }

        var path = Path.Combine(Path.GetDirectoryName(_startupDbPath) ?? ".", $"teamstation-sessions-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        _sessions.ExportCsv(path);
        Audit("export", "sessions", null, $"Exported session history to {path}.");
        ReportStatus(LogLevel.Success, $"Session history exported to {path}.");
    }

    private void SaveSearch()
    {
        var value = SearchText.Trim();
        if (value.Length == 0)
            return;

        if (!_settings.SavedSearches.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            _settings.SavedSearches.Add(value);
            _settingsService.Save(_settings);
            OnPropertyChanged(nameof(SavedSearches));
            OnPropertyChanged(nameof(HasSavedSearches));
            Audit("create", "saved-search", null, $"Saved search \"{value}\".");
        }

        ReportStatus(LogLevel.Success, $"Saved search \"{value}\".");
    }

    private void ApplySavedSearch(object? parameter)
    {
        if (parameter is not string search || string.IsNullOrWhiteSpace(search))
            return;

        SearchText = search;
        ReportStatus(LogLevel.Info, $"Applied saved search \"{search}\".");
    }

    private void RunExternalTool(object? parameter)
    {
        if (Selected is not EntryNode entry || parameter is not ExternalToolDefinition tool)
            return;

        try
        {
            ExternalToolRunner.Run(tool, InheritanceResolver.Resolve(entry.Model, _foldersById));
            Audit("run", "external-tool", entry.Id, $"Ran {tool.Name} for \"{entry.Name}\".");
            ReportStatus(LogLevel.Success, $"Ran {tool.Name} for \"{entry.Name}\".");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"External tool failed: {ex.Message}");
        }
    }

    private void ImportCsvFile()
    {
        var path = _chooseImportCsvPath(Application.Current?.MainWindow);
        if (path is null) return;

        try
        {
            var text = File.ReadAllText(path);
            var result = CsvImport.Parse(text, _folders.GetAll());

            if (result.Errors.Count > 0)
            {
                foreach (var err in result.Errors) AppendLog(LogLevel.Error, $"CSV: {err}");
                ReportStatus(LogLevel.Error, "CSV import stopped. Review the activity panel for column-mapping errors.");
                MessageBox.Show(Application.Current?.MainWindow!,
                    string.Join('\n', result.Errors),
                    "CSV import", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var message = $"CSV will add {DisplayText.Count(result.Folders.Count, "new folder")} and {DisplayText.Count(result.Entries.Count, "connection")} from\n\n{path}\n\n" +
                          (result.Skipped.Count > 0 ? $"{DisplayText.Count(result.Skipped.Count, "row")} will be skipped. Review the activity panel for details.\n\n" : string.Empty) +
                          "Continue?";
            if (!_confirmDialog(Application.Current?.MainWindow, message))
            {
                ReportStatus(LogLevel.Warning, "CSV import cancelled.");
                return;
            }

            // Insert folders first so that entries with ParentFolderId = folder.Id
            // don't trip the foreign-key constraint. CsvImport already guarantees
            // referenced folders are in result.Folders, but an entry could carry a
            // stray ParentFolderId pointing at a folder that was neither in this
            // import nor in the current DB (e.g. re-running a partial import) — null
            // those out defensively.
            var knownFolderIds = new HashSet<Guid>(
                _folders.GetAll().Select(f => f.Id).Concat(result.Folders.Select(f => f.Id)));
            foreach (var e in result.Entries)
                if (e.ParentFolderId is { } pid && !knownFolderIds.Contains(pid))
                    e.ParentFolderId = null;

            foreach (var f in result.Folders) _folders.Upsert(f);
            foreach (var e in result.Entries) _entries.Upsert(e);
            foreach (var (line, reason) in result.Skipped)
                AppendLog(LogLevel.Warning, $"CSV line {line} skipped: {reason}");

            Reload();
            Audit("import", "csv", null, $"Imported {result.Entries.Count} connections from {path}.");
            MirrorDatabase();
            ReportStatus(LogLevel.Success,
                $"Imported {DisplayText.Count(result.Entries.Count, "connection")} and {DisplayText.Count(result.Folders.Count, "new folder")}" +
                $"{(result.Skipped.Count > 0 ? $"; {result.Skipped.Count} skipped" : string.Empty)}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"CSV import failed: {ex.Message}");
            MessageBox.Show(Application.Current?.MainWindow!, ex.ToString(), "CSV import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Reparent(TreeNode source, FolderNode? newParent)
    {
        // Belt-and-braces: never let a folder land inside its own subtree even
        // if a caller somehow bypasses the drag/picker guards. Protects the DB
        // from cycles that would break the recursive tree walks.
        if (source is FolderNode srcFolder && newParent is not null)
        {
            for (var cursor = (FolderNode?)newParent; cursor is not null; cursor = cursor.Parent)
            {
                if (cursor.Id == srcFolder.Id)
                {
                    ReportStatus(LogLevel.Warning,
                        $"Cannot move \"{source.Name}\" inside its own subtree.");
                    return;
                }
            }
        }

        switch (source)
        {
            case FolderNode folder:
                folder.Model.ParentFolderId = newParent?.Id;
                _folders.Upsert(folder.Model);
                break;
            case EntryNode entry:
                entry.Model.ParentFolderId = newParent?.Id;
                _entries.Upsert(entry.Model);
                break;
            default:
                return;
        }
        var id = source.Id;
        Reload();
        SelectById(id);
        Audit("move", "tree-node", id, $"Moved \"{source.Name}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Info, newParent is null
            ? $"Moved \"{source.Name}\" to root."
            : $"Moved \"{source.Name}\" to \"{newParent.Name}\".");
    }

    // ---- Folder commands ----

    private void AddFolder()
    {
        var name = InputDialog.Prompt(Application.Current?.MainWindow, "Create folder", "Name the new folder:");
        if (name is null) return;
        var folder = new Folder { Name = name };
        _folders.Upsert(folder);
        Reload();
        SelectById(folder.Id);
        Audit("create", "folder", folder.Id, $"Created folder \"{folder.Name}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Created folder \"{folder.Name}\".");
    }

    private void AddSubfolder()
    {
        if (Selected is not FolderNode parent) return;
        var name = InputDialog.Prompt(Application.Current?.MainWindow, "Create subfolder", $"Name the folder inside \"{parent.Name}\":");
        if (name is null) return;
        var folder = new Folder { Name = name, ParentFolderId = parent.Id };
        _folders.Upsert(folder);
        parent.IsExpanded = true;
        Reload();
        SelectById(folder.Id);
        Audit("create", "folder", folder.Id, $"Created folder \"{folder.Name}\" inside \"{parent.Name}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Created folder \"{folder.Name}\" inside \"{parent.Name}\".");
    }

    // ---- Unified commands ----

    private void Rename()
    {
        if (Selected is null) return;
        var oldName = Selected.Name;
        var newName = InputDialog.Prompt(Application.Current?.MainWindow, "Rename", "New name:", oldName);
        if (newName is null) return;

        var renamedId = Selected.Id;
        switch (Selected)
        {
            case FolderNode f:
                f.Model.Name = newName;
                _folders.Upsert(f.Model);
                break;
            case EntryNode e:
                e.Model.Name = newName;
                _entries.Upsert(e.Model);
                break;
        }
        Reload();
        SelectById(renamedId);
        Audit("rename", "tree-node", renamedId, $"Renamed \"{oldName}\" to \"{newName}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Renamed \"{oldName}\" to \"{newName}\".");
    }

    private void Move()
    {
        if (Selected is null) return;
        var roots = RootNodes.OfType<FolderNode>().ToList();
        var excludeId = Selected is FolderNode ? Selected.Id : (Guid?)null;
        var (ok, folderId, toRoot) = FolderPickerDialog.Pick(Application.Current?.MainWindow, roots, excludeId);
        if (!ok) return;

        var newParent = toRoot ? (Guid?)null : folderId;

        switch (Selected)
        {
            case FolderNode f:
                f.Model.ParentFolderId = newParent;
                _folders.Upsert(f.Model);
                break;
            case EntryNode e:
                e.Model.ParentFolderId = newParent;
                _entries.Upsert(e.Model);
                break;
        }
        var movedId = Selected.Id;
        var movedName = Selected.Name;
        Reload();
        SelectById(movedId);
        Audit("move", "tree-node", movedId, $"Moved \"{movedName}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, newParent is null
            ? $"Moved \"{movedName}\" to the top level."
            : $"Moved \"{movedName}\" to \"{FindById(RootNodes, newParent.Value)?.Name ?? "selected folder"}\".");
    }

    private void Delete()
    {
        if (Selected is null) return;
        var deletedId = Selected.Id;
        var deletedName = Selected.Name;
        var kind = Selected is FolderNode ? "folder" : "entry";
        var suffix = Selected is FolderNode f && (f.Children.Count > 0)
            ? "\n\nNested subfolders will also be deleted. Connections inside will become unassigned and move to the top level."
            : string.Empty;

        var choice = MessageBox.Show(
            Application.Current?.MainWindow!,
            $"Delete {kind} \"{deletedName}\"?{suffix}",
            "Delete item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;

        switch (Selected)
        {
            case FolderNode folder:
                _folders.Delete(folder.Id);
                break;
            case EntryNode entry:
                _entries.Delete(entry.Id);
                break;
        }
        Reload();
        Audit("delete", kind, deletedId, $"Deleted {kind} \"{deletedName}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Warning, $"Deleted {kind} \"{deletedName}\".");
    }

    private void Launch()
    {
        if (Selected is not EntryNode entry) return;
        LaunchEntry(entry.Model, persistLastConnected: true);
    }

    public void LaunchEntryById(Guid id)
    {
        var entry = _entries.Get(id);
        if (entry is null)
        {
            ReportStatus(LogLevel.Warning, "The selected connection no longer exists.");
            return;
        }

        LaunchEntry(entry, persistLastConnected: true);
    }

    public IReadOnlyList<ConnectionEntry> GetPinnedEntries() =>
        _entries.GetAll().Where(e => e.IsPinned).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<ConnectionEntry> GetRecentEntries(int limit = 10) =>
        _entries.GetAll()
            .Where(e => e.LastConnectedUtc is not null)
            .OrderByDescending(e => e.LastConnectedUtc)
            .Take(limit)
            .ToList();

    private void TogglePin()
    {
        if (Selected is not EntryNode entry)
            return;

        entry.Model.IsPinned = !entry.Model.IsPinned;
        _entries.Upsert(entry.Model);
        entry.Refresh();
        OnPropertyChanged(nameof(SelectedPinText));
        NotifyRecentsChanged();
        Audit(entry.Model.IsPinned ? "pin" : "unpin", "connection", entry.Id,
            $"{(entry.Model.IsPinned ? "Pinned" : "Unpinned")} \"{entry.Name}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, entry.Model.IsPinned ? $"Pinned \"{entry.Name}\"." : $"Unpinned \"{entry.Name}\".");
    }

    private void LaunchEntry(ConnectionEntry source, bool persistLastConnected)
    {
        // Resolve folder-chain inheritance at launch time so folder default
        // changes propagate to every entry that defers to them.
        var effective = InheritanceResolver.Resolve(source, _foldersById);
        if (_settings.WakeOnLanBeforeLaunch && !string.IsNullOrWhiteSpace(effective.WakeMacAddress))
        {
            if (WakeOnLanService.TrySend(effective.WakeMacAddress, effective.WakeBroadcastAddress, out var wakeMessage))
                AppendLog(LogLevel.Info, wakeMessage);
            else
                AppendLog(LogLevel.Warning, wakeMessage);
        }

        if (!string.IsNullOrWhiteSpace(effective.PreLaunchScript))
            ExternalToolRunner.RunScript(effective.PreLaunchScript, effective);

        var outcome = _launcher.Launch(effective);
        if (outcome.Success)
        {
            var started = DateTimeOffset.UtcNow;
            var session = new SessionRecord
            {
                EntryId = persistLastConnected ? source.Id : null,
                EntryName = source.Name,
                TeamViewerId = source.TeamViewerId,
                ProfileName = source.ProfileName,
                Mode = effective.Mode,
                Route = outcome.Uri is not null ? "URI" : "CLI",
                ProcessId = outcome.ProcessId,
                StartedUtc = started,
                Outcome = "Started",
            };
            _sessions.Upsert(session);

            if (persistLastConnected)
            {
                _entries.TouchLastConnected(source.Id, started);
                if (Selected is EntryNode selected && selected.Id == source.Id)
                {
                    selected.Model.LastConnectedUtc = started;
                    selected.Refresh();
                }
                NotifyRecentsChanged();
            }

            Audit("launch", "connection", persistLastConnected ? source.Id : null, $"Launched \"{source.Name}\".");
            TrackSessionExit(session, effective);
            MirrorDatabase();
            ReportStatus(LogLevel.Success, outcome.Uri is not null
                ? $"Launched \"{source.Name}\" via URI handler."
                : $"Launched \"{source.Name}\" (pid {outcome.ProcessId?.ToString() ?? "?"}).");
        }
        else
        {
            ReportStatus(LogLevel.Error, $"Launch failed: {outcome.Error}");
            MessageBox.Show(
                Application.Current?.MainWindow!,
                outcome.Error ?? "Unknown error.",
                "Launch failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TrackSessionExit(SessionRecord session, ConnectionEntry entry)
    {
        if (session.ProcessId is not { } pid)
        {
            if (!string.IsNullOrWhiteSpace(entry.PostLaunchScript))
                ExternalToolRunner.RunScript(entry.PostLaunchScript, entry);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.WaitForExit();
                _sessions.Complete(session.Id, DateTimeOffset.UtcNow, $"Exited {process.ExitCode}");
            }
            catch
            {
                _sessions.Complete(session.Id, DateTimeOffset.UtcNow, "Process unavailable");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(entry.PostLaunchScript))
                    ExternalToolRunner.RunScript(entry.PostLaunchScript, entry);
            }
        });
    }

    // ---- Helpers ----

    private void SelectById(Guid id)
    {
        var node = FindById(RootNodes, id);
        if (node is null) return;
        ExpandToRoot(node);
        node.IsSelected = true;
        Selected = node;
    }

    private static ConnectionEntry MergeCloudEntry(ConnectionEntry incoming, ConnectionEntry existing)
    {
        var tags = existing.Tags
            .Concat(incoming.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConnectionEntry
        {
            Id = existing.Id,
            ParentFolderId = incoming.ParentFolderId,
            Name = ShouldAcceptCloudName(existing) ? incoming.Name : existing.Name,
            TeamViewerId = existing.TeamViewerId,
            ProfileName = existing.ProfileName,
            Password = existing.Password,
            Mode = existing.Mode,
            Quality = existing.Quality,
            AccessControl = existing.AccessControl,
            Proxy = existing.Proxy,
            TeamViewerPathOverride = existing.TeamViewerPathOverride,
            IsPinned = existing.IsPinned,
            WakeMacAddress = existing.WakeMacAddress,
            WakeBroadcastAddress = existing.WakeBroadcastAddress,
            PreLaunchScript = existing.PreLaunchScript,
            PostLaunchScript = existing.PostLaunchScript,
            Notes = string.IsNullOrWhiteSpace(existing.Notes) ? incoming.Notes : existing.Notes,
            Tags = tags,
            LastConnectedUtc = existing.LastConnectedUtc,
            CreatedUtc = existing.CreatedUtc,
            ModifiedUtc = existing.ModifiedUtc,
        };
    }

    private static bool ShouldAcceptCloudName(ConnectionEntry existing)
    {
        return string.IsNullOrWhiteSpace(existing.Name) ||
               string.Equals(existing.Name, $"TeamViewer {existing.TeamViewerId}", StringComparison.OrdinalIgnoreCase);
    }

    private static TreeNode? FindById(IEnumerable<TreeNode> nodes, Guid id)
    {
        foreach (var n in nodes)
        {
            if (n.Id == id) return n;
            if (n is FolderNode folder)
            {
                var hit = FindById(folder.Children, id);
                if (hit is not null) return hit;
            }
        }
        return null;
    }

    private static void ExpandToRoot(TreeNode node)
    {
        var cursor = node.Parent;
        while (cursor is not null)
        {
            cursor.IsExpanded = true;
            cursor = cursor.Parent;
        }
    }
}
