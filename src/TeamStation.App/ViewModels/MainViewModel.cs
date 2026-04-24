using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using TeamStation.App.Mvvm;
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
    private readonly TeamViewerLauncher _launcher;
    private readonly Func<ConnectionEntry, Window?, bool> _editEntryDialog;
    private readonly Func<Folder, Window?, bool> _editFolderDialog;
    private readonly Func<Window?, string?> _chooseExportPath;
    private readonly Func<Window?, string?> _chooseImportPath;
    private readonly Func<Window?, string, bool> _confirmDialog;

    private TreeNode? _selected;
    private string _status = string.Empty;
    private string _tvExePath;
    private string _searchText = string.Empty;
    private Dictionary<Guid, Folder> _foldersById = new();

    public MainViewModel(
        EntryRepository entries,
        FolderRepository folders,
        TeamViewerLauncher launcher,
        Func<ConnectionEntry, Window?, bool> editEntryDialog,
        Func<Folder, Window?, bool> editFolderDialog,
        Func<Window?, string?> chooseExportPath,
        Func<Window?, string?> chooseImportPath,
        Func<Window?, string, bool> confirmDialog,
        string? tvExePath)
    {
        _entries = entries;
        _folders = folders;
        _launcher = launcher;
        _editEntryDialog = editEntryDialog;
        _editFolderDialog = editFolderDialog;
        _chooseExportPath = chooseExportPath;
        _chooseImportPath = chooseImportPath;
        _confirmDialog = confirmDialog;
        _tvExePath = tvExePath ?? "TeamViewer.exe not found — install TeamViewer before launching";

        AddEntryCommand = new RelayCommand(AddEntry);
        AddFolderCommand = new RelayCommand(AddFolder);
        AddSubfolderCommand = new RelayCommand(AddSubfolder, () => Selected is FolderNode);
        RenameCommand = new RelayCommand(Rename, () => Selected is not null);
        MoveCommand = new RelayCommand(Move, () => Selected is not null);
        DeleteCommand = new RelayCommand(Delete, () => Selected is not null);
        EditCommand = new RelayCommand(EditSelected, () => Selected is not null);
        LaunchCommand = new RelayCommand(Launch, () => Selected is EntryNode);
        ExportCommand = new RelayCommand(Export);
        ImportCommand = new RelayCommand(Import);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);

        Reload();
    }

    public ObservableCollection<TreeNode> RootNodes { get; } = new();

    public TreeNode? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                foreach (var cmd in new[] { AddSubfolderCommand, RenameCommand, MoveCommand, DeleteCommand, EditCommand, LaunchCommand })
                    ((RelayCommand)cmd).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedIsEntry));
                OnPropertyChanged(nameof(SelectedIsFolder));
            }
        }
    }

    public bool SelectedIsEntry => Selected is EntryNode;
    public bool SelectedIsFolder => Selected is FolderNode;

    public string Status { get => _status; private set => SetField(ref _status, value); }
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
    public System.Windows.Input.ICommand ClearSearchCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    public void Reload()
    {
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

        var folderCount = folders.Count;
        var entryCount = entries.Count;
        Status = (folderCount, entryCount) switch
        {
            (0, 0) => "No folders or entries yet — right-click the empty tree to add one.",
            _ => $"{folderCount} folder{(folderCount == 1 ? "" : "s")}, {entryCount} entr{(entryCount == 1 ? "y" : "ies")}"
        };
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
        }
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
                    Status = $"Saved \"{entry.Name}\".";
                }
                break;

            case FolderNode folder:
                if (_editFolderDialog(folder.Model, Application.Current?.MainWindow))
                {
                    _folders.Upsert(folder.Model);
                    Reload();
                    SelectById(folder.Id);
                    Status = $"Saved folder \"{folder.Name}\".";
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
            return;
        }

        foreach (var root in RootNodes)
            _ = ComputeVisibility(root, query);
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

    private void Export()
    {
        var path = _chooseExportPath(Application.Current?.MainWindow);
        if (path is null) return;

        var anyPasswords = _entries.GetAll().Any(e => !string.IsNullOrEmpty(e.Password))
                           || _folders.GetAll().Any(f => !string.IsNullOrEmpty(f.DefaultPassword));
        if (anyPasswords && !_confirmDialog(Application.Current?.MainWindow,
                "This export contains passwords in PLAINTEXT.\n\n" +
                "Anyone with access to the resulting file can read them. Store it only where you would store a password manager vault.\n\n" +
                "Continue?"))
        {
            Status = "Export cancelled.";
            return;
        }

        try
        {
            var backup = JsonBackup.Build(_folders.GetAll(), _entries.GetAll());
            File.WriteAllText(path, backup);
            Status = $"Exported to {path}.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
            MessageBox.Show(Application.Current?.MainWindow!, ex.ToString(), "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                $"Import {folders.Count} folder(s) and {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} from\n\n{path}\n\n" +
                "Existing rows with matching IDs will be overwritten. Continue?"))
            {
                Status = "Import cancelled.";
                return;
            }

            foreach (var f in folders) _folders.Upsert(f);
            foreach (var e in entries) _entries.Upsert(e);
            Reload();
            Status = $"Imported {folders.Count} folder(s) and {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}.";
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {ex.Message}";
            MessageBox.Show(Application.Current?.MainWindow!, ex.ToString(), "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Reparent(TreeNode source, FolderNode? newParent)
    {
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
        Status = newParent is null
            ? $"Moved \"{source.Name}\" to root."
            : $"Moved \"{source.Name}\" to \"{newParent.Name}\".";
    }

    // ---- Folder commands ----

    private void AddFolder()
    {
        var name = InputDialog.Prompt(Application.Current?.MainWindow, "New folder", "Folder name:");
        if (name is null) return;
        var folder = new Folder { Name = name };
        _folders.Upsert(folder);
        Reload();
        SelectById(folder.Id);
    }

    private void AddSubfolder()
    {
        if (Selected is not FolderNode parent) return;
        var name = InputDialog.Prompt(Application.Current?.MainWindow, "New subfolder", $"Subfolder under \"{parent.Name}\":");
        if (name is null) return;
        var folder = new Folder { Name = name, ParentFolderId = parent.Id };
        _folders.Upsert(folder);
        parent.IsExpanded = true;
        Reload();
        SelectById(folder.Id);
    }

    // ---- Unified commands ----

    private void Rename()
    {
        if (Selected is null) return;
        var newName = InputDialog.Prompt(Application.Current?.MainWindow, "Rename", "New name:", Selected.Name);
        if (newName is null) return;

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
        SelectById(Selected.Id);
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
        Reload();
        SelectById(movedId);
    }

    private void Delete()
    {
        if (Selected is null) return;
        var kind = Selected is FolderNode ? "folder" : "entry";
        var suffix = Selected is FolderNode f && (f.Children.Count > 0)
            ? "\n\nNested subfolders will also be deleted. Entries inside will become unassigned (moved to root)."
            : string.Empty;

        var choice = MessageBox.Show(
            Application.Current?.MainWindow!,
            $"Delete {kind} \"{Selected.Name}\"?{suffix}",
            "Confirm delete",
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
    }

    private void Launch()
    {
        if (Selected is not EntryNode entry) return;
        // Resolve folder-chain inheritance at launch time so folder default
        // changes propagate to every entry that defers to them.
        var effective = InheritanceResolver.Resolve(entry.Model, _foldersById);
        var outcome = _launcher.Launch(effective);
        if (outcome.Success)
        {
            _entries.TouchLastConnected(entry.Id, DateTimeOffset.UtcNow);
            entry.Model.LastConnectedUtc = DateTimeOffset.UtcNow;
            entry.Refresh();
            Status = outcome.Uri is not null
                ? $"Launched \"{entry.Name}\" via URI handler."
                : $"Launched \"{entry.Name}\" (pid {outcome.ProcessId?.ToString() ?? "?"}).";
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

    // ---- Helpers ----

    private void SelectById(Guid id)
    {
        var node = FindById(RootNodes, id);
        if (node is null) return;
        ExpandToRoot(node);
        node.IsSelected = true;
        Selected = node;
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
