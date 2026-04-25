using TeamStation.Launcher;

namespace TeamStation.Tests;

public class TeamViewerWebClientTests
{
    [Fact]
    public void Portal_uri_points_to_the_official_browser_client_entrypoint()
    {
        Assert.Equal("https://web.teamviewer.com/", TeamViewerWebClient.PortalUri.ToString());
    }
}
