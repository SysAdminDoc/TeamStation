namespace TeamStation.Core.Models;

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public Guid? TargetId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Detail { get; init; }
}
