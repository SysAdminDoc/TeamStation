using System.Collections.ObjectModel;
using System.Windows.Media;
using TeamStation.Core.Models;

namespace TeamStation.App.ViewModels;

public sealed class FolderNode : TreeNode
{
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
    /// Brush for the tree-item dot. Resolves the folder's <c>#RRGGBB</c>
    /// accent color if present, otherwise falls back to the default folder
    /// accent looked up from application resources.
    /// </summary>
    public Brush AccentBrush
    {
        get
        {
            if (!string.IsNullOrEmpty(Model.AccentColor))
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(Model.AccentColor);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
                catch
                {
                    // fall through to default
                }
            }
            return System.Windows.Application.Current?.TryFindResource("MauveBrush") as Brush
                   ?? Brushes.MediumPurple;
        }
    }
}
