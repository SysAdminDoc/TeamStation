using TeamStation.Core.Models;
using TeamStation.Launcher;

namespace TeamStation.Tests;

public class LaunchRoutePlannerTests
{
    private static ConnectionEntry Entry(
        ConnectionMode? mode = ConnectionMode.RemoteControl,
        ConnectionQuality? quality = null,
        AccessControl? accessControl = null,
        ProxySettings? proxy = null) => new()
        {
            Name = "Test",
            TeamViewerId = "123456789",
            Mode = mode,
            Quality = quality,
            AccessControl = accessControl,
            Proxy = proxy,
        };

    [Fact]
    public void Default_remote_control_keeps_executable_route_for_compatibility()
    {
        var plan = LaunchRoutePlanner.Plan(Entry());

        Assert.Equal(LaunchRoute.Executable, plan.Route);
        Assert.False(plan.FellBackToExecutable);
    }

    [Theory]
    [InlineData(ConnectionMode.RemoteControl)]
    [InlineData(ConnectionMode.FileTransfer)]
    [InlineData(ConnectionMode.Vpn)]
    public void Preferred_protocol_uses_protocol_handler_when_no_cli_only_options_are_set(ConnectionMode mode)
    {
        var plan = LaunchRoutePlanner.Plan(
            Entry(mode),
            new LaunchOptions(UseBase64Password: true, ForceUri: false, PreferProtocolHandler: true));

        Assert.Equal(LaunchRoute.ProtocolHandler, plan.Route);
        Assert.False(plan.FellBackToExecutable);
        Assert.Equal("PROTOCOL", plan.Badge);
    }

    [Theory]
    [InlineData(ConnectionMode.Chat)]
    [InlineData(ConnectionMode.VideoCall)]
    [InlineData(ConnectionMode.Presentation)]
    public void Uri_only_modes_always_use_protocol_handler(ConnectionMode mode)
    {
        var plan = LaunchRoutePlanner.Plan(Entry(mode));

        Assert.Equal(LaunchRoute.ProtocolHandler, plan.Route);
        Assert.False(plan.FellBackToExecutable);
    }

    [Fact]
    public void Preferred_protocol_falls_back_to_executable_for_proxy_settings()
    {
        var plan = LaunchRoutePlanner.Plan(
            Entry(proxy: new ProxySettings("proxy.internal", 3128, "user", "pw")),
            new LaunchOptions(UseBase64Password: true, ForceUri: false, PreferProtocolHandler: true));

        Assert.Equal(LaunchRoute.Executable, plan.Route);
        Assert.True(plan.FellBackToExecutable);
        Assert.Contains("proxy settings", plan.Description);
    }

    [Fact]
    public void Preferred_protocol_falls_back_to_executable_for_quality_and_access_overrides()
    {
        var plan = LaunchRoutePlanner.Plan(
            Entry(
                quality: ConnectionQuality.OptimizeSpeed,
                accessControl: AccessControl.ConfirmAll),
            new LaunchOptions(UseBase64Password: true, ForceUri: false, PreferProtocolHandler: true));

        Assert.Equal(LaunchRoute.Executable, plan.Route);
        Assert.True(plan.FellBackToExecutable);
        Assert.Contains("quality overrides", plan.Description);
        Assert.Contains("access-control overrides", plan.Description);
    }

    [Fact]
    public void Force_uri_overrides_command_line_only_options_for_explicit_uri_requests()
    {
        var plan = LaunchRoutePlanner.Plan(
            Entry(proxy: new ProxySettings("proxy.internal", 3128, "user", "pw")),
            new LaunchOptions(UseBase64Password: true, ForceUri: true));

        Assert.Equal(LaunchRoute.ProtocolHandler, plan.Route);
        Assert.False(plan.FellBackToExecutable);
    }
}
