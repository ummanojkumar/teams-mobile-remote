namespace TeamsRemote.Core.Models;

public record ControlEventReport
{
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public required TargetControl TargetControl { get; init; }
    public required string PropertyName { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? RedactedSummary { get; init; }
}
