using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace TeamStation.Core.Services;

public static partial class WakeOnLanService
{
    public static bool TrySend(string? macAddress, string? broadcastAddress, out string message)
    {
        if (!TryBuildMagicPacket(macAddress, out var packet, out message))
            return false;

        if (!TryResolveTarget(broadcastAddress, out var target, out message))
            return false;

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.EnableBroadcast = true;
            udp.Send(packet, packet.Length, new IPEndPoint(target, 9));
        }
        catch (SocketException ex)
        {
            message = $"Wake-on-LAN packet could not be sent: {ex.Message}";
            return false;
        }

        message = $"Wake-on-LAN packet sent to {target}.";
        return true;
    }

    public static bool TryBuildMagicPacket(string? macAddress, out byte[] packet, out string message)
    {
        packet = [];
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

        packet = new byte[102];
        for (var i = 0; i < 6; i++) packet[i] = 0xFF;
        for (var block = 1; block <= 16; block++)
            Buffer.BlockCopy(mac, 0, packet, block * 6, mac.Length);

        message = string.Empty;
        return true;
    }

    public static bool TryResolveTarget(string? broadcastAddress, out IPAddress target, out string message)
    {
        target = IPAddress.Broadcast;
        message = string.Empty;

        if (string.IsNullOrWhiteSpace(broadcastAddress))
            return true;

        if (!IPAddress.TryParse(broadcastAddress.Trim(), out var parsed))
        {
            message = "Wake-on-LAN broadcast address is invalid.";
            return false;
        }

        if (parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            message = "Wake-on-LAN broadcast address must be an IPv4 address.";
            return false;
        }

        if (!IsUsableIpv4Target(parsed))
        {
            message = "Wake-on-LAN broadcast address must be a usable IPv4 broadcast or directed broadcast address.";
            return false;
        }

        target = parsed;
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

    private static bool IsUsableIpv4Target(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        return !bytes.All(b => b == 0) &&
               bytes[0] is < 224 or > 239;
    }

    [GeneratedRegex("[-:.\\s]")]
    private static partial Regex MacSeparatorRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{12}$")]
    private static partial Regex HexRegex();
}
