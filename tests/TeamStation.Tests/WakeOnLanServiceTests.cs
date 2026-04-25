using System.Net;
using TeamStation.Core.Services;

namespace TeamStation.Tests;

public class WakeOnLanServiceTests
{
    [Fact]
    public void TryBuildMagicPacket_builds_standard_magic_packet()
    {
        Assert.True(WakeOnLanService.TryBuildMagicPacket("AA-BB-CC-DD-EE-FF", out var packet, out var message));

        Assert.Equal(string.Empty, message);
        Assert.Equal(102, packet.Length);
        for (var i = 0; i < 6; i++)
            Assert.Equal((byte)0xFF, packet[i]);

        var expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        for (var block = 0; block < 16; block++)
        {
            var offset = 6 + block * expectedMac.Length;
            Assert.Equal(expectedMac, packet[offset..(offset + expectedMac.Length)]);
        }
    }

    [Theory]
    [InlineData("00:11:22:33:44:55")]
    [InlineData("00-11-22-33-44-55")]
    [InlineData("0011.2233.4455")]
    [InlineData("00 11 22 33 44 55")]
    [InlineData("001122334455")]
    public void TryBuildMagicPacket_accepts_common_mac_formats(string macAddress)
    {
        Assert.True(WakeOnLanService.TryBuildMagicPacket(macAddress, out var packet, out var message));
        Assert.Equal(string.Empty, message);
        Assert.Equal(102, packet.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00:11:22:33:44")]
    [InlineData("00:11:22:33:44:55:66")]
    [InlineData("00:11:22:33:44:ZZ")]
    public void TryBuildMagicPacket_rejects_invalid_mac_without_throwing(string macAddress)
    {
        Assert.False(WakeOnLanService.TryBuildMagicPacket(macAddress, out var packet, out var message));
        Assert.Empty(packet);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void TryResolveTarget_defaults_to_limited_broadcast()
    {
        Assert.True(WakeOnLanService.TryResolveTarget(null, out var target, out var message));

        Assert.Equal(IPAddress.Broadcast, target);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void TryResolveTarget_accepts_ipv4_directed_broadcast()
    {
        Assert.True(WakeOnLanService.TryResolveTarget("192.168.1.255", out var target, out var message));

        Assert.Equal(IPAddress.Parse("192.168.1.255"), target);
        Assert.Equal(string.Empty, message);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("::1")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    public void TryResolveTarget_rejects_invalid_or_unusable_targets(string broadcastAddress)
    {
        Assert.False(WakeOnLanService.TryResolveTarget(broadcastAddress, out _, out var message));
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public void TrySend_returns_false_for_invalid_input_without_sending()
    {
        Assert.False(WakeOnLanService.TrySend("not-a-mac", "192.168.1.255", out var message));
        Assert.Equal("Wake-on-LAN MAC address is invalid.", message);
    }
}
