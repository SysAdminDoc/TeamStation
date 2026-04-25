using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using TeamStation.App.Mvvm;
using TeamStation.App.Services;
using TeamStation.App.Views;
using TeamStation.Core.Io;
using TeamStation.Core.Models;
using TeamStation.Core.Serialization;
using TeamStation.Core.Services;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

[SupportedOSPlatform("windows")]
public sealed class MainViewModel : ViewModelBase
{
    private enum BulkTagOperation
    {
        Add,
        Remove,
        Replace,
    }

    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;
    private readonly SessionRepository _sessions;
    private readonly AuditLogRepository _auditLog;
    private readonly TeamViewerLauncher _launcher;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly TeamViewerCloudSyncService _cloudSync = new();
    private readonly IDialogService _dialogs;

    private TreeNode? _selected;
    private string _status = string.Empty;
    private string _tvExePath;
    private Dictionary<Guid, Folder> _foldersById = new();
    private readonly string _startupVersion;
    private readonly string? _startupDbPath;
    private readonly object _rootsLock = new();
    private bool _isTeamViewerReady;
    private bool _isCloudSyncing;
    private Brush _statusBrush = Brushes.Transparent;
    private string _statusTag = "Ready";
    private int _folderCount;
    private int _entryCount;
    private int _visibleFolderCount;
    private int _visibleEntryCount;
    private TreeNode? _multiSelectionAnchor;

    public MainViewModel(
        EntryRepository entries,
        FolderRepository folders,
        TeamViewerLauncher launcher,
        IDialogService dialogs,
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
        _dialogs = dialogs;
        _isTeamViewerReady = !string.IsNullOrWhiteSpace(tvExePath);
        _tvExePath = tvExePath ?? "TeamViewer.exe not found — install TeamViewer before launching";
        _startupVersion = startupVersion ?? "dev";
        _startupDbPath = startupDbPath;

        LogPanel = new LogPanelViewModel();
        LogPanel.PropertyChanged += OnLogPanelPropertyChanged;
        QuickConnect = new QuickConnectViewModel(
            (entry, save) => QuickLaunch(entry, save),
            () => IsTeamViewerReady);
        QuickConnect.PropertyChanged += OnQuickConnectPropertyChanged;
        Search = new SearchViewModel(settings, settingsService);
        Search.PropertyChanged += OnSearchPropertyChanged;
        Search.SearchTextChanged += (_, _) =>
        {
            ApplyFilter();
            NotifySurfacePropertyChanges();
            OnPropertyChanged(nameof(SearchHintText));
        };
        Search.SavedSearchApplied += s => ReportStatus(LogLevel.Info, $"Applied saved search \"{s}\".");
        Search.SavedSearchAdded += s =>
        {
            Audit("create", "saved-search", null, $"Saved search \"{s}\".");
            ReportStatus(LogLevel.Success, $"Saved search \"{s}\".");
        };

        AddEntryCommand = new RelayCommand(AddEntry);
        AddFolderCommand = new RelayCommand(AddFolder);
        AddSubfolderCommand = new RelayCommand(AddSubfolder, () => Selected is FolderNode);
        RenameCommand = new RelayCommand(Rename, () => Selected is not null);
        MoveCommand = new RelayCommand(Move, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        EditCommand = new RelayCommand(EditSelected, () => Selected is not null);
        LaunchCommand = new RelayCommand(Launch, () => Selected is EntryNode && IsTeamViewerReady);
        LaunchProtocolCommand = new RelayCommand(LaunchViaProtocol, () => Selected is EntryNode);
        OpenTeamViewerWebClientCommand = new RelayCommand(OpenTeamViewerWebClient, () => Selected is EntryNode);
        CopySelectedIdCommand = new RelayCommand(CopySelectedId, () => Selected is EntryNode);
        DuplicateCommand = new RelayCommand(DuplicateSelectedEntry, () => Selected is EntryNode);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ImportCsvCommand = new RelayCommand(ImportCsvFile);
        TogglePinCommand = new RelayCommand(TogglePin, () => Selected is EntryNode);
        BulkCopyIdsCommand = new RelayCommand(BulkCopyIds, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkMoveCommand = new RelayCommand(BulkMove, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkDeleteCommand = new RelayCommand(BulkDelete, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkPinCommand = new RelayCommand(() => BulkSetPinned(true), () => SelectedNodes.OfType<EntryNode>().Any());
        BulkUnpinCommand = new RelayCommand(() => BulkSetPinned(false), () => SelectedNodes.OfType<EntryNode>().Any());
        BulkAddTagCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Add), () => SelectedNodes.OfType<EntryNode>().Any());
        BulkRemoveTagCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Remove), () => SelectedNodes.OfType<EntryNode>().Any());
        BulkReplaceTagsCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Replace), () => SelectedNodes.OfType<EntryNode>().Any());
        BulkSetModeCommand = new RelayCommand(BulkSetMode, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkSetQualityCommand = new RelayCommand(BulkSetQuality, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkSetAccessControlCommand = new RelayCommand(BulkSetAccessControl, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkSetProxyCommand = new RelayCommand(BulkSetProxy, () => SelectedNodes.OfType<EntryNode>().Any());
        BulkClearProxyCommand = new RelayCommand(BulkClearProxy, () => SelectedNodes.OfType<EntryNode>().Any());
        ClearMultiSelectionCommand = new RelayCommand(ClearMultiSelection, () => IsBulkSelectionActive);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        OpenDatabaseFolderCommand = new RelayCommand(OpenDatabaseFolder, () => CanOpenDatabaseFolder);
        ImportTeamViewerHistoryCommand = new RelayCommand(ImportTeamViewerHistory);
        SyncTeamViewerCloudCommand = new RelayCommand(() => _ = SyncTeamViewerCloudAsync(), () => CanSyncTeamViewerCloud);
        ExportSessionsCommand = new RelayCommand(ExportSessions, () => CanExportSessions);
        RunExternalToolCommand = new RelayCommand(RunExternalTool,
            parameter => Selected is EntryNode && parameter is ExternalToolDefinition);

        System.Windows.Data.BindingOperations.EnableCollectionSynchronization(RootNodes, _rootsLock);

        Reload();
        RefreshTeamViewerVersion();
        LogPanel.Append(LogLevel.Info, $"TeamStation v{_startupVersion} started.");
        if (!string.IsNullOrEmpty(_startupDbPath))
            LogPanel.Append(LogLevel.Info, $"Database: {_startupDbPath}");
        LogPanel.Append(LogLevel.Info, tvExePath is null
            ? "TeamViewer.exe not found — launches will be disabled until TeamViewer is installed."
            : $"TeamViewer.exe: {tvExePath}");
        if (TeamViewerNeedsUpdate)
            LogPanel.Append(LogLevel.Warning,
                $"{TeamViewerClientVersion} is below the recommended baseline ({TeamViewerVersionDetector.MinimumSafeVersion}). " +
                "CVE-2026-23572 (auth-bypass) is fixed in TeamViewer 15.74.5+ — update the installed client when convenient.");
        if (!string.IsNullOrEmpty(_settingsService.LastLoadError))
            LogPanel.Append(LogLevel.Warning, _settingsService.LastLoadError!);
        PruneHistory();
    }

    private void PruneHistory()
    {
        var days = _settings.HistoryRetentionDays;
        if (days <= 0) return;
        var retention = TimeSpan.FromDays(days);
        try
        {
            var removedSessions = _sessions.Prune(retention);
            var removedAudit = _auditLog.Prune(retention);
            if (removedSessions + removedAudit > 0)
            {
                LogPanel.Append(LogLevel.Info,
                    $"Pruned history older than {days} days: {removedSessions} session{(removedSessions == 1 ? string.Empty : "s")}, " +
                    $"{removedAudit} audit event{(removedAudit == 1 ? string.Empty : "s")}.");
            }
        }
        catch (Exception ex)
        {
            LogPanel.Append(LogLevel.Warning, $"History prune skipped: {ex.Message}");
        }
    }

    public event EventHandler? TrayMenuInvalidated;

    public ObservableCollection<TreeNode> RootNodes { get; } = new();

    // Sub-view-models — exposed for future XAML bindings that want the clean shape.
    public LogPanelViewModel LogPanel { get; }
    public QuickConnectViewModel QuickConnect { get; }
    public SearchViewModel Search { get; }

    // ---- Legacy surface proxies (keep XAML binding paths stable) ----

    public ObservableCollection<LogEntry> Log => LogPanel.Entries;
    public bool IsLogVisible { get => LogPanel.IsVisible; set => LogPanel.IsVisible = value; }
    public string LogSummary => LogPanel.Summary;
    public string ActivityButtonText => LogPanel.ButtonText;
    public bool LogHasEntries => LogPanel.HasEntries;
    public bool ShowLogEmptyState => !LogHasEntries;
    public string LogClearTooltip => LogPanel.ClearTooltip;
    public System.Windows.Input.ICommand ClearLogCommand => LogPanel.ClearCommand;
    public System.Windows.Input.ICommand ToggleLogCommand => LogPanel.ToggleCommand;

    public string QuickName { get => QuickConnect.Name; set => QuickConnect.Name = value; }
    public string QuickTeamViewerId { get => QuickConnect.TeamViewerId; set => QuickConnect.TeamViewerId = value; }
    public string QuickPassword { get => QuickConnect.Password; set => QuickConnect.Password = value; }
    public bool QuickSaveConnection { get => QuickConnect.SaveConnection; set => QuickConnect.SaveConnection = value; }
    public bool QuickHasTeamViewerId => QuickConnect.HasTeamViewerId;
    public string QuickConnectTooltip => QuickConnect.ConnectTooltip;
    public System.Windows.Input.ICommand QuickConnectCommand => QuickConnect.ConnectCommand;

    public string SearchText { get => Search.SearchText; set => Search.SearchText = value; }
    public bool HasSearchText => Search.HasText;
    public string SaveSearchTooltip => Search.SaveTooltip;
    public string ClearSearchTooltip => Search.ClearTooltip;
    public bool CanSaveCurrentSearch => Search.CanSaveCurrent;
    public IReadOnlyList<string> SavedSearches => Search.SavedSearches;
    public bool HasSavedSearches => Search.HasSavedSearches;

    // Rebroadcast sub-VM property changes under the legacy MainViewModel names
    // so XAML bindings like {Binding LogSummary} keep updating after the
    // v0.2.0 sub-VM split. Keep these maps synced with the proxy properties
    // above whenever a new proxy is added.
    private void OnLogPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LogPanelViewModel.Entries): OnPropertyChanged(nameof(Log)); break;
            case nameof(LogPanelViewModel.IsVisible): OnPropertyChanged(nameof(IsLogVisible)); break;
            case nameof(LogPanelViewModel.Summary): OnPropertyChanged(nameof(LogSummary)); break;
            case nameof(LogPanelViewModel.ButtonText): OnPropertyChanged(nameof(ActivityButtonText)); break;
            case nameof(LogPanelViewModel.HasEntries):
                OnPropertyChanged(nameof(LogHasEntries));
                OnPropertyChanged(nameof(ShowLogEmptyState));
                break;
            case nameof(LogPanelViewModel.ClearTooltip): OnPropertyChanged(nameof(LogClearTooltip)); break;
        }
    }

    private void OnQuickConnectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(QuickConnectViewModel.Name): OnPropertyChanged(nameof(QuickName)); break;
            case nameof(QuickConnectViewModel.TeamViewerId): OnPropertyChanged(nameof(QuickTeamViewerId)); break;
            case nameof(QuickConnectViewModel.HasTeamViewerId): OnPropertyChanged(nameof(QuickHasTeamViewerId)); break;
            case nameof(QuickConnectViewModel.ConnectTooltip): OnPropertyChanged(nameof(QuickConnectTooltip)); break;
            case nameof(QuickConnectViewModel.Password): OnPropertyChanged(nameof(QuickPassword)); break;
            case nameof(QuickConnectViewModel.SaveConnection): OnPropertyChanged(nameof(QuickSaveConnection)); break;
        }
    }

    private void OnSearchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SearchViewModel.SearchText):
                OnPropertyChanged(nameof(SearchText));
                break;
            case nameof(SearchViewModel.HasText):
                OnPropertyChanged(nameof(HasSearchText));
                break;
            case nameof(SearchViewModel.SaveTooltip):
                OnPropertyChanged(nameof(SaveSearchTooltip));
                break;
            case nameof(SearchViewModel.ClearTooltip):
                OnPropertyChanged(nameof(ClearSearchTooltip));
                break;
            case nameof(SearchViewModel.CanSaveCurrent):
                OnPropertyChanged(nameof(CanSaveCurrentSearch));
                break;
            case nameof(SearchViewModel.SavedSearches):
                OnPropertyChanged(nameof(SavedSearches));
                break;
            case nameof(SearchViewModel.HasSavedSearches):
                OnPropertyChanged(nameof(HasSavedSearches));
                break;
        }
    }
    public System.Windows.Input.ICommand SaveSearchCommand => Search.SaveCommand;
    public System.Windows.Input.ICommand ApplySavedSearchCommand => Search.ApplyCommand;
    public System.Windows.Input.ICommand ClearSearchCommand => Search.ClearCommand;

    public TreeNode? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                foreach (var cmd in new[] { AddSubfolderCommand, RenameCommand, MoveCommand, DeleteCommand, EditCommand, LaunchCommand, LaunchProtocolCommand, OpenTeamViewerWebClientCommand, CopySelectedIdCommand, DuplicateCommand, TogglePinCommand, RunExternalToolCommand })
                    ((RelayCommand)cmd).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedIsEntry));
                OnPropertyChanged(nameof(SelectedIsFolder));
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowSelectionPlaceholder));
                OnPropertyChanged(nameof(SelectedPinText));
                OnPropertyChanged(nameof(LaunchSelectedTooltip));
                OnPropertyChanged(nameof(LaunchProtocolSelectedTooltip));
                OnPropertyChanged(nameof(OpenTeamViewerWebClientTooltip));
                OnPropertyChanged(nameof(EditSelectionTooltip));
                OnPropertyChanged(nameof(CopySelectedIdTooltip));
                OnPropertyChanged(nameof(DuplicateSelectionTooltip));
                OnPropertyChanged(nameof(MoveSelectionTooltip));
                OnPropertyChanged(nameof(DeleteSelectionTooltip));
                OnPropertyChanged(nameof(PinSelectionTooltip));
            }
        }
    }

    public bool SelectedIsEntry => Selected is EntryNode;
    public bool SelectedIsFolder => Selected is FolderNode;
    public bool HasSelection => Selected is not null;
    public bool ShowSelectionPlaceholder => Selected is null;
    public string SelectedPinText => Selected is EntryNode { Model.IsPinned: true } ? "Unpin" : "Pin";
    public string LaunchSelectedTooltip => Selected is EntryNode
        ? (IsTeamViewerReady ? "Launch the selected connection." : "Install or configure TeamViewer before launching.")
        : "Select a connection to launch.";
    public string LaunchProtocolSelectedTooltip => Selected is EntryNode
        ? "Launch through the registered TeamViewer protocol handler, bypassing TeamViewer.exe command-line flags."
        : "Select a connection to launch through the TeamViewer protocol handler.";
    public string OpenTeamViewerWebClientTooltip => Selected is EntryNode
        ? "Open TeamViewer Web Client in your browser and copy the selected TeamViewer ID."
        : "Select a connection to open in TeamViewer Web Client.";
    public string EditSelectionTooltip => HasSelection ? "Edit the selected item." : "Select an item to edit.";
    public string CopySelectedIdTooltip => Selected is EntryNode
        ? "Copy the selected TeamViewer ID to the clipboard."
        : "Select a connection to copy its TeamViewer ID.";
    public string DuplicateSelectionTooltip => Selected is EntryNode
        ? "Duplicate the selected connection as an editable copy."
        : "Select a connection to duplicate.";
    public string MoveSelectionTooltip => HasSelection ? "Move the selected item to another folder." : "Select an item to move.";
    public string DeleteSelectionTooltip => HasSelection ? "Delete the selected item." : "Select an item to delete.";
    public string PinSelectionTooltip => Selected is EntryNode
        ? $"{SelectedPinText} the selected connection."
        : "Select a connection to pin.";

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
                OnPropertyChanged(nameof(LaunchSelectedTooltip));
                ((RelayCommand)LaunchCommand).RaiseCanExecuteChanged();
                QuickConnect.RaiseCanLaunchChanged();
            }
        }
    }
    public string TeamViewerStatusText => _isTeamViewerReady ? "TeamViewer ready" : "Install TeamViewer";
    public bool IsCloudSyncing
    {
        get => _isCloudSyncing;
        private set
        {
            if (SetField(ref _isCloudSyncing, value))
            {
                OnPropertyChanged(nameof(CanSyncTeamViewerCloud));
                OnPropertyChanged(nameof(CloudSyncStatusText));
                OnPropertyChanged(nameof(CloudSyncButtonText));
                OnPropertyChanged(nameof(CloudSyncToneBrush));
                ((RelayCommand)SyncTeamViewerCloudCommand).RaiseCanExecuteChanged();
            }
        }
    }
    public bool HasTeamViewerApiToken => !string.IsNullOrWhiteSpace(_settings.TeamViewerApiToken);
    public bool CanSyncTeamViewerCloud => HasTeamViewerApiToken && !IsCloudSyncing;
    public string CloudSyncButtonText => IsCloudSyncing ? "Syncing" : "Sync cloud";
    public string CloudSyncStatusText => IsCloudSyncing
        ? "Cloud sync in progress"
        : HasTeamViewerApiToken
            ? "Cloud sync ready"
            : "Cloud sync not configured";
    public Brush CloudSyncToneBrush => IsCloudSyncing
        ? TryBrush("BlueBrush", Brushes.LightSkyBlue)
        : HasTeamViewerApiToken
            ? TryBrush("GreenBrush", Brushes.LightGreen)
            : TryBrush("Overlay0Brush", Brushes.Gray);
    public string DatabasePathDisplay => _startupDbPath ?? "Database path unavailable";
    public string DatabaseLocationDisplay => TryResolveDatabaseFolder(out var folder)
        ? folder
        : _startupDbPath is null
            ? "Portable mode"
            : _startupDbPath;
    public int FolderCount { get => _folderCount; private set => SetField(ref _folderCount, value); }
    public int EntryCount { get => _entryCount; private set => SetField(ref _entryCount, value); }
    public int VisibleFolderCount { get => _visibleFolderCount; private set => SetField(ref _visibleFolderCount, value); }
    public int VisibleEntryCount { get => _visibleEntryCount; private set => SetField(ref _visibleEntryCount, value); }
    public bool HasAnyItems => FolderCount + EntryCount > 0;
    public bool ShowWelcomeState => !HasAnyItems;
    public bool ShowNoSearchResultsState => HasAnyItems && HasSearchText && VisibleFolderCount + VisibleEntryCount == 0;
    public string TreeSummary => HasSearchText
        ? $"{DisplayText.Count(VisibleEntryCount, "matching connection")}, {DisplayText.Count(VisibleFolderCount, "visible folder")}"
        : $"{DisplayText.Count(EntryCount, "connection")}, {DisplayText.Count(FolderCount, "folder")}";
    public string SearchHintText => HasSearchText
        ? $"Filtering names, IDs, notes, and tags for \"{SearchText.Trim()}\"."
        : "Search names, TeamViewer IDs, notes, or tags. Double-click a connection to launch it.";
    public IReadOnlyList<ExternalToolDefinition> ExternalTools => _settings.ExternalTools;
    public bool HasExternalTools => ExternalTools.Count > 0;

    private void AppendLog(LogLevel level, string message)
    {
        LogPanel.Append(level, message);
        OnPropertyChanged(nameof(LogSummary));
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

    private string _teamViewerClientVersion = "TeamViewer not detected";
    private bool _teamViewerNeedsUpdate;

    /// <summary>
    /// Display string for the status-bar TeamViewer chip — "TeamViewer 15.71.5"
    /// when detected, "TeamViewer not detected" otherwise. Refreshed on
    /// startup and after Settings save (the resolved exe may change when
    /// the operator overrides the TeamViewer path).
    /// </summary>
    public string TeamViewerClientVersion
    {
        get => _teamViewerClientVersion;
        private set => SetField(ref _teamViewerClientVersion, value);
    }

    /// <summary>
    /// True when the detected client is below
    /// <see cref="TeamViewerVersionDetector.MinimumSafeVersion"/>
    /// (15.74.5 — CVE-2026-23572 baseline). The status bar surfaces an
    /// "Update available" pill when this is set.
    /// </summary>
    public bool TeamViewerNeedsUpdate
    {
        get => _teamViewerNeedsUpdate;
        private set => SetField(ref _teamViewerNeedsUpdate, value);
    }

    private void RefreshTeamViewerVersion()
    {
        var detected = TeamViewerVersionDetector.Detect();
        TeamViewerClientVersion = detected is null
            ? "TeamViewer not detected"
            : $"TeamViewer {detected}";
        TeamViewerNeedsUpdate = TeamViewerVersionDetector.NeedsUpdate(detected);
    }

    public System.Windows.Input.ICommand AddEntryCommand { get; }
    public System.Windows.Input.ICommand AddFolderCommand { get; }
    public System.Windows.Input.ICommand AddSubfolderCommand { get; }
    public System.Windows.Input.ICommand RenameCommand { get; }
    public System.Windows.Input.ICommand MoveCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand EditCommand { get; }
    public System.Windows.Input.ICommand LaunchCommand { get; }
    public System.Windows.Input.ICommand LaunchProtocolCommand { get; }
    public System.Windows.Input.ICommand OpenTeamViewerWebClientCommand { get; }
    public System.Windows.Input.ICommand CopySelectedIdCommand { get; }
    public System.Windows.Input.ICommand DuplicateCommand { get; }
    public System.Windows.Input.ICommand BulkCopyIdsCommand { get; }
    public System.Windows.Input.ICommand BulkMoveCommand { get; }
    public System.Windows.Input.ICommand BulkDeleteCommand { get; }
    public System.Windows.Input.ICommand BulkPinCommand { get; }
    public System.Windows.Input.ICommand BulkUnpinCommand { get; }
    public System.Windows.Input.ICommand BulkAddTagCommand { get; }
    public System.Windows.Input.ICommand BulkRemoveTagCommand { get; }
    public System.Windows.Input.ICommand BulkReplaceTagsCommand { get; }
    public System.Windows.Input.ICommand BulkSetModeCommand { get; }
    public System.Windows.Input.ICommand BulkSetQualityCommand { get; }
    public System.Windows.Input.ICommand BulkSetAccessControlCommand { get; }
    public System.Windows.Input.ICommand BulkSetProxyCommand { get; }
    public System.Windows.Input.ICommand BulkClearProxyCommand { get; }
    public System.Windows.Input.ICommand ClearMultiSelectionCommand { get; }

    /// <summary>
    /// v0.3.5: nodes with <see cref="TreeNode.IsMultiSelected"/> set,
    /// flattened from <see cref="RootNodes"/>. When the multi-selection is
    /// empty, callers should fall back to <see cref="Selected"/> (single-
    /// select semantics). The status-bar / context-menu count comes from
    /// <see cref="MultiSelectedEntryCount"/>.
    /// </summary>
    public IReadOnlyList<TreeNode> SelectedNodes
    {
        get
        {
            var result = new List<TreeNode>();
            foreach (var root in RootNodes) Collect(root, result);
            return result;

            static void Collect(TreeNode node, List<TreeNode> sink)
            {
                if (node.IsMultiSelected) sink.Add(node);
                if (node is FolderNode folder)
                {
                    foreach (var child in folder.Children) Collect(child, sink);
                }
            }
        }
    }

    public int MultiSelectedEntryCount => SelectedNodes.OfType<EntryNode>().Count();
    public bool IsBulkSelectionActive => MultiSelectedEntryCount >= 2;
    public string BulkCopyIdsSelectionLabel => $"Copy IDs from selection ({MultiSelectedEntryCount})";
    public string BulkMoveSelectionLabel => $"Move selection ({MultiSelectedEntryCount})";
    public string BulkDeleteSelectionLabel => $"Delete selection ({MultiSelectedEntryCount})";
    public string BulkPinSelectionLabel => $"Pin selection ({MultiSelectedEntryCount})";
    public string BulkUnpinSelectionLabel => $"Unpin selection ({MultiSelectedEntryCount})";
    public string BulkAddTagSelectionLabel => $"Add tag to selection ({MultiSelectedEntryCount})";
    public string BulkRemoveTagSelectionLabel => $"Remove tag from selection ({MultiSelectedEntryCount})";
    public string BulkReplaceTagsSelectionLabel => $"Replace tags on selection ({MultiSelectedEntryCount})";
    public string BulkSetModeSelectionLabel => $"Set mode on selection ({MultiSelectedEntryCount})";
    public string BulkSetQualitySelectionLabel => $"Set quality on selection ({MultiSelectedEntryCount})";
    public string BulkSetAccessControlSelectionLabel => $"Set access on selection ({MultiSelectedEntryCount})";
    public string BulkSetProxySelectionLabel => $"Set proxy on selection ({MultiSelectedEntryCount})";
    public string BulkClearProxySelectionLabel => $"Clear proxy on selection ({MultiSelectedEntryCount})";
    public string MultiSelectionSummary => $"{DisplayText.Count(MultiSelectedEntryCount, "connection")} selected";

    /// <summary>
    /// Remembers the node range selection should start from.
    /// </summary>
    public void SetMultiSelectionAnchor(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _multiSelectionAnchor = node;
    }

    /// <summary>
    /// Toggles <see cref="TreeNode.IsMultiSelected"/> on <paramref name="node"/>.
    /// Called by the Ctrl-click handler in MainWindow.xaml.cs. Refreshes the
    /// dependent properties and CanExecute state on the bulk commands.
    /// </summary>
    public void ToggleMultiSelection(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _multiSelectionAnchor = node;
        node.IsMultiSelected = !node.IsMultiSelected;
        RaiseBulkSelectionChanged();
    }

    /// <summary>
    /// Selects a contiguous range in the visible tree order. Collapsed and
    /// filtered-out descendants are intentionally skipped so Shift-click
    /// matches the rows the user can actually see.
    /// </summary>
    public void SelectMultiSelectionRange(TreeNode target, bool append = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        var displayNodes = DisplayOrderedNodes();
        var targetIndex = displayNodes.IndexOf(target);
        if (targetIndex < 0)
        {
            if (!append)
                ClearMultiSelectionCore();

            target.IsMultiSelected = true;
            _multiSelectionAnchor = target;
            RaiseBulkSelectionChanged();
            return;
        }

        var anchorIndex = _multiSelectionAnchor is not null
            ? displayNodes.IndexOf(_multiSelectionAnchor)
            : -1;
        if (anchorIndex < 0 && Selected is not null)
            anchorIndex = displayNodes.IndexOf(Selected);
        if (anchorIndex < 0)
            anchorIndex = targetIndex;

        if (!append)
            ClearMultiSelectionCore();

        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        for (var i = start; i <= end; i++)
            displayNodes[i].IsMultiSelected = true;

        _multiSelectionAnchor = displayNodes[anchorIndex];
        RaiseBulkSelectionChanged();
    }

    /// <summary>
    /// Clears <see cref="TreeNode.IsMultiSelected"/> on every node in the
    /// tree. Called on plain (non-Ctrl) click and after Reload so stale
    /// selection state can't survive a refresh.
    /// </summary>
    public void ClearMultiSelection()
    {
        _multiSelectionAnchor = null;
        ClearMultiSelectionCore();
        RaiseBulkSelectionChanged();
    }

    private void ClearMultiSelectionCore()
    {
        foreach (var root in RootNodes) Clear(root);

        static void Clear(TreeNode node)
        {
            node.IsMultiSelected = false;
            if (node is FolderNode folder)
                foreach (var child in folder.Children) Clear(child);
        }
    }

    private List<TreeNode> DisplayOrderedNodes()
    {
        var nodes = new List<TreeNode>();
        foreach (var root in RootNodes) CollectDisplayNodes(root, nodes);
        return nodes;
    }

    private static void CollectDisplayNodes(TreeNode node, List<TreeNode> sink)
    {
        if (!node.IsVisible)
            return;

        sink.Add(node);
        if (node is FolderNode { IsExpanded: true } folder)
            foreach (var child in folder.Children) CollectDisplayNodes(child, sink);
    }

    private void RaiseBulkSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNodes));
        OnPropertyChanged(nameof(MultiSelectedEntryCount));
        OnPropertyChanged(nameof(IsBulkSelectionActive));
        OnPropertyChanged(nameof(BulkCopyIdsSelectionLabel));
        OnPropertyChanged(nameof(BulkMoveSelectionLabel));
        OnPropertyChanged(nameof(BulkDeleteSelectionLabel));
        OnPropertyChanged(nameof(BulkPinSelectionLabel));
        OnPropertyChanged(nameof(BulkUnpinSelectionLabel));
        OnPropertyChanged(nameof(BulkAddTagSelectionLabel));
        OnPropertyChanged(nameof(BulkRemoveTagSelectionLabel));
        OnPropertyChanged(nameof(BulkReplaceTagsSelectionLabel));
        OnPropertyChanged(nameof(BulkSetModeSelectionLabel));
        OnPropertyChanged(nameof(BulkSetQualitySelectionLabel));
        OnPropertyChanged(nameof(BulkSetAccessControlSelectionLabel));
        OnPropertyChanged(nameof(BulkSetProxySelectionLabel));
        OnPropertyChanged(nameof(BulkClearProxySelectionLabel));
        OnPropertyChanged(nameof(MultiSelectionSummary));
        ((RelayCommand)BulkCopyIdsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkMoveCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkDeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkPinCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkUnpinCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkAddTagCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkRemoveTagCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkReplaceTagsCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkSetModeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkSetQualityCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkSetAccessControlCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkSetProxyCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BulkClearProxyCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearMultiSelectionCommand).RaiseCanExecuteChanged();
    }

    private void BulkCopyIds()
    {
        var ids = SelectedNodes.OfType<EntryNode>()
            .Select(entry => entry.TeamViewerId.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            ReportStatus(LogLevel.Warning, "Selected connections do not have TeamViewer IDs to copy.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(string.Join(Environment.NewLine, ids), copy: true);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, $"Bulk TeamViewer ID copy failed: {ex.Message}");
            ReportStatus(LogLevel.Error, $"Could not copy TeamViewer IDs: {ex.Message}");
            return;
        }

        var countText = DisplayText.Count(ids.Count, "TeamViewer ID");
        Audit("bulk_copy_ids", "connection", null, $"Copied {countText} to clipboard via bulk action.");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Copied {countText} to the clipboard.");
    }

    private void CopySelectedId()
    {
        if (Selected is not EntryNode entry)
            return;

        var id = entry.TeamViewerId.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            ReportStatus(LogLevel.Warning, "Selected connection does not have a TeamViewer ID to copy.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(id, copy: true);
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, $"TeamViewer ID copy failed: {ex.Message}");
            ReportStatus(LogLevel.Error, $"Could not copy TeamViewer ID: {ex.Message}");
            return;
        }

        Audit("copy_id", "connection", entry.Id, $"Copied TeamViewer ID for \"{entry.Name}\".");
        MirrorDatabase();
        ReportStatus(LogLevel.Success, "Copied TeamViewer ID to the clipboard.");
    }

    private void BulkMove()
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var roots = RootNodes.OfType<FolderNode>().ToList();
        var (ok, folderId, toRoot) = FolderPickerDialog.Pick(Application.Current?.MainWindow, roots);
        if (!ok)
        {
            ReportStatus(LogLevel.Warning, "Bulk move cancelled.");
            return;
        }

        if (!toRoot && folderId is null)
        {
            ReportStatus(LogLevel.Warning, "Choose a destination folder before moving the selection.");
            return;
        }

        var newParent = toRoot ? (Guid?)null : folderId;
        var destinationName = newParent is null
            ? "the top level"
            : $"\"{FindById(RootNodes, newParent.Value)?.Name ?? "selected folder"}\"";
        var destinationPreposition = newParent is null ? "at" : "in";
        var changed = 0;
        Guid? firstMovedId = null;

        foreach (var entryNode in entries)
        {
            if (entryNode.Model.ParentFolderId == newParent)
                continue;

            firstMovedId ??= entryNode.Id;
            entryNode.Model.ParentFolderId = newParent;
            _entries.Upsert(entryNode.Model);
            changed++;
        }

        if (changed == 0)
        {
            ReportStatus(LogLevel.Info, $"Selected connections are already {destinationPreposition} {destinationName}.");
            return;
        }

        var countText = DisplayText.Count(changed, "connection");
        Reload();
        if (firstMovedId is { } id)
            SelectById(id);
        Audit("bulk_move", "connection", null, $"Moved {countText} to {destinationName} via bulk action.", newParent?.ToString());
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Moved {countText} to {destinationName}.");
    }

    private void BulkDelete()
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var countText = DisplayText.Count(entries.Count, "connection");
        var preview = FormatBulkDeletePreview(entries);
        if (!_dialogs.Confirm(
                Application.Current?.MainWindow,
                $"Delete {countText}?\n\n{preview}This cannot be undone from TeamStation. Session history and audit events are kept.",
                "Delete selected connections",
                "Delete selection",
                isDestructive: true))
        {
            ReportStatus(LogLevel.Warning, "Bulk delete cancelled.");
            return;
        }

        foreach (var entryNode in entries)
            _entries.Delete(entryNode.Id);

        Reload();
        Audit(
            "bulk_delete",
            "connection",
            null,
            $"Deleted {countText} via bulk action.",
            string.Join(", ", entries.Select(entry => entry.Name)));
        MirrorDatabase();
        ReportStatus(LogLevel.Warning, $"Deleted {countText}.");
    }

    private static string FormatBulkDeletePreview(IReadOnlyList<EntryNode> entries)
    {
        var names = entries
            .Select(entry => entry.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(5)
            .ToList();

        if (names.Count == 0)
            return string.Empty;

        var preview = "Connections:\n" + string.Join('\n', names.Select(name => $"- {name}"));
        if (entries.Count > names.Count)
            preview += $"\n- ...and {DisplayText.Count(entries.Count - names.Count, "more connection")}";

        return preview + "\n\n";
    }

    private void BulkSetPinned(bool pinned)
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var changed = 0;
        foreach (var entryNode in entries)
        {
            if (entryNode.Model.IsPinned == pinned) continue;
            entryNode.Model.IsPinned = pinned;
            _entries.Upsert(entryNode.Model);
            entryNode.Refresh();
            changed++;
        }

        if (changed == 0)
        {
            ReportStatus(LogLevel.Info, pinned
                ? "All selected connections were already pinned."
                : "All selected connections were already unpinned.");
            return;
        }

        OnPropertyChanged(nameof(SelectedPinText));
        NotifyRecentsChanged();
        Audit(pinned ? "bulk_pin" : "bulk_unpin", "connection", null,
            $"{(pinned ? "Pinned" : "Unpinned")} {changed} connection{(changed == 1 ? string.Empty : "s")} via bulk action.");
        MirrorDatabase();
        ReportStatus(LogLevel.Success,
            $"{(pinned ? "Pinned" : "Unpinned")} {changed} connection{(changed == 1 ? string.Empty : "s")}.");
    }

    private void BulkEditTags(BulkTagOperation operation)
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var (title, prompt, actionVerb, auditAction) = operation switch
        {
            BulkTagOperation.Add => (
                "Add tag to selection",
                "Enter one or more tags to add. Separate multiple tags with commas.",
                "Added",
                "bulk_add_tag"),
            BulkTagOperation.Remove => (
                "Remove tag from selection",
                "Enter one or more tags to remove. Separate multiple tags with commas.",
                "Removed",
                "bulk_remove_tag"),
            _ => (
                "Replace tags on selection",
                "Enter the complete tag list for the selected connections. Separate tags with commas.",
                "Replaced",
                "bulk_replace_tags"),
        };

        var input = InputDialog.Prompt(
            Application.Current?.MainWindow,
            title,
            prompt,
            validationMessage: "Enter at least one tag before applying.");
        if (input is null)
        {
            ReportStatus(LogLevel.Warning, "Bulk tag update cancelled.");
            return;
        }

        var requestedTags = ParseBulkTags(input);
        if (requestedTags.Count == 0)
        {
            ReportStatus(LogLevel.Warning, "No valid tags were entered.");
            return;
        }

        var changed = 0;
        foreach (var entryNode in entries)
        {
            var updated = operation switch
            {
                BulkTagOperation.Add => MergeTags(entryNode.Model.Tags, requestedTags),
                BulkTagOperation.Remove => RemoveTags(entryNode.Model.Tags, requestedTags),
                _ => requestedTags.ToList(),
            };

            if (TagsEqual(entryNode.Model.Tags, updated))
                continue;

            entryNode.Model.Tags = updated;
            entryNode.Model.ModifiedUtc = DateTimeOffset.UtcNow;
            _entries.Upsert(entryNode.Model);
            entryNode.Refresh();
            changed++;
        }

        if (changed == 0)
        {
            ReportStatus(LogLevel.Info, "Selected connections already matched that tag update.");
            return;
        }

        ApplyFilter();
        NotifySurfacePropertyChanges();
        Audit(auditAction, "connection", null,
            $"{actionVerb} {DisplayText.Count(requestedTags.Count, "tag")} on {DisplayText.Count(changed, "connection")} via bulk action.",
            string.Join(", ", requestedTags));
        MirrorDatabase();
        ReportStatus(LogLevel.Success,
            $"{actionVerb} {DisplayText.Count(requestedTags.Count, "tag")} on {DisplayText.Count(changed, "connection")}.");
    }

    private static List<string> ParseBulkTags(string input) =>
        input.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Trim().TrimStart('#'))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> MergeTags(IEnumerable<string> existing, IEnumerable<string> additions) =>
        existing.Concat(additions)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> RemoveTags(IEnumerable<string> existing, IReadOnlyCollection<string> removals)
    {
        var removeSet = removals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return existing
            .Where(tag => !removeSet.Contains(tag))
            .ToList();
    }

    private static bool TagsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

    private void BulkSetMode() => BulkSetLaunchOption<ConnectionMode>(
        "Set mode on selection",
        "Choose the TeamViewer launch mode to apply to every selected connection.",
        "Selected connections have mixed modes. Choose the value to apply before continuing.",
        CreateModeOptions(),
        entry => entry.Model.Mode,
        (entry, value) => entry.Mode = value,
        value => DisplayText.ModeLabel(value),
        "mode",
        "bulk_set_mode");

    private void BulkSetQuality() => BulkSetLaunchOption<ConnectionQuality>(
        "Set quality on selection",
        "Choose the TeamViewer quality preference to apply to every selected connection.",
        "Selected connections have mixed quality preferences. Choose the value to apply before continuing.",
        CreateQualityOptions(),
        entry => entry.Model.Quality,
        (entry, value) => entry.Quality = value,
        value => DisplayText.QualityLabel(value),
        "quality",
        "bulk_set_quality");

    private void BulkSetAccessControl() => BulkSetLaunchOption<AccessControl>(
        "Set access on selection",
        "Choose the access-control behavior to apply to every selected connection.",
        "Selected connections have mixed access-control settings. Choose the value to apply before continuing.",
        CreateAccessControlOptions(),
        entry => entry.Model.AccessControl,
        (entry, value) => entry.AccessControl = value,
        value => DisplayText.AccessLabel(value),
        "access control",
        "bulk_set_access_control");

    private void BulkSetLaunchOption<T>(
        string title,
        string prompt,
        string noSelectionText,
        IReadOnlyList<ChoiceDialogOption> options,
        Func<EntryNode, T?> getValue,
        Action<ConnectionEntry, T?> setValue,
        Func<T?, string> labelForValue,
        string settingName,
        string auditAction)
        where T : struct
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var hasInitialValue = TryGetCommonLaunchValue(entries, getValue, out var initialValue);
        if (!ChoiceDialog.Pick(
                Application.Current?.MainWindow,
                title,
                prompt,
                noSelectionText,
                options,
                hasInitialValue,
                initialValue,
                out T? selectedValue))
        {
            ReportStatus(LogLevel.Warning, $"Bulk {settingName} update cancelled.");
            return;
        }

        var changed = 0;
        foreach (var entryNode in entries)
        {
            if (EqualityComparer<T?>.Default.Equals(getValue(entryNode), selectedValue))
                continue;

            setValue(entryNode.Model, selectedValue);
            entryNode.Model.ModifiedUtc = DateTimeOffset.UtcNow;
            _entries.Upsert(entryNode.Model);
            entryNode.Refresh();
            changed++;
        }

        var selectedLabel = labelForValue(selectedValue);
        if (changed == 0)
        {
            ReportStatus(LogLevel.Info, $"Selected connections already use {selectedLabel} for {settingName}.");
            return;
        }

        ApplyFilter();
        NotifySurfacePropertyChanges();
        Audit(auditAction, "connection", null,
            $"Set {settingName} to {selectedLabel} on {DisplayText.Count(changed, "connection")} via bulk action.",
            selectedLabel);
        MirrorDatabase();
        ReportStatus(LogLevel.Success,
            $"Set {settingName} to {selectedLabel} on {DisplayText.Count(changed, "connection")}.");
    }

    private static bool TryGetCommonLaunchValue<T>(
        IReadOnlyList<EntryNode> entries,
        Func<EntryNode, T?> getValue,
        out T? commonValue)
        where T : struct
    {
        commonValue = default;
        if (entries.Count == 0) return false;

        commonValue = getValue(entries[0]);
        for (var i = 1; i < entries.Count; i++)
        {
            if (!EqualityComparer<T?>.Default.Equals(commonValue, getValue(entries[i])))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<ChoiceDialogOption> CreateModeOptions() =>
    [
        new("Inherit from folder", "Resolve the launch mode from the parent folder at launch time.", null),
        new("Remote control", "Start a standard remote-control session.", ConnectionMode.RemoteControl),
        new("File transfer", "Start a file-transfer session for the selected connection.", ConnectionMode.FileTransfer),
        new("VPN", "Start a VPN session when supported by the installed client.", ConnectionMode.Vpn),
        new("Chat", "Open the TeamViewer chat URI handler for the selected connection.", ConnectionMode.Chat),
        new("Video call", "Open the TeamViewer video-call URI handler for the selected connection.", ConnectionMode.VideoCall),
        new("Presentation", "Open the TeamViewer presentation URI handler for the selected connection.", ConnectionMode.Presentation),
    ];

    private static IReadOnlyList<ChoiceDialogOption> CreateQualityOptions() =>
    [
        new("Inherit from folder", "Resolve the quality preference from the parent folder at launch time.", null),
        new("Auto", "Let TeamViewer choose the best balance for the session.", ConnectionQuality.AutoSelect),
        new("Optimize quality", "Favor image quality when bandwidth allows it.", ConnectionQuality.OptimizeQuality),
        new("Optimize speed", "Favor responsiveness on slower or less reliable networks.", ConnectionQuality.OptimizeSpeed),
        new("Custom", "Use custom TeamViewer quality settings already configured in the client.", ConnectionQuality.CustomSettings),
        new("Undefined", "Pass TeamViewer's undefined quality value for compatibility with imported data.", ConnectionQuality.Undefined),
    ];

    private static IReadOnlyList<ChoiceDialogOption> CreateAccessControlOptions() =>
    [
        new("Inherit from folder", "Resolve access control from the parent folder at launch time.", null),
        new("Undefined", "Use TeamViewer's default access-control behavior.", AccessControl.Undefined),
        new("Full access", "Request full access for the remote session.", AccessControl.FullAccess),
        new("Confirm all", "Require confirmation for all access requests.", AccessControl.ConfirmAll),
        new("View and show", "Limit the session to viewing and showing actions.", AccessControl.ViewAndShow),
        new("Custom settings", "Use the custom access-control settings configured in TeamViewer.", AccessControl.CustomSettings),
    ];

    private void BulkSetProxy()
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var initialProxy = TryGetCommonProxy(entries, out var commonProxy) ? commonProxy : null;
        var proxy = BulkProxyDialog.Prompt(Application.Current?.MainWindow, initialProxy);
        if (proxy is null)
        {
            ReportStatus(LogLevel.Warning, "Bulk proxy update cancelled.");
            return;
        }

        BulkApplyProxy(
            entries,
            proxy,
            "bulk_set_proxy",
            $"Set proxy to {proxy.Endpoint} on {{0}} via bulk action.",
            $"Set proxy to {proxy.Endpoint} on {{0}}.");
    }

    private void BulkClearProxy()
    {
        var entries = SelectedNodes.OfType<EntryNode>().ToList();
        if (entries.Count == 0) return;

        var proxyCount = entries.Count(entry => entry.Model.Proxy is not null);
        if (proxyCount == 0)
        {
            ReportStatus(LogLevel.Info, "Selected connections do not have proxy routing configured.");
            return;
        }

        if (!_dialogs.Confirm(
                Application.Current?.MainWindow,
                $"Clear proxy routing from {DisplayText.Count(proxyCount, "selected connection")}?\n\n" +
                "Saved proxy usernames and passwords on those connections will be removed. Connections without a proxy will be left unchanged.",
                "Clear proxy routing",
                "Clear proxy",
                isDestructive: true))
        {
            ReportStatus(LogLevel.Warning, "Bulk proxy clear cancelled.");
            return;
        }

        BulkApplyProxy(
            entries,
            null,
            "bulk_clear_proxy",
            "Cleared proxy routing on {0} via bulk action.",
            "Cleared proxy routing on {0}.");
    }

    private void BulkApplyProxy(
        IReadOnlyList<EntryNode> entries,
        ProxySettings? proxy,
        string auditAction,
        string auditTemplate,
        string statusTemplate)
    {
        var changed = 0;
        foreach (var entryNode in entries)
        {
            if (EqualityComparer<ProxySettings?>.Default.Equals(entryNode.Model.Proxy, proxy))
                continue;

            entryNode.Model.Proxy = proxy;
            entryNode.Model.ModifiedUtc = DateTimeOffset.UtcNow;
            _entries.Upsert(entryNode.Model);
            entryNode.Refresh();
            changed++;
        }

        if (changed == 0)
        {
            ReportStatus(LogLevel.Info, proxy is null
                ? "Selected connections already have no proxy routing."
                : $"Selected connections already use {proxy.Endpoint}.");
            return;
        }

        var countText = DisplayText.Count(changed, "connection");
        ApplyFilter();
        NotifySurfacePropertyChanges();
        Audit(auditAction, "connection", null, string.Format(auditTemplate, countText), proxy?.Endpoint);
        MirrorDatabase();
        ReportStatus(LogLevel.Success, string.Format(statusTemplate, countText));
    }

    private static bool TryGetCommonProxy(IReadOnlyList<EntryNode> entries, out ProxySettings? commonProxy)
    {
        commonProxy = null;
        if (entries.Count == 0) return false;

        commonProxy = entries[0].Model.Proxy;
        for (var i = 1; i < entries.Count; i++)
        {
            if (!EqualityComparer<ProxySettings?>.Default.Equals(commonProxy, entries[i].Model.Proxy))
                return false;
        }

        return true;
    }

    public System.Windows.Input.ICommand ExportCommand { get; }
    public System.Windows.Input.ICommand ImportCommand { get; }
    public System.Windows.Input.ICommand ImportCsvCommand { get; }
    public System.Windows.Input.ICommand TogglePinCommand { get; }
    public System.Windows.Input.ICommand OpenSettingsCommand { get; }
    public System.Windows.Input.ICommand OpenDatabaseFolderCommand { get; }
    public System.Windows.Input.ICommand ImportTeamViewerHistoryCommand { get; }
    public System.Windows.Input.ICommand SyncTeamViewerCloudCommand { get; }
    public System.Windows.Input.ICommand ExportSessionsCommand { get; }
    public System.Windows.Input.ICommand RunExternalToolCommand { get; }
    public bool CanOpenDatabaseFolder => TryGetOpenableDatabaseFolder(out _);
    public string OpenDatabaseFolderTooltip => CanOpenDatabaseFolder
        ? $"Open database folder: {DatabaseLocationDisplay}"
        : "Database folder is unavailable until TeamStation has an open database path.";
    public bool CanExportSessions => !string.IsNullOrWhiteSpace(_startupDbPath);
    public string SessionExportTooltip => CanExportSessions
        ? "Export session launch history as CSV beside the current database."
        : "Session export is unavailable until TeamStation has an open database path.";

    public void Reload()
    {
        // v0.3.5: clear bulk multi-selection on every Reload — node identities
        // are about to be replaced and IsMultiSelected on stale references
        // would silently leak references to nodes no longer in the tree.
        ClearMultiSelection();

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
        Search.SaveCommand.RaiseCanExecuteChanged();
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
        if (_dialogs.EditEntry(draft, Application.Current?.MainWindow))
        {
            _entries.Upsert(draft);
            Reload();
            SelectById(draft.Id);
            Audit("create", "connection", draft.Id, $"Created connection \"{draft.Name}\".");
            MirrorDatabase();
            ReportStatus(LogLevel.Success, $"Created connection \"{draft.Name}\".");
        }
    }

    private void QuickLaunch(ConnectionEntry entry, bool persist)
    {
        if (persist)
        {
            _entries.Upsert(entry);
            Reload();
            SelectById(entry.Id);
            Audit("create", "connection", entry.Id, $"Saved quick connection \"{entry.Name}\".");
            MirrorDatabase();
        }

        LaunchEntry(entry, persistLastConnected: persist);
    }

    private void EditSelected()
    {
        switch (Selected)
        {
            case EntryNode entry:
                if (_dialogs.EditEntry(entry.Model, Application.Current?.MainWindow))
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
                if (_dialogs.EditFolder(folder.Model, Application.Current?.MainWindow))
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

    private void DuplicateSelectedEntry()
    {
        if (Selected is not EntryNode entry)
            return;

        var duplicate = CreateDuplicateEntry(entry.Model, _entries.GetAll().Select(existing => existing.Name));
        if (!_dialogs.EditEntry(duplicate, Application.Current?.MainWindow))
        {
            ReportStatus(LogLevel.Warning, "Duplicate cancelled.");
            return;
        }

        _entries.Upsert(duplicate);
        Reload();
        SelectById(duplicate.Id);
        Audit(
            "duplicate",
            "connection",
            duplicate.Id,
            $"Duplicated \"{entry.Name}\" as \"{duplicate.Name}\".",
            entry.Id.ToString());
        MirrorDatabase();
        ReportStatus(LogLevel.Success, $"Created duplicate \"{duplicate.Name}\".");
    }

    private static ConnectionEntry CreateDuplicateEntry(ConnectionEntry source, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ConnectionEntry
        {
            ParentFolderId = source.ParentFolderId,
            Name = BuildDuplicateName(source.Name, existingNames),
            TeamViewerId = source.TeamViewerId,
            ProfileName = source.ProfileName,
            Password = source.Password,
            Mode = source.Mode,
            Quality = source.Quality,
            AccessControl = source.AccessControl,
            Proxy = source.Proxy is null
                ? null
                : new ProxySettings(source.Proxy.Host, source.Proxy.Port, source.Proxy.Username, source.Proxy.Password),
            TeamViewerPathOverride = source.TeamViewerPathOverride,
            IsPinned = false,
            WakeMacAddress = source.WakeMacAddress,
            WakeBroadcastAddress = source.WakeBroadcastAddress,
            PreLaunchScript = source.PreLaunchScript,
            PostLaunchScript = source.PostLaunchScript,
            Notes = source.Notes,
            Tags = source.Tags.ToList(),
            LastConnectedUtc = null,
        };
    }

    private static string BuildDuplicateName(string sourceName, IEnumerable<string> existingNames)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "Connection" : sourceName.Trim();
        var candidate = $"{baseName} copy";
        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(candidate))
            return candidate;

        for (var i = 2; ; i++)
        {
            var numbered = $"{candidate} {i}";
            if (!existing.Contains(numbered))
                return numbered;
        }
    }

    private void ApplyFilter()
    {
        var query = Search.SearchText.Trim();
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
        var path = _dialogs.ChooseExportPath(Application.Current?.MainWindow);
        if (path is null) return;

        var anyPasswords = _entries.GetAll().Any(e => !string.IsNullOrEmpty(e.Password))
                           || _folders.GetAll().Any(f => !string.IsNullOrEmpty(f.DefaultPassword));
        if (anyPasswords && !_dialogs.Confirm(Application.Current?.MainWindow,
                "This backup contains saved passwords in plain text.\n\n" +
                "Anyone who can open the file can read those credentials. Store it only where you would store a password vault export.\n\n" +
                "Continue with the export?",
                "Export plaintext backup",
                "Export backup",
                isDestructive: true))
        {
            ReportStatus(LogLevel.Warning, "Backup cancelled.");
            return;
        }

        try
        {
            var backup = JsonBackup.Build(_folders.GetAll(), _entries.GetAll());
            AtomicFile.WriteAllText(path, backup);
            Audit("export", "json-backup", null, $"Wrote backup to {path}.");
            ReportStatus(LogLevel.Success, $"Backup written to {path}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"Backup failed: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "Backup failed", ex.ToString());
        }
    }

    private void Import()
    {
        var path = _dialogs.ChooseImportPath(Application.Current?.MainWindow);
        if (path is null) return;

        try
        {
            var text = File.ReadAllText(path);
            var (folders, entries) = JsonBackup.Parse(text);

            if (!_dialogs.Confirm(Application.Current?.MainWindow,
                $"Restore {DisplayText.Count(folders.Count, "folder")} and {DisplayText.Count(entries.Count, "connection")} from\n\n{path}\n\n" +
                "Existing items with matching IDs will be overwritten. Review the file source before continuing.",
                "Restore backup",
                "Restore backup",
                isDestructive: true))
            {
                ReportStatus(LogLevel.Warning, "Restore cancelled.");
                return;
            }

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
            _dialogs.ShowError(Application.Current?.MainWindow, "Restore failed", ex.ToString());
        }
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true)
            return;

        _settingsService.Save(_settings);
        ThemeManager.Apply(_settings.Theme);
        TvExePath = !string.IsNullOrWhiteSpace(_settings.TeamViewerPathOverride)
            ? _settings.TeamViewerPathOverride
            : TeamViewerPathResolver.Resolve() ?? "TeamViewer.exe not found - install TeamViewer before launching";
        IsTeamViewerReady = File.Exists(TvExePath);
        RefreshTeamViewerVersion();
        OnPropertyChanged(nameof(ExternalTools));
        OnPropertyChanged(nameof(HasExternalTools));
        OnPropertyChanged(nameof(HasTeamViewerApiToken));
        OnPropertyChanged(nameof(CanSyncTeamViewerCloud));
        OnPropertyChanged(nameof(CloudSyncStatusText));
        OnPropertyChanged(nameof(CloudSyncButtonText));
        OnPropertyChanged(nameof(CloudSyncToneBrush));
        ((RelayCommand)SyncTeamViewerCloudCommand).RaiseCanExecuteChanged();
        Search.RaiseSavedSearchesChanged();
        OnPropertyChanged(nameof(SavedSearches));
        OnPropertyChanged(nameof(HasSavedSearches));
        Audit("settings", "application", null, "Updated settings.");
        ReportStatus(LogLevel.Success, "Settings saved.");
    }

    private void ImportTeamViewerHistory()
    {
        try
        {
            var paths = TeamViewerHistoryImport.DefaultPaths();
            var result = TeamViewerHistoryImport.ScanFiles(paths, _entries.GetAll());

            foreach (var missingPath in result.MissingPaths)
                AppendLog(LogLevel.Info, $"TeamViewer history file not found: {missingPath}");
            foreach (var readError in result.ReadErrors)
                AppendLog(LogLevel.Warning, $"TeamViewer history file could not be read: {readError}");

            if (result.ReadErrors.Count > 0 && result.Entries.Count == 0)
            {
                ReportStatus(LogLevel.Error, "TeamViewer history import failed. Review the activity panel for file access details.");
                _dialogs.ShowError(
                    Application.Current?.MainWindow,
                    "TeamViewer history import failed",
                    "TeamStation could not read TeamViewer's local history files.\n\n" + string.Join('\n', result.ReadErrors));
                return;
            }

            if (result.Entries.Count == 0)
            {
                ReportStatus(LogLevel.Warning, result.ReadPaths.Count == 0
                    ? "No readable TeamViewer history files were found."
                    : $"No new TeamViewer history entries were found in {DisplayText.Count(result.ReadPaths.Count, "history file")}.");
                return;
            }

            foreach (var entry in result.Entries)
                _entries.Upsert(entry);

            Reload();
            Audit("import", "teamviewer-history", null, $"Imported {DisplayText.Count(result.Entries.Count, "connection")} from TeamViewer history.");
            MirrorDatabase();
            ReportStatus(result.ReadErrors.Count > 0 ? LogLevel.Warning : LogLevel.Success,
                result.ReadErrors.Count > 0
                    ? $"Imported {DisplayText.Count(result.Entries.Count, "connection")}; {DisplayText.Count(result.ReadErrors.Count, "history file")} could not be read."
                    : $"Imported {DisplayText.Count(result.Entries.Count, "connection")} from {DisplayText.Count(result.ReadPaths.Count, "history file")}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"TeamViewer history import failed: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "TeamViewer history import failed", ex.ToString());
        }
    }

    private async Task SyncTeamViewerCloudAsync()
    {
        if (!HasTeamViewerApiToken)
        {
            ReportStatus(LogLevel.Warning, "Add a TeamViewer Web API token in Settings before syncing cloud devices.");
            return;
        }

        if (IsCloudSyncing)
            return;

        IsCloudSyncing = true;
        ReportStatus(LogLevel.Info, "Syncing TeamViewer cloud devices...");
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
            _dialogs.ShowError(Application.Current?.MainWindow, "TeamViewer cloud sync failed", ex.Message);
        }
        finally
        {
            IsCloudSyncing = false;
        }
    }

    private void ExportSessions()
    {
        if (!CanExportSessions)
        {
            ReportStatus(LogLevel.Error, "Session export path is unavailable.");
            _dialogs.ShowError(
                Application.Current?.MainWindow,
                "Session export unavailable",
                "TeamStation does not have an open database path to place the session CSV beside.");
            return;
        }

        try
        {
            var path = Path.Combine(Path.GetDirectoryName(_startupDbPath!) ?? ".", $"teamstation-sessions-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _sessions.ExportCsv(path);
            Audit("export", "sessions", null, $"Exported session history to {path}.");
            ReportStatus(LogLevel.Success, $"Session history exported to {path}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"Session export failed: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "Session export failed", ex.ToString());
        }
    }

    private void OpenDatabaseFolder()
    {
        if (!TryGetOpenableDatabaseFolder(out var folder))
        {
            ReportStatus(LogLevel.Warning, "Database folder is unavailable.");
            _dialogs.ShowError(
                Application.Current?.MainWindow,
                "Database folder unavailable",
                "TeamStation does not have an open database folder to show.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
            Audit("open", "database-folder", null, $"Opened database folder {folder}.");
            ReportStatus(LogLevel.Success, $"Opened database folder {folder}.");
        }
        catch (Exception ex)
        {
            ReportStatus(LogLevel.Error, $"Could not open database folder: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "Open database folder failed", ex.ToString());
        }
    }

    private bool TryGetOpenableDatabaseFolder(out string folder)
    {
        if (!TryResolveDatabaseFolder(out folder))
            return false;

        return Directory.Exists(folder);
    }

    private bool TryResolveDatabaseFolder(out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(_startupDbPath))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(_startupDbPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            folder = directory;
            return true;
        }
        catch
        {
            return false;
        }
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
        var path = _dialogs.ChooseImportCsvPath(Application.Current?.MainWindow);
        if (path is null) return;

        try
        {
            var text = File.ReadAllText(path);
            var result = CsvImport.Parse(text, _folders.GetAll());

            if (result.Errors.Count > 0)
            {
                foreach (var err in result.Errors) AppendLog(LogLevel.Error, $"CSV: {err}");
                ReportStatus(LogLevel.Error, "CSV import stopped. Review the activity panel for column-mapping errors.");
                _dialogs.ShowError(Application.Current?.MainWindow, "CSV import", string.Join('\n', result.Errors));
                return;
            }

            var message = $"CSV will add {DisplayText.Count(result.Folders.Count, "new folder")} and {DisplayText.Count(result.Entries.Count, "connection")} from\n\n{path}\n\n" +
                          (result.Skipped.Count > 0 ? $"{DisplayText.Count(result.Skipped.Count, "row")} will be skipped. Review the activity panel for details.\n\n" : string.Empty) +
                          "Continue?";
            if (!_dialogs.Confirm(Application.Current?.MainWindow, message))
            {
                ReportStatus(LogLevel.Warning, "CSV import cancelled.");
                return;
            }

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
            _dialogs.ShowError(Application.Current?.MainWindow, "CSV import failed", ex.ToString());
        }
    }

    public void Reparent(TreeNode source, FolderNode? newParent)
    {
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

        if (!_dialogs.Confirm(
                Application.Current?.MainWindow,
                $"Delete {kind} \"{deletedName}\"?{suffix}",
                "Delete item",
                "Delete",
                isDestructive: true))
            return;

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

    private void LaunchViaProtocol()
    {
        if (Selected is not EntryNode entry)
            return;

        LaunchEntry(
            entry.Model,
            persistLastConnected: true,
            new LaunchOptions(
                UseBase64Password: true,
                ForceUri: true,
                PreferProtocolHandler: true));
    }

    private void OpenTeamViewerWebClient()
    {
        if (Selected is not EntryNode entry)
            return;

        var effective = InheritanceResolver.Resolve(entry.Model, _foldersById);
        var teamViewerId = effective.TeamViewerId.Trim();
        if (string.IsNullOrWhiteSpace(teamViewerId))
        {
            ReportStatus(LogLevel.Warning, "Selected connection does not have a TeamViewer ID for the Web Client.");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(teamViewerId, copy: true);
            Process.Start(new ProcessStartInfo
            {
                FileName = TeamViewerWebClient.PortalUri.ToString(),
                UseShellExecute = true,
            });

            Audit("open_web_client", "connection", entry.Id,
                $"Opened TeamViewer Web Client for \"{entry.Name}\".", teamViewerId);
            ReportStatus(LogLevel.Success, $"Opened TeamViewer Web Client and copied ID {teamViewerId}.");
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, $"TeamViewer Web Client handoff failed: {ex.Message}");
            ReportStatus(LogLevel.Error, $"Could not open TeamViewer Web Client: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "TeamViewer Web Client failed", ex.ToString());
        }
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

    private void LaunchEntry(ConnectionEntry source, bool persistLastConnected, LaunchOptions? forcedOptions = null)
    {
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

        // Clipboard-password mode — send the ID to TeamViewer without the password
        // on argv and stage the password on the clipboard instead. Avoids the
        // process-command-line disclosure window for hostile multi-user hosts.
        LaunchOptions? overrideOptions = null;
        ConnectionEntry launchTarget = effective;
        var clipboardStagedPassword = (string?)null;
        if (forcedOptions?.ForceUri != true && ShouldUseClipboardPasswordMode(effective))
        {
            if (TryStageClipboardPassword(effective.Password!))
            {
                clipboardStagedPassword = effective.Password;
                launchTarget = CloneWithoutPassword(effective);
                overrideOptions = new LaunchOptions(UseBase64Password: true, ForceUri: false);
                AppendLog(LogLevel.Info, "Password staged on clipboard — paste it into the TeamViewer prompt. It will clear in 30s.");
            }
        }

        var launchOptions = overrideOptions ?? forcedOptions ?? new LaunchOptions(
            UseBase64Password: true,
            ForceUri: false,
            PreferProtocolHandler: _settings.PreferProtocolLaunch);
        var launchPlan = LaunchRoutePlanner.Plan(launchTarget, launchOptions);
        if (launchPlan.FellBackToExecutable)
            AppendLog(LogLevel.Info, launchPlan.Description);

        LaunchOutcome outcome;
        try
        {
            // v0.3.5: route through the byte[] launcher overload when the
            // entry has its own (non-inherited) password and clipboard mode
            // is NOT engaged. The launcher zeros the buffers via try/finally
            // immediately after argv is composed. The folder-inheritance
            // case (source.Password is null but effective.Password came
            // from a folder default) keeps using the legacy string path —
            // a byte-aware InheritanceResolver is its own task.
            byte[]? pwBytes = null;
            byte[]? proxyPwBytes = null;
            if (clipboardStagedPassword is null)
            {
                if (!string.IsNullOrEmpty(source.Password))
                    pwBytes = _entries.LoadEntryPasswordBytes(source.Id);
                if (!string.IsNullOrEmpty(source.Proxy?.Password))
                    proxyPwBytes = _entries.LoadEntryProxyPasswordBytes(source.Id);
            }

            outcome = pwBytes is not null || proxyPwBytes is not null
                ? _launcher.Launch(launchTarget, pwBytes, proxyPwBytes, launchOptions)
                : (launchOptions == LaunchOptions.Default
                    ? _launcher.Launch(launchTarget)
                    : _launcher.Launch(launchTarget, launchOptions));
        }
        catch (Exception ex)
        {
            ClearClipboardIfMatches(clipboardStagedPassword);
            ReportStatus(LogLevel.Error, $"Launch failed: {ex.Message}");
            _dialogs.ShowError(Application.Current?.MainWindow, "Launch failed", ex.ToString());
            return;
        }

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
                Route = outcome.Uri is not null ? "URI" : (clipboardStagedPassword is not null ? "CLI+Clipboard" : "CLI"),
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
            // Clipboard clear still scheduled 30s out — intentional, so the user
            // has time to paste into the TeamViewer authorization dialog.
            if (clipboardStagedPassword is not null)
                _ = ScheduleClipboardClearAsync(clipboardStagedPassword);
            MirrorDatabase();
            ReportStatus(LogLevel.Success, outcome.Uri is not null
                ? $"Launched \"{source.Name}\" via URI handler."
                : $"Launched \"{source.Name}\" (pid {outcome.ProcessId?.ToString() ?? "?"}).");
        }
        else
        {
            ClearClipboardIfMatches(clipboardStagedPassword);
            ReportStatus(LogLevel.Error, $"Launch failed: {outcome.Error}");
            _dialogs.ShowError(Application.Current?.MainWindow, "Launch failed", outcome.Error ?? "Unknown error.");
        }
    }

    private bool TryStageClipboardPassword(string password)
    {
        try
        {
            System.Windows.Clipboard.SetDataObject(password, copy: true);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, $"Clipboard copy failed; launching with password on argv: {ex.Message}");
            return false;
        }
    }

    private static void ClearClipboardIfMatches(string? expected)
    {
        if (expected is null) return;
        try
        {
            if (System.Windows.Clipboard.ContainsText() &&
                System.Windows.Clipboard.GetText() == expected)
            {
                System.Windows.Clipboard.Clear();
            }
        }
        catch
        {
            // Clipboard contention — best effort; the 30s scheduled clear will retry.
        }
    }

    private bool ShouldUseClipboardPasswordMode(ConnectionEntry effective)
    {
        if (!_settings.PreferClipboardPasswordLaunch) return false;
        if (string.IsNullOrEmpty(effective.Password)) return false;
        // URI-handler modes already carry the password in the URL — clipboard
        // mode only helps for CLI launches.
        return effective.Mode is null or ConnectionMode.RemoteControl
            or ConnectionMode.FileTransfer or ConnectionMode.Vpn;
    }

    private static ConnectionEntry CloneWithoutPassword(ConnectionEntry source) => new()
    {
        Id = source.Id,
        ParentFolderId = source.ParentFolderId,
        Name = source.Name,
        TeamViewerId = source.TeamViewerId,
        ProfileName = source.ProfileName,
        Password = null,
        Mode = source.Mode,
        Quality = source.Quality,
        AccessControl = source.AccessControl,
        Proxy = source.Proxy,
        TeamViewerPathOverride = source.TeamViewerPathOverride,
        IsPinned = source.IsPinned,
        WakeMacAddress = source.WakeMacAddress,
        WakeBroadcastAddress = source.WakeBroadcastAddress,
        PreLaunchScript = source.PreLaunchScript,
        PostLaunchScript = source.PostLaunchScript,
        Notes = source.Notes,
        Tags = source.Tags,
        LastConnectedUtc = source.LastConnectedUtc,
        CreatedUtc = source.CreatedUtc,
        ModifiedUtc = source.ModifiedUtc,
    };

    private static async Task ScheduleClipboardClearAsync(string expected)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null) return;
            await dispatcher.InvokeAsync(() => ClearClipboardIfMatches(expected));
        }
        catch { /* swallow — app may be shutting down */ }
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
