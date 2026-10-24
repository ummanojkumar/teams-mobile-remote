using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;

namespace TeamsRemote.Core.Transport;

public sealed class CommandProcessor
{
    private readonly MeetingStateStore _stateStore;
    private readonly CommandDeduplicator _deduplicator;
    private readonly CommandRateLimiter _rateLimiter;
    private readonly Func<Guid?> _getActiveDeviceIdFunc;

    public CommandProcessor(
        MeetingStateStore stateStore,
        Func<Guid?> getActiveDeviceIdFunc,
        CommandDeduplicator? deduplicator = null,
        CommandRateLimiter? rateLimiter = null)
    {
        _stateStore = stateStore;
        _getActiveDeviceIdFunc = getActiveDeviceIdFunc;
        _deduplicator = deduplicator ?? new CommandDeduplicator();
        _rateLimiter = rateLimiter ?? new CommandRateLimiter();
    }

    public ProtocolEnvelope ProcessEnvelope(ProtocolEnvelope envelope)
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
                // Phase 2 constraint: Remote microphone commands remain strictly disabled
                execResult = new CommandResultPayload
                {
                    Status = "unsupported",
                    StateRevision = _stateStore.StateRevision,
                    ErrorCode = ProtocolErrorCodes.UnsupportedInPhase2,
                    Reason = "Remote microphone control is disabled in Phase 2 proof of transport. Only local companion control is permitted."
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
