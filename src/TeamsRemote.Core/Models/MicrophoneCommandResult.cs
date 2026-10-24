namespace TeamsRemote.Core.Models;

public record MicrophoneCommandResult
{
    public required CommandExecutionStatus Status { get; init; }
    public required string FinalState { get; init; }
    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; }
}
