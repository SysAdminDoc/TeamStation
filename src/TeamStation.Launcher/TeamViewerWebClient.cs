namespace TeamStation.Launcher;

/// <summary>
/// Official TeamViewer Web Client entry point. TeamViewer documents
/// connection-by-ID inside the Web Client, but not a stable direct-ID URL.
/// </summary>
public static class TeamViewerWebClient
{
    public static Uri PortalUri { get; } = new("https://web.teamviewer.com/");
}
