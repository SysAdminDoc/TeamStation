using System.Collections.ObjectModel;
using System.Windows.Media;
using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

public sealed class FolderNode : TreeNode
{
    private Brush? _accentCache;
    private string? _accentCacheKey;

    public FolderNode(Folder model, FolderNode? parent) : base(parent)
    {
        Model = model;
    }

    public Folder Model { get; }
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

    public ObservableCollection<TreeNode> Children { get; } = new();
    public string PathDisplay => DisplayText.FormatPath(this);
    public int FolderCount => Children.OfType<FolderNode>().Count();
    public int EntryCount => Children.OfType<EntryNode>().Count();
    public string ChildSummary => $"{DisplayText.Count(FolderCount, "subfolder")}, {DisplayText.Count(EntryCount, "connection")}";
    public bool HasAccentColor => !string.IsNullOrWhiteSpace(Model.AccentColor);
    public string AccentColorDisplay => Model.AccentColor ?? "Using the default folder accent";
    public bool HasDefaults =>
        Model.DefaultMode is not null ||
        Model.DefaultQuality is not null ||
        Model.DefaultAccessControl is not null ||
        !string.IsNullOrWhiteSpace(Model.DefaultPassword) ||
        !string.IsNullOrWhiteSpace(Model.DefaultTeamViewerPath) ||
        !string.IsNullOrWhiteSpace(Model.DefaultWakeBroadcastAddress) ||
        !string.IsNullOrWhiteSpace(Model.PreLaunchScript) ||
        !string.IsNullOrWhiteSpace(Model.PostLaunchScript);
    public bool HasDefaultPassword => !string.IsNullOrWhiteSpace(Model.DefaultPassword);
    public bool HasDefaultPath => !string.IsNullOrWhiteSpace(Model.DefaultTeamViewerPath);
    public bool HasScripts => !string.IsNullOrWhiteSpace(Model.PreLaunchScript) || !string.IsNullOrWhiteSpace(Model.PostLaunchScript);
    public string DefaultModeDisplay => DisplayText.ModeLabel(Model.DefaultMode, "Entry decides");
    public string DefaultQualityDisplay => DisplayText.QualityLabel(Model.DefaultQuality, "Entry decides");
    public string DefaultAccessControlDisplay => DisplayText.AccessLabel(Model.DefaultAccessControl, "Entry decides");
    public string PasswordPolicyDisplay => HasDefaultPassword ? "Default password available" : "No default password";
    public string DefaultPathDisplay => HasDefaultPath ? Model.DefaultTeamViewerPath! : "Auto-detected TeamViewer.exe";
    public string ScriptPolicyDisplay => HasScripts ? "Launch scripts configured" : "No launch scripts";
    public string DefaultsSummary => HasDefaults
        ? $"{DefaultModeDisplay} • {DefaultQualityDisplay} • {DefaultAccessControlDisplay}"
        : "No folder defaults configured";
    public string ParentSummary => Parent is null ? "Top-level folder" : $"Inside {Parent.Name}";

    /// <summary>
    /// Brush for the tree-item dot. Parses the folder's <c>#RRGGBB</c> accent
    /// color once per distinct value and caches the frozen brush. Falls back
    /// to the application's default folder-accent brush (Catppuccin Mauve) on
    /// parse failure or when unset.
    /// </summary>
    public Brush AccentBrush
    {
        get
        {
            var key = Model.AccentColor ?? string.Empty;
            if (_accentCache is not null && _accentCacheKey == key) return _accentCache;

            Brush brush;
            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(key);
                    var solid = new SolidColorBrush(color);
                    solid.Freeze();
                    brush = solid;
                }
                catch
                {
                    brush = DefaultAccent();
                }
            }
            else
            {
                brush = DefaultAccent();
            }

            _accentCache = brush;
            _accentCacheKey = key;
            return brush;
        }
    }

    /// <summary>Called after <see cref="Model"/> fields change so bindings re-evaluate.</summary>
    public void RefreshAccent()
    {
        _accentCache = null;
        _accentCacheKey = null;
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(PathDisplay));
        OnPropertyChanged(nameof(FolderCount));
        OnPropertyChanged(nameof(EntryCount));
        OnPropertyChanged(nameof(ChildSummary));
        OnPropertyChanged(nameof(HasAccentColor));
        OnPropertyChanged(nameof(AccentColorDisplay));
        OnPropertyChanged(nameof(HasDefaults));
        OnPropertyChanged(nameof(HasDefaultPassword));
        OnPropertyChanged(nameof(HasDefaultPath));
        OnPropertyChanged(nameof(HasScripts));
        OnPropertyChanged(nameof(DefaultModeDisplay));
        OnPropertyChanged(nameof(DefaultQualityDisplay));
        OnPropertyChanged(nameof(DefaultAccessControlDisplay));
        OnPropertyChanged(nameof(PasswordPolicyDisplay));
        OnPropertyChanged(nameof(DefaultPathDisplay));
        OnPropertyChanged(nameof(ScriptPolicyDisplay));
        OnPropertyChanged(nameof(DefaultsSummary));
        OnPropertyChanged(nameof(ParentSummary));
    }

    private static Brush DefaultAccent() =>
        System.Windows.Application.Current?.TryFindResource("MauveBrush") as Brush
        ?? Brushes.MediumPurple;
}
