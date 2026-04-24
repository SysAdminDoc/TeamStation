namespace TeamStation.Core.Models;

public sealed class ConnectionEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TeamViewerId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = "Default";
    public string? Password { get; set; }

    /// <summary>
    /// Connection mode. <c>null</c> means "inherit from parent folder chain"
    /// (resolved at launch time by <see cref="Services.InheritanceResolver"/>).
    /// </summary>
    public ConnectionMode? Mode { get; set; }

    /// <summary>Connection quality. <c>null</c> means "inherit from parent folder chain".</summary>
    public ConnectionQuality? Quality { get; set; }

    /// <summary>Access control. <c>null</c> means "inherit from parent folder chain".</summary>
    public AccessControl? AccessControl { get; set; }
    public ProxySettings? Proxy { get; set; }
    public string? TeamViewerPathOverride { get; set; }
    public bool IsPinned { get; set; }
    public string? WakeMacAddress { get; set; }
    public string? WakeBroadcastAddress { get; set; }
    public string? PreLaunchScript { get; set; }
    public string? PostLaunchScript { get; set; }
    public string? Notes { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset? LastConnectedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
}
