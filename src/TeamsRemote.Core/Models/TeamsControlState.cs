namespace TeamsRemote.Core.Models;

public static class TeamsControlState
{
    public const string Muted = "muted";
    public const string Unmuted = "unmuted";
    public const string On = "on";
    public const string Off = "off";
    public const string Raised = "raised";
    public const string Lowered = "lowered";
    public const string Unknown = "unknown";

    public static readonly IReadOnlySet<string> ValidStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Muted,
        Unmuted,
        On,
        Off,
        Raised,
        Lowered,
        Unknown
    };
}
