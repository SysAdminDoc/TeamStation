namespace TeamStation.Core.Models;

public sealed record ProxySettings(string Host, int Port, string? Username, string? Password)
{
    public string Endpoint => $"{Host}:{Port}";
}
