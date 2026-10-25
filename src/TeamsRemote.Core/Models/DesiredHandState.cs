namespace TeamsRemote.Core.Models;

public enum DesiredHandState
{
    Raised,
    Lowered
}

public static class DesiredHandStateExtensions
{
    public const string RaisedWire = "raised";
    public const string LoweredWire = "lowered";

    public static string ToWireString(this DesiredHandState state) =>
        state switch
        {
            DesiredHandState.Raised => RaisedWire,
            DesiredHandState.Lowered => LoweredWire,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

    public static bool TryParseWire(string? value, out DesiredHandState state)
    {
        if (string.Equals(value?.Trim(), RaisedWire, StringComparison.OrdinalIgnoreCase))
        {
            state = DesiredHandState.Raised;
            return true;
        }

        if (string.Equals(value?.Trim(), LoweredWire, StringComparison.OrdinalIgnoreCase))
        {
            state = DesiredHandState.Lowered;
            return true;
        }

        state = default;
        return false;
    }
}
