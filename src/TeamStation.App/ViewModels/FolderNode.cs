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
    }

    private static Brush DefaultAccent() =>
        System.Windows.Application.Current?.TryFindResource("MauveBrush") as Brush
        ?? Brushes.MediumPurple;
}
