using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using TeamStation.App.Services;

namespace TeamStation.App.Views;

public partial class FileSystemFolderDialog : Window
{
    private bool _syncingPath;

    private FileSystemFolderDialog(string title, string initialPath)
    {
        InitializeComponent();
        ThemeManager.ConfigureWindow(this);
        DialogTitleText.Text = title;
        Title = title;
        DataContext = this;
        LoadRoots();

        if (!string.IsNullOrWhiteSpace(initialPath))
            PathBox.Text = initialPath.Trim();
    }

    public ObservableCollection<FileSystemFolderItem> RootFolders { get; } = new();
    public string? SelectedPath { get; private set; }

    public static string? Pick(Window? owner, string title, string initialPath = "")
    {
        var dialog = new FileSystemFolderDialog(title, initialPath);
        if (owner is not null)
            dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private void LoadRoots()
    {
        RootFolders.Clear();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name} {drive.VolumeLabel}"
                    : drive.Name;
                RootFolders.Add(FileSystemFolderItem.Create(label, drive.RootDirectory.FullName, isDrive: true));
            }
            catch
            {
                RootFolders.Add(FileSystemFolderItem.Create(drive.Name, drive.Name, isDrive: true));
            }
        }
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not FileSystemFolderItem { IsPlaceholder: false } item)
            return;

        _syncingPath = true;
        PathBox.Text = item.Path;
        _syncingPath = false;
        ValidatePath(showError: false);
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncingPath)
            ValidatePath(showError: true);
    }

    private bool ValidatePath(bool showError)
    {
        var path = PathBox.Text.Trim().Trim('"');
        if (path.Length == 0)
        {
            SelectedPath = null;
            OkButton.IsEnabled = false;
            ValidationBorder.Visibility = Visibility.Collapsed;
            return false;
        }

        if (Directory.Exists(path))
        {
            SelectedPath = Path.GetFullPath(path);
            OkButton.IsEnabled = true;
            ValidationBorder.Visibility = Visibility.Collapsed;
            return true;
        }

        SelectedPath = null;
        OkButton.IsEnabled = false;
        if (showError)
        {
            ValidationText.Text = "Folder path does not exist or is not accessible.";
            ValidationBorder.Visibility = Visibility.Visible;
        }

        return false;
    }

    private void FolderTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (OkButton.IsEnabled)
            Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidatePath(showError: true))
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class FileSystemFolderItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _hasLoadedChildren;

    private FileSystemFolderItem(string name, string path, bool isDrive, bool isPlaceholder = false)
    {
        Name = name;
        Path = path;
        IsDrive = isDrive;
        IsPlaceholder = isPlaceholder;
        if (!isPlaceholder)
            Children.Add(new FileSystemFolderItem("Loading...", path, isDrive: false, isPlaceholder: true));
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsDrive { get; }
    public bool IsPlaceholder { get; }
    public bool IsSelectable => !IsPlaceholder;
    public bool ShowPath => !IsPlaceholder && !string.Equals(Name, Path, StringComparison.OrdinalIgnoreCase);
    public string Icon => IsDrive ? "\uE8B7" : IsPlaceholder ? "\uE9D9" : "\uE8B7";
    public ObservableCollection<FileSystemFolderItem> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            if (_isExpanded)
                LoadChildren();
            OnPropertyChanged();
        }
    }

    public static FileSystemFolderItem Create(string name, string path, bool isDrive = false) =>
        new(name, path, isDrive);

    private void LoadChildren()
    {
        if (_hasLoadedChildren || IsPlaceholder)
            return;

        _hasLoadedChildren = true;
        Children.Clear();

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(Path)
                .OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return;
        }

        foreach (var directory in directories)
        {
            var name = System.IO.Path.GetFileName(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            Children.Add(new FileSystemFolderItem(string.IsNullOrWhiteSpace(name) ? directory : name, directory, isDrive: false));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
