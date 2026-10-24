using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.Classification;

public static class DesiredStateDecisionEngine
{
    public static (bool ShouldInvoke, CommandExecutionStatus Status, string Reason) Evaluate(
        string currentState,
        DesiredMicrophoneState desiredState,
        bool hasActionablePattern,
        bool isEnabled)
    {
        string targetWire = desiredState.ToWireString();

        if (string.Equals(currentState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                CommandExecutionStatus.StateUncertain,
                "Current microphone state is unknown or unreadable. Action prohibited to prevent unsafe toggling.");
        }

        if (string.Equals(currentState, targetWire, StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                CommandExecutionStatus.AlreadyInDesiredState,
                $"Microphone is already {currentState}. No invocation required.");
        }

        if (!hasActionablePattern || !isEnabled)
        {
            return (
                false,
                CommandExecutionStatus.Unsupported,
                "Microphone control does not support an actionable invocation pattern (Toggle/Invoke) or is disabled.");
        }

        return (
            true,
            CommandExecutionStatus.Confirmed,
            $"Microphone state '{currentState}' differs from requested state '{targetWire}'. Invocation required.");
    }
}
