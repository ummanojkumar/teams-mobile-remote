using System.Diagnostics;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Redaction;

namespace TeamsRemote.Core.Adapters;

public sealed class HandActionCoordinator
{
    private readonly SemaphoreSlim _actionLock = new(1, 1);

    public async Task<HandCommandResult> ExecuteAsync(
        DesiredHandState desiredState,
        Func<Task<(bool found, string currentState, bool hasPattern, bool isEnabled, Action invokeAction, string? fingerprint)>> controlProvider,
        Func<Task<string>> reQueryStateFunc,
        TimeSpan verificationTimeout,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Concurrency / In-Flight Guard (Duplicate concurrent action prevention)
        if (!await _actionLock.WaitAsync(0, cancellationToken))
        {
            return new HandCommandResult
            {
                Status = CommandExecutionStatus.Failed,
                FinalState = TeamsControlState.Unknown,
                ErrorCode = "COMMAND_IN_PROGRESS",
                Reason = "Another raise hand command is currently executing. Duplicate concurrent action rejected.",
                Duration = sw.Elapsed,
                PromoteToVerified = false
            };
        }

        try
        {
            string targetWire = desiredState.ToWireString();

            // 2. Query control provider
            var (found, currentState, hasPattern, isEnabled, invokeAction, _) = await controlProvider();

            if (!found)
            {
                return new HandCommandResult
                {
                    Status = CommandExecutionStatus.Unsupported,
                    FinalState = TeamsControlState.Unknown,
                    ErrorCode = "CONTROL_NOT_FOUND",
                    Reason = "Raise Hand control could not be located in active meeting UI.",
                    Duration = sw.Elapsed,
                    PromoteToVerified = false
                };
            }

            // 3. Pre-action action eligibility & desired state evaluation
            var (shouldInvoke, decisionStatus, decisionReason) = DesiredStateDecisionEngine.EvaluateHand(
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

                return new HandCommandResult
                {
                    Status = decisionStatus,
                    FinalState = currentState,
                    ErrorCode = errorCode,
                    Reason = decisionReason,
                    Duration = sw.Elapsed,
                    // No promotion if no action was verified
                    PromoteToVerified = false
                };
            }

            // 4. Invoke action (TogglePattern or InvokePattern)
            try
            {
                invokeAction();
            }
            catch (Exception ex)
            {
                return new HandCommandResult
                {
                    Status = CommandExecutionStatus.Failed,
                    FinalState = currentState,
                    ErrorCode = "INVOKE_FAILED",
                    Reason = RedactionHelper.RedactSecrets($"Failed to invoke pattern on hand control: {ex.Message}"),
                    Duration = sw.Elapsed,
                    PromoteToVerified = false
                };
            }

            // 5. Bounded post-action verification loop
            // NOTE: Never promote capability based solely on successful pattern invocation above.
            // Promotion requires verified state match below.
            var timeoutAt = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * verificationTimeout.TotalSeconds);
            string latestObserved = currentState;

            while (Stopwatch.GetTimestamp() < timeoutAt)
            {
                try
                {
                    latestObserved = await reQueryStateFunc();
                    if (string.Equals(latestObserved, targetWire, StringComparison.OrdinalIgnoreCase))
                    {
                        return new HandCommandResult
                        {
                            Status = CommandExecutionStatus.Confirmed,
                            FinalState = latestObserved,
                            Reason = $"Hand successfully set to {latestObserved} and confirmed via post-action UIA verification.",
                            Duration = sw.Elapsed,
                            // Only promote to verified after independent post-action UIA verification confirms desired state!
                            PromoteToVerified = true
                        };
                    }
                }
                catch
                {
                    // Continue polling through transient read errors
                }

                await Task.Delay(50, cancellationToken);
            }

            // 6. Verification Timeout: result is uncertain, capability remains observe-only (not promoted)
            return new HandCommandResult
            {
                Status = CommandExecutionStatus.TimedOut,
                FinalState = latestObserved,
                ErrorCode = "VERIFICATION_TIMED_OUT",
                Reason = $"Invoked action pattern on hand control, but verification timed out after {verificationTimeout.TotalMilliseconds}ms without confirming state '{targetWire}'. State remained '{latestObserved}'.",
                Duration = sw.Elapsed,
                PromoteToVerified = false
            };
        }
        finally
        {
            _actionLock.Release();
        }
    }
}
