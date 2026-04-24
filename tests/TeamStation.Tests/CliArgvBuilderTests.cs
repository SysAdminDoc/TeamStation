using System.Text;
using TeamStation.Core.Models;
using TeamStation.Launcher;

namespace TeamStation.Tests;

public class CliArgvBuilderTests
{
    private static ConnectionEntry Entry(string id = "123456789", string? password = null,
        ConnectionMode? mode = null, ConnectionQuality? quality = null, AccessControl? ac = null,
        ProxySettings? proxy = null) => new()
        {
            Name = "E", TeamViewerId = id, Password = password,
            Mode = mode, Quality = quality, AccessControl = ac, Proxy = proxy,
        };

    [Fact]
    public void Build_produces_id_only_for_bare_entry()
    {
        var argv = CliArgvBuilder.Build(Entry()).ToArray();
        Assert.Equal(new[] { "--id", "123456789" }, argv);
    }

    [Fact]
    public void Build_uses_PasswordB64_by_default_not_plaintext()
    {
        var argv = CliArgvBuilder.Build(Entry(password: "hunter2")).ToArray();
        Assert.Contains("--PasswordB64", argv);
        Assert.DoesNotContain("--Password", argv);
        // The B64 token is the base64 of UTF-8 "hunter2".
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hunter2"));
        Assert.Contains(b64, argv);
    }

    [Fact]
    public void Build_passes_plaintext_only_when_base64_opt_out()
    {
        var argv = CliArgvBuilder.Build(Entry(password: "hunter2"), base64Password: false).ToArray();
        Assert.Contains("--Password", argv);
        Assert.Contains("hunter2", argv);
    }

    [Fact]
    public void Build_emits_no_mode_flag_for_RemoteControl_or_null()
    {
        foreach (var mode in new ConnectionMode?[] { ConnectionMode.RemoteControl, null })
        {
            var argv = CliArgvBuilder.Build(Entry(mode: mode)).ToArray();
            Assert.DoesNotContain("--mode", argv);
        }
    }

    [Fact]
    public void Build_emits_filetransfer_and_vpn_mode_flags()
    {
        var ft = CliArgvBuilder.Build(Entry(mode: ConnectionMode.FileTransfer));
        Assert.Contains("fileTransfer", ft);
        var vpn = CliArgvBuilder.Build(Entry(mode: ConnectionMode.Vpn));
        Assert.Contains("vpn", vpn);
    }

    [Theory]
    [InlineData(ConnectionMode.Chat)]
    [InlineData(ConnectionMode.VideoCall)]
    [InlineData(ConnectionMode.Presentation)]
    public void Build_throws_for_URI_only_modes(ConnectionMode mode)
    {
        Assert.Throws<InvalidOperationException>(() => CliArgvBuilder.Build(Entry(mode: mode)));
    }

    [Fact]
    public void Build_skips_quality_when_null_or_AutoSelect()
    {
        foreach (var q in new ConnectionQuality?[] { null, ConnectionQuality.AutoSelect })
        {
            var argv = CliArgvBuilder.Build(Entry(quality: q)).ToArray();
            Assert.DoesNotContain("--quality", argv);
        }
    }

    [Fact]
    public void Build_emits_quality_flag_for_explicit_values()
    {
        var argv = CliArgvBuilder.Build(Entry(quality: ConnectionQuality.OptimizeSpeed));
        Assert.Contains("--quality", argv);
        Assert.Contains("3", argv); // OptimizeSpeed = 3
    }

    [Fact]
    public void Build_skips_ac_when_null_or_Undefined()
    {
        foreach (var ac in new AccessControl?[] { null, AccessControl.Undefined })
        {
            var argv = CliArgvBuilder.Build(Entry(ac: ac)).ToArray();
            Assert.DoesNotContain("--ac", argv);
        }
    }

    [Fact]
    public void Build_emits_proxy_triplet_with_base64_password()
    {
        var proxy = new ProxySettings("proxy.internal", 3128, "u", "pw");
        var argv = CliArgvBuilder.Build(Entry(proxy: proxy));
        Assert.Contains("--ProxyIP", argv);
        Assert.Contains("proxy.internal:3128", argv);
        Assert.Contains("--ProxyUser", argv);
        Assert.Contains("u", argv);
        Assert.Contains("--ProxyPassword", argv);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("pw")), argv);
    }
}
