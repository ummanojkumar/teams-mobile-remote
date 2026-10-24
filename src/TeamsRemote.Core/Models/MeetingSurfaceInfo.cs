namespace TeamsRemote.Core.Models;

public record MeetingSurfaceInfo
{
    public required bool IsDetected { get; init; }
    public long WindowHandle { get; init; }
    public string? RedactedWindowTitle { get; init; }
    public string? AutomationClassName { get; init; }
    public string? ControlSurfaceType { get; init; }
}
