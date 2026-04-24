namespace TeamStation.Core.Models;

public sealed class SessionRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? EntryId { get; init; }
    public string EntryName { get; init; } = string.Empty;
    public string TeamViewerId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = "Default";
    public ConnectionMode? Mode { get; init; }
    public string Route { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndedUtc { get; set; }
    public string? Notes { get; set; }
    public string? Outcome { get; set; }

    public TimeSpan? Duration => EndedUtc is null ? null : EndedUtc.Value - StartedUtc;
}
