using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;

namespace TeamsRemote.Core.Transport;

public sealed class CommandProcessor
{
    private readonly MeetingStateStore _stateStore;
    private readonly CommandDeduplicator _deduplicator;
    private readonly CommandRateLimiter _rateLimiter;
    private readonly Func<Guid?> _getActiveDeviceIdFunc;
    private readonly ITeamsController? _teamsController;

    public CommandProcessor(
        MeetingStateStore stateStore,
        Func<Guid?> getActiveDeviceIdFunc,
        ITeamsController? teamsController = null,
        CommandDeduplicator? deduplicator = null,
        CommandRateLimiter? rateLimiter = null)
    {
        _stateStore = stateStore;
        _getActiveDeviceIdFunc = getActiveDeviceIdFunc;
        _teamsController = teamsController;
        _deduplicator = deduplicator ?? new CommandDeduplicator();
        _rateLimiter = rateLimiter ?? new CommandRateLimiter();
    }

    public ProtocolEnvelope ProcessEnvelope(ProtocolEnvelope envelope)
    {
        return ProcessEnvelopeAsync(envelope).GetAwaiter().GetResult();
    }

    public async Task<ProtocolEnvelope> ProcessEnvelopeAsync(
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        // 1. Version validation
        if (envelope.Version != ProtocolEnvelope.CurrentVersion)
        {
            var result = new CommandResultPayload
            {
                Status = "rejected",
                StateRevision = _stateStore.StateRevision,
                ErrorCode = ProtocolErrorCodes.UnsupportedProtocolVersion,
                Reason = $"Protocol version {envelope.Version} is unsupported. Expected version {ProtocolEnvelope.CurrentVersion}."
            };
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, result, envelope.DeviceId, envelope.CorrelationId);
        }

        // 2. Authorization check
        var activeDeviceId = _getActiveDeviceIdFunc();
        if (activeDeviceId == null || envelope.DeviceId == Guid.Empty || envelope.DeviceId != activeDeviceId.Value)
        {
            var result = new CommandResultPayload
            {
                Status = "rejected",
                StateRevision = _stateStore.StateRevision,
                ErrorCode = ProtocolErrorCodes.PairingRequired,
                Reason = "Device is not authorized or has been revoked. Re-pairing is required."
            };
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, result, envelope.DeviceId, envelope.CorrelationId);
        }

        // 3. Deduplication check
        if (_deduplicator.TryGet(envelope.MessageId, out var cachedResult) && cachedResult != null)
        {
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, cachedResult, envelope.DeviceId, envelope.CorrelationId, envelope.MessageId);
        }

        // 4. Parse CommandPayload
        CommandPayload? cmdPayload;
        try
        {
            cmdPayload = envelope.DeserializePayload<CommandPayload>();
        }
        catch (Exception ex)
        {
            var result = new CommandResultPayload
            {
                Status = "failed",
                StateRevision = _stateStore.StateRevision,
                ErrorCode = "INVALID_COMMAND_PAYLOAD",
                Reason = ex.Message
            };
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, result, envelope.DeviceId, envelope.CorrelationId);
        }

        if (cmdPayload == null || string.IsNullOrWhiteSpace(cmdPayload.Command))
        {
            var result = new CommandResultPayload
            {
                Status = "failed",
                StateRevision = _stateStore.StateRevision,
                ErrorCode = "EMPTY_COMMAND",
                Reason = "Command payload is missing or empty."
            };
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, result, envelope.DeviceId, envelope.CorrelationId);
        }

        // 5. Rate limiting check
        if (!_rateLimiter.CheckAndRecord(cmdPayload.Command))
        {
            var result = new CommandResultPayload
            {
                Status = "rejected",
                StateRevision = _stateStore.StateRevision,
                ErrorCode = ProtocolErrorCodes.RateLimited,
                Reason = $"Command rate limit exceeded for '{cmdPayload.Command}'. Maximum 5 requests per 10 seconds allowed."
            };
            return ProtocolEnvelope.Create(ProtocolMessageType.CommandResult, result, envelope.DeviceId, envelope.CorrelationId);
        }

        // 6. Command Dispatch
        CommandResultPayload execResult;

        switch (cmdPayload.Command.ToLowerInvariant())
        {
            case "test_echo":
                execResult = new CommandResultPayload
                {
                    Status = "confirmed",
                    StateRevision = _stateStore.StateRevision,
                    State = new Dictionary<string, string>
                    {
                        ["echo"] = cmdPayload.EchoText ?? "echo_ok"
                    },
                    Reason = "Echo command validated successfully."
                };
                break;

            case "query_state":
                var (rev, micState, conf, meeting) = _stateStore.GetSnapshot();
                execResult = new CommandResultPayload
                {
                    Status = "confirmed",
                    StateRevision = rev,
                    State = new Dictionary<string, string>
                    {
                        ["microphone"] = micState,
                        ["confidence"] = conf.ToString().ToLowerInvariant(),
                        ["meeting"] = meeting
                    },
                    Reason = "Current state snapshot returned."
                };
                break;

            case "set_microphone":
                var targetDesired = cmdPayload.Desired?.Trim().ToLowerInvariant();
                if (targetDesired != "muted" && targetDesired != "unmuted")
                {
                    execResult = new CommandResultPayload
                    {
                        Status = "rejected",
                        StateRevision = _stateStore.StateRevision,
                        ErrorCode = ProtocolErrorCodes.InvalidDesiredState,
                        Reason = $"Invalid desired state '{cmdPayload.Desired}'. Expected 'muted' or 'unmuted'."
                    };
                    break;
                }

                var (currentRev, currentMicState, curConf, curMeeting) = _stateStore.GetSnapshot();

                // Safe exception: If refreshed state already equals the requested desired state,
                // return AlreadyInDesiredState without invoking Teams.
                if (string.Equals(currentMicState, targetDesired, StringComparison.OrdinalIgnoreCase))
                {
                    execResult = new CommandResultPayload
                    {
                        Status = "confirmed",
                        StateRevision = currentRev,
                        Reason = $"Microphone is already {targetDesired}. No action needed.",
                        State = new Dictionary<string, string>
                        {
                            ["microphone"] = currentMicState,
                            ["confidence"] = curConf.ToString().ToLowerInvariant(),
                            ["meeting"] = curMeeting
                        }
                    };
                    break;
                }

                // Strict revision rule:
                // - If lower than current: reject with STALE_COMMAND
                if (cmdPayload.ExpectedStateRevision.HasValue && cmdPayload.ExpectedStateRevision.Value < currentRev)
                {
                    execResult = new CommandResultPayload
                    {
                        Status = "rejected",
                        StateRevision = currentRev,
                        ErrorCode = ProtocolErrorCodes.StaleCommand,
                        Reason = $"Command rejected: expected state revision {cmdPayload.ExpectedStateRevision.Value} is lower than current revision {currentRev}."
                    };
                    break;
                }

                // - If higher than current: reject with INVALID_STATE_REVISION
                if (cmdPayload.ExpectedStateRevision.HasValue && cmdPayload.ExpectedStateRevision.Value > currentRev)
                {
                    execResult = new CommandResultPayload
                    {
                        Status = "rejected",
                        StateRevision = currentRev,
                        ErrorCode = ProtocolErrorCodes.InvalidStateRevision,
                        Reason = $"Command rejected: expected state revision {cmdPayload.ExpectedStateRevision.Value} is higher than current revision {currentRev}."
                    };
                    break;
                }

                // Exact match (or no revision specified): proceed with UIA execution
                if (_teamsController == null)
                {
                    execResult = new CommandResultPayload
                    {
                        Status = "unsupported",
                        StateRevision = currentRev,
                        ErrorCode = ProtocolErrorCodes.UnsupportedInPhase4A,
                        Reason = "No Teams controller available to execute remote microphone command."
                    };
                    break;
                }

                var desiredEnum = targetDesired == "muted" ? DesiredMicrophoneState.Muted : DesiredMicrophoneState.Unmuted;
                _stateStore.MarkPending();

                try
                {
                    var actionResult = await _teamsController.SetMicrophoneStateAsync(desiredEnum, cancellationToken);
                    switch (actionResult.Status)
                    {
                        case CommandExecutionStatus.Confirmed:
                        case CommandExecutionStatus.AlreadyInDesiredState:
                            _stateStore.UpdateObserved(actionResult.FinalState, isConfirmed: true, meetingStatus: "active");
                            var (newRev, newMic, newConf, newMeeting) = _stateStore.GetSnapshot();
                            execResult = new CommandResultPayload
                            {
                                Status = "confirmed",
                                StateRevision = newRev,
                                Reason = actionResult.Reason,
                                State = new Dictionary<string, string>
                                {
                                    ["microphone"] = newMic,
                                    ["confidence"] = newConf.ToString().ToLowerInvariant(),
                                    ["meeting"] = newMeeting
                                }
                            };
                            break;

                        case CommandExecutionStatus.TimedOut:
                        case CommandExecutionStatus.StateUncertain:
                            _stateStore.MarkStaleOrUncertain();
                            execResult = new CommandResultPayload
                            {
                                Status = "state_uncertain",
                                StateRevision = _stateStore.StateRevision,
                                ErrorCode = ProtocolErrorCodes.VerificationTimedOut,
                                Reason = actionResult.Reason
                            };
                            break;

                        case CommandExecutionStatus.Unsupported:
                            _stateStore.MarkStaleOrUncertain();
                            execResult = new CommandResultPayload
                            {
                                Status = "unsupported",
                                StateRevision = _stateStore.StateRevision,
                                ErrorCode = actionResult.ErrorCode ?? ProtocolErrorCodes.ControlNotFound,
                                Reason = actionResult.Reason
                            };
                            break;

                        default:
                            _stateStore.MarkStaleOrUncertain();
                            execResult = new CommandResultPayload
                            {
                                Status = "failed",
                                StateRevision = _stateStore.StateRevision,
                                ErrorCode = actionResult.ErrorCode ?? ProtocolErrorCodes.InvokeFailed,
                                Reason = actionResult.Reason
                            };
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _stateStore.MarkStaleOrUncertain();
                    execResult = new CommandResultPayload
                    {
                        Status = "failed",
                        StateRevision = _stateStore.StateRevision,
                        ErrorCode = ProtocolErrorCodes.InvokeFailed,
                        Reason = ex.Message
                    };
                }
                break;

            case "set_camera":
            case "set_hand":
                execResult = new CommandResultPayload
                {
                    Status = "unsupported",
                    StateRevision = _stateStore.StateRevision,
                    ErrorCode = ProtocolErrorCodes.UnsupportedInPhase4A,
                    Reason = $"Remote control of '{cmdPayload.Command}' is locked in Phase 4A."
                };
                break;

            default:
                execResult = new CommandResultPayload
                {
                    Status = "rejected",
                    StateRevision = _stateStore.StateRevision,
                    ErrorCode = "UNKNOWN_COMMAND",
                    Reason = $"Unrecognized command '{cmdPayload.Command}'."
                };
                break;
        }

        // 7. Store in deduplicator
        _deduplicator.Store(envelope.MessageId, execResult);

        return ProtocolEnvelope.Create(
            ProtocolMessageType.CommandResult,
            execResult,
            envelope.DeviceId,
            envelope.CorrelationId,
            envelope.MessageId);
    }
}
