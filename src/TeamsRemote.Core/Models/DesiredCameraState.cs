namespace TeamsRemote.Core.Models;

public enum DesiredCameraState
{
    On,
    Off
}

public static class DesiredCameraStateExtensions
{
    public const string OnWire = "on";
    public const string OffWire = "off";

    public static string ToWireString(this DesiredCameraState state) =>
        state switch
        {
            DesiredCameraState.On => OnWire,
            DesiredCameraState.Off => OffWire,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

    public static bool TryParseWire(string? value, out DesiredCameraState state)
    {
        if (string.Equals(value?.Trim(), OnWire, StringComparison.OrdinalIgnoreCase))
        {
            state = DesiredCameraState.On;
            return true;
        }

        if (string.Equals(value?.Trim(), OffWire, StringComparison.OrdinalIgnoreCase))
        {
            state = DesiredCameraState.Off;
            return true;
        }

        state = default;
        return false;
    }
}
