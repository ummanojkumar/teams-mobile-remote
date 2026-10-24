namespace TeamsRemote.Core.Models;

public record ControlDiagnosticInfo
{
    public required TargetControl TargetControl { get; init; }
    public required bool Found { get; init; }
    public string? AccessibleName { get; init; }
    public string? ControlType { get; init; }
    public string? AutomationId { get; init; }
    public IReadOnlyList<string> SupportedPatterns { get; init; } = Array.Empty<string>();
    public string ReadableState { get; init; } = TeamsControlState.Unknown;
    public string? StateSource { get; init; }
    public bool IsEventObservable { get; init; }
    public required ControlCapabilityTier CapabilityTier { get; init; }
    public string? Reason { get; init; }
}
