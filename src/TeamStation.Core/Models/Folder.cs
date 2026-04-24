namespace TeamStation.Core.Models;

public sealed class Folder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AccentColor { get; set; }
    public int SortOrder { get; set; }

    // Inherited defaults pushed down to descendant entries
    public ConnectionMode? DefaultMode { get; set; }
    public ConnectionQuality? DefaultQuality { get; set; }
    public AccessControl? DefaultAccessControl { get; set; }
    public string? DefaultPassword { get; set; }
    public string? DefaultTeamViewerPath { get; set; }
    public string? DefaultWakeBroadcastAddress { get; set; }
    public string? PreLaunchScript { get; set; }
    public string? PostLaunchScript { get; set; }
}
