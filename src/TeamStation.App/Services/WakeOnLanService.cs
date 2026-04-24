using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace TeamStation.App.Services;

public static partial class WakeOnLanService
{
    public static bool TrySend(string? macAddress, string? broadcastAddress, out string message)
    {
        if (string.IsNullOrWhiteSpace(macAddress))
        {
            message = "No Wake-on-LAN MAC address is configured.";
            return false;
        }

        if (!TryParseMac(macAddress, out var mac))
        {
            message = "Wake-on-LAN MAC address is invalid.";
            return false;
        }

        var target = IPAddress.Broadcast;
        if (!string.IsNullOrWhiteSpace(broadcastAddress) &&
            !IPAddress.TryParse(broadcastAddress.Trim(), out target))
        {
            message = "Wake-on-LAN broadcast address is invalid.";
            return false;
        }

        var packet = new byte[102];
        for (var i = 0; i < 6; i++) packet[i] = 0xFF;
        for (var block = 1; block <= 16; block++)
            Buffer.BlockCopy(mac, 0, packet, block * 6, mac.Length);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Send(packet, packet.Length, new IPEndPoint(target, 9));
        message = $"Wake-on-LAN packet sent to {target}.";
        return true;
    }

    private static bool TryParseMac(string value, out byte[] mac)
    {
        var hex = MacSeparatorRegex().Replace(value.Trim(), string.Empty);
        mac = [];
        if (hex.Length != 12 || !HexRegex().IsMatch(hex))
            return false;

        mac = Enumerable.Range(0, 6)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
            .ToArray();
        return true;
    }

    [GeneratedRegex("[-:.\\s]")]
    private static partial Regex MacSeparatorRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{12}$")]
    private static partial Regex HexRegex();
}
