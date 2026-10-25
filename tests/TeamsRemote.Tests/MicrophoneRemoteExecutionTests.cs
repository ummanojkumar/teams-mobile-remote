using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class MicrophoneRemoteExecutionTests
{
    private class MockTeamsController : ITeamsController
    {
        public int InvocationCount { get; private set; }
        public DesiredMicrophoneState LastDesired { get; private set; }
        public CommandExecutionStatus StatusToReturn { get; set; } = CommandExecutionStatus.Confirmed;
        public string FinalStateToReturn { get; set; } = TeamsControlState.Muted;
        public string? ErrorCodeToReturn { get; set; }
        public string ReasonToReturn { get; set; } = "Mock execution completed.";

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(
            DesiredMicrophoneState desired,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            LastDesired = desired;
            return Task.FromResult(new MicrophoneCommandResult
            {
                Status = StatusToReturn,
                FinalState = FinalStateToReturn,
                ErrorCode = ErrorCodeToReturn,
                Reason = ReasonToReturn,
                Duration = TimeSpan.FromMilliseconds(50)
            });
        }

        public Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task ObserveEventsAsync(TimeSpan duration, Action<ControlEventReport> onEventObserved, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected) => null;

        public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected) => null;

        public IDisposable? SubscribeHandStateChanged(Action onStateChangeDetected) => null;

        public Task<CameraCommandResult> SetCameraStateAsync(DesiredCameraState desired, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<HandCommandResult> SetHandStateAsync(DesiredHandState desired, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void Dispose() { }
    }

    [Fact]
    public async Task SetMicrophone_ValidUnmutedToMuted_ReturnsConfirmed()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true, meetingStatus: "active");
        var initialRev = stateStore.StateRevision;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Confirmed,
            FinalStateToReturn = TeamsControlState.Muted,
            ReasonToReturn = "Microphone muted and verified."
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_microphone",
                Desired = "muted",
                ExpectedStateRevision = (int)initialRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(initialRev + 1, payload.StateRevision);
        Assert.Equal("muted", payload.State?["microphone"]);
        Assert.Equal("confirmed", payload.State?["confidence"]);
        Assert.Equal(1, mockController.InvocationCount);
        Assert.Equal(DesiredMicrophoneState.Muted, mockController.LastDesired);
    }

    [Fact]
    public async Task SetMicrophone_AlreadyInDesiredState_ReturnsConfirmedWithoutInvoke()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(TeamsControlState.Muted, isConfirmed: true, meetingStatus: "active");
        var initialRev = stateStore.StateRevision;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_microphone",
                Desired = "muted",
                ExpectedStateRevision = (int)initialRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(initialRev, payload.StateRevision);
        Assert.Contains("already muted", payload.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, mockController.InvocationCount); // Teams never invoked
    }

    [Fact]
    public async Task SetMicrophone_StaleLowerRevision_ReturnsRejectedStaleCommand()
    {
        var stateStore = new MeetingStateStore();
        // Advance revision to 5
        for (int i = 0; i < 5; i++)
        {
            stateStore.UpdateObserved(i % 2 == 0 ? TeamsControlState.Unmuted : TeamsControlState.Muted, isConfirmed: true, meetingStatus: "active");
        }
        Assert.Equal(5, stateStore.StateRevision);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        // Client expected revision 4 (< 5)
        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_microphone",
                Desired = "muted",
                ExpectedStateRevision = 4
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.StaleCommand, payload.ErrorCode);
        Assert.Equal(5, payload.StateRevision);
        Assert.Equal(0, mockController.InvocationCount); // UIA never invoked
    }

    [Fact]
    public async Task SetMicrophone_InvalidHigherRevision_ReturnsRejectedInvalidStateRevision()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true, meetingStatus: "active");
        var currentRev = stateStore.StateRevision;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        // Client sends higher revision (rev + 10)
        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_microphone",
                Desired = "muted",
                ExpectedStateRevision = (int)currentRev + 10
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidStateRevision, payload.ErrorCode);
        Assert.Equal(currentRev, payload.StateRevision);
        Assert.Equal(0, mockController.InvocationCount); // UIA never invoked
    }

    [Fact]
    public async Task SetMicrophone_Deduplication_ReturnsCachedResultWithoutReinvoking()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true, meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            FinalStateToReturn = TeamsControlState.Muted
        };
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var msgId = Guid.NewGuid();
        var env1 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_microphone", Desired = "muted", ExpectedStateRevision = 1 },
            devId,
            correlationId: null,
            messageId: msgId);

        var res1 = await processor.ProcessEnvelopeAsync(env1);
        Assert.Equal(1, mockController.InvocationCount);

        // Resend identical messageId (retry simulation)
        var env2 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_microphone", Desired = "muted", ExpectedStateRevision = 1 },
            devId,
            correlationId: null,
            messageId: msgId);

        var res2 = await processor.ProcessEnvelopeAsync(env2);
        Assert.Equal(1, mockController.InvocationCount); // Still 1

        var payload1 = res1.DeserializePayload<CommandResultPayload>();
        var payload2 = res2.DeserializePayload<CommandResultPayload>();
        Assert.Equal(payload1?.Status, payload2?.Status);
        Assert.Equal(payload1?.StateRevision, payload2?.StateRevision);
    }

    [Fact]
    public async Task SetMicrophone_InvalidDesiredState_ReturnsRejected()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_microphone", Desired = "toggle" },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidDesiredState, payload.ErrorCode);
        Assert.Equal(0, mockController.InvocationCount);
    }

    [Fact]
    public async Task Hand_UnknownState_RefusedWithStateUncertain()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        // Hand is unknown/unavailable initially, so action is refused
        var handEnv = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_hand", Desired = "raised" },
            devId);
        var handRes = await processor.ProcessEnvelopeAsync(handEnv);
        var handPayload = handRes.DeserializePayload<CommandResultPayload>();
        Assert.Equal("state_uncertain", handPayload?.Status);
        Assert.Equal("STATE_UNKNOWN", handPayload?.ErrorCode);
    }

    [Fact]
    public async Task SetMicrophone_VerificationTimeout_ReturnsStateUncertain()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(TeamsControlState.Unmuted, isConfirmed: true, meetingStatus: "active");
        var devId = Guid.NewGuid();

        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.TimedOut,
            ErrorCodeToReturn = ProtocolErrorCodes.VerificationTimedOut,
            ReasonToReturn = "Verification timed out after 2000ms."
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_microphone", Desired = "muted", ExpectedStateRevision = 1 },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("state_uncertain", payload.Status);
        Assert.Equal(ProtocolErrorCodes.VerificationTimedOut, payload.ErrorCode);
        Assert.Equal(StateConfidence.Stale, stateStore.Confidence);
    }

    [Fact]
    public async Task ProcessEnvelope_UnauthorizedDevice_ReturnsPairingRequired()
    {
        var stateStore = new MeetingStateStore();
        var pairedDevId = Guid.NewGuid();
        var attackerDevId = Guid.NewGuid();

        var processor = new CommandProcessor(stateStore, () => pairedDevId);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_microphone", Desired = "muted" },
            attackerDevId); // Mismatched device ID

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.PairingRequired, payload.ErrorCode);
    }
}
