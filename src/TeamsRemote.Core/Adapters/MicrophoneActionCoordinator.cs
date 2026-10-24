using System.Diagnostics;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Redaction;

namespace TeamsRemote.Core.Adapters;

public sealed class MicrophoneActionCoordinator
{
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    public async Task<MicrophoneCommandResult> ExecuteAsync(
        DesiredMicrophoneState desiredState,
        Func<Task<(bool found, string currentState, bool hasPattern, bool isEnabled, Action invokeAction)>> controlProvider,
        Func<Task<string>> reQueryStateFunc,
        TimeSpan verificationTimeout,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Concurrency / In-Flight Guard (Duplicate action prevention)
        if (!await _actionLock.WaitAsync(0, cancellationToken))
        {
            return new MicrophoneCommandResult
            {
                Status = CommandExecutionStatus.Failed,
                FinalState = TeamsControlState.Unknown,
                ErrorCode = "COMMAND_IN_PROGRESS",
                Reason = "Another microphone command is currently executing. Duplicate concurrent action rejected.",
                Duration = sw.Elapsed
            };
        }

        try
        {
            string targetWire = desiredState.ToWireString();

            // 2. Query control provider
            var (found, currentState, hasPattern, isEnabled, invokeAction) = await controlProvider();

            if (!found)
            {
                return new MicrophoneCommandResult
                {
                    Status = CommandExecutionStatus.Unsupported,
                    FinalState = TeamsControlState.Unknown,
                    ErrorCode = "CONTROL_NOT_FOUND",
                    Reason = "Microphone control could not be located in active meeting UI.",
                    Duration = sw.Elapsed
                };
            }

            // 3. Pre-action state & pattern evaluation
            var (shouldInvoke, decisionStatus, decisionReason) = DesiredStateDecisionEngine.Evaluate(
                currentState,
                desiredState,
                hasPattern,
                isEnabled);

            if (!shouldInvoke)
            {
                string? errorCode = decisionStatus switch
                {
                    CommandExecutionStatus.StateUncertain => "STATE_UNKNOWN",
                    CommandExecutionStatus.Unsupported => "ACTION_PATTERN_UNAVAILABLE",
                    _ => null
                };

                return new MicrophoneCommandResult
                {
                    Status = decisionStatus,
                    FinalState = currentState,
                    ErrorCode = errorCode,
                    Reason = decisionReason,
                    Duration = sw.Elapsed
                };
            }

            // 4. Invoke action
            try
            {
                invokeAction();
            }
            catch (Exception ex)
            {
                return new MicrophoneCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    FinalState = currentState,
                    ErrorCode = "INVOKE_FAILED",
                    Reason = RedactionHelper.RedactSecrets($"Failed to invoke pattern on microphone control: {ex.Message}"),
                    Duration = sw.Elapsed
                };
            }

            // 5. Bounded verification loop
            var timeoutAt = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * verificationTimeout.TotalSeconds);
            string latestObserved = currentState;

            while (Stopwatch.GetTimestamp() < timeoutAt)
            {
                try
                {
                    latestObserved = await reQueryStateFunc();
                    if (string.Equals(latestObserved, targetWire, StringComparison.OrdinalIgnoreCase))
                    {
                        return new MicrophoneCommandResult
                        {
                            Status = CommandExecutionStatus.Confirmed,
                            FinalState = latestObserved,
                            Reason = $"Microphone successfully set to {latestObserved} and confirmed via UIA verification.",
                            Duration = sw.Elapsed
                        };
                    }
                }
                catch
                {
                    // Continue polling through transient read errors
                }

                await Task.Delay(50, cancellationToken);
            }

            // 6. Timeout
            return new MicrophoneCommandResult
            {
                Status = CommandExecutionStatus.TimedOut,
                FinalState = latestObserved,
                ErrorCode = "VERIFICATION_TIMED_OUT",
                Reason = $"Invoked action pattern on microphone control, but verification timed out after {verificationTimeout.TotalMilliseconds}ms without confirming state '{targetWire}'. State remained '{latestObserved}'.",
                Duration = sw.Elapsed
            };
        }
        finally
        {
            _actionLock.Release();
        }
    }
}
