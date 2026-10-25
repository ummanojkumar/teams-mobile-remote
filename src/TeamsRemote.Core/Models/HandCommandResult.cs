namespace TeamsRemote.Core.Models;

public sealed record HandCommandResult
{
    public required CommandExecutionStatus Status { get; init; }
    public required string FinalState { get; init; }
    public required string Reason { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan Duration { get; init; }
    public bool PromoteToVerified { get; init; }
}
