using TeamStation.Core.Models;
using TeamStation.Launcher;

namespace TeamStation.Tests;

public class UriSchemeBuilderTests
{
    [Theory]
    [InlineData(ConnectionMode.RemoteControl, "teamviewer10://control?device=123456789")]
    [InlineData(ConnectionMode.FileTransfer, "tvfiletransfer1://?device=123456789")]
    [InlineData(ConnectionMode.Vpn, "tvvpn1://?device=123456789")]
    [InlineData(ConnectionMode.Chat, "tvchat1://?device=123456789")]
    [InlineData(ConnectionMode.VideoCall, "tvvideocall1://?device=123456789")]
    [InlineData(ConnectionMode.Presentation, "tvpresent1://?device=123456789")]
    public void Build_produces_correct_scheme_per_mode(ConnectionMode mode, string expected)
    {
        var entry = new ConnectionEntry { Name = "E", TeamViewerId = "123456789", Mode = mode };
        var uri = UriSchemeBuilder.Build(entry);
        Assert.Equal(expected, uri);
    }

    [Fact]
    public void Build_percent_encodes_password()
    {
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789",
            Password = "p&%=w",  // characters that need URL escaping
            Mode = ConnectionMode.RemoteControl,
        };
        var uri = UriSchemeBuilder.Build(entry);
        // Uri.EscapeDataString encodes '&' as %26, '%' as %25, '=' as %3D
        Assert.Contains("authorization=p%26%25%3Dw", uri);
    }

    [Fact]
    public void Build_treats_null_mode_as_RemoteControl()
    {
        var entry = new ConnectionEntry { Name = "E", TeamViewerId = "123456789", Mode = null };
        var uri = UriSchemeBuilder.Build(entry);
        Assert.StartsWith("teamviewer10://control?", uri);
    }

    [Theory]
    [InlineData(ConnectionMode.Chat, true)]
    [InlineData(ConnectionMode.VideoCall, true)]
    [InlineData(ConnectionMode.Presentation, true)]
    [InlineData(ConnectionMode.RemoteControl, false)]
    [InlineData(ConnectionMode.FileTransfer, false)]
    [InlineData(ConnectionMode.Vpn, false)]
    public void IsUriOnly_matches_documented_CLI_capability_matrix(ConnectionMode mode, bool expected)
    {
        Assert.Equal(expected, UriSchemeBuilder.IsUriOnly(mode));
    }
}
