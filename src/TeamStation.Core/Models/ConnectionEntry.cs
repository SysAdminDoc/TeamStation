namespace TeamStation.Core.Models;

public sealed class ConnectionEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TeamViewerId { get; set; } = string.Empty;
    public string? Password { get; set; }
    public ConnectionMode Mode { get; set; } = ConnectionMode.RemoteControl;
    public ConnectionQuality Quality { get; set; } = ConnectionQuality.AutoSelect;
    public AccessControl AccessControl { get; set; } = AccessControl.Undefined;
    public ProxySettings? Proxy { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset? LastConnectedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}
