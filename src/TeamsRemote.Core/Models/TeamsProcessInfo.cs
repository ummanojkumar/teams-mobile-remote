namespace TeamsRemote.Core.Models;

public record TeamsProcessInfo
{
    public required bool IsRunning { get; init; }
    public string? ProcessName { get; init; }
    public int? ProcessId { get; init; }
    public string? ExecutablePath { get; init; }
    public IReadOnlyList<int> AllProcessIds { get; init; } = Array.Empty<int>();
}
