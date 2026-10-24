namespace TeamsRemote.Core.Models;

public record TeamsDiagnosticReport
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required TeamsProcessInfo Process { get; init; }
    public required MeetingSurfaceInfo MeetingSurface { get; init; }
    public IReadOnlyList<ControlDiagnosticInfo> Controls { get; init; } = Array.Empty<ControlDiagnosticInfo>();
    public string DiagnosticSummary { get; init; } = string.Empty;
}
