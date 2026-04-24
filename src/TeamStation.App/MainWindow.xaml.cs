using System.Reflection;
using System.Windows;
using TeamStation.Launcher;

namespace TeamStation.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "dev";
        VersionBadge.Text = $"v{version}";

        var exe = TeamViewerPathResolver.Resolve();
        TvStatus.Text = exe is null
            ? "TeamViewer.exe not found. Install the full TeamViewer client before shipping v0.1.0 behavior."
            : $"Found: {exe}";
    }
}
