using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class HandRemoteExecutionTests
{
    private class MockTeamsController : ITeamsController
    {
        public CommandExecutionStatus StatusToReturn { get; set; } = CommandExecutionStatus.Confirmed;
        public string FinalStateToReturn { get; set; } = TeamsControlState.Raised;
        public string ReasonToReturn { get; set; } = "Hand state changed and verified.";
        public string? ErrorCodeToReturn { get; set; }
        public bool PromoteToVerifiedToReturn { get; set; } = true;

        public int SetHandCallCount { get; private set; }
        public DesiredHandState LastDesiredHand { get; private set; }

        public Task<HandCommandResult> SetHandStateAsync(DesiredHandState desired, CancellationToken cancellationToken = default)
        {
            SetHandCallCount++;
            LastDesiredHand = desired;

            return Task.FromResult(new HandCommandResult
            {
                Status = StatusToReturn,
                FinalState = FinalStateToReturn,
                Reason = ReasonToReturn,
                ErrorCode = ErrorCodeToReturn,
                Duration = TimeSpan.FromMilliseconds(50),
                PromoteToVerified = PromoteToVerifiedToReturn
            });
        }

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(DesiredMicrophoneState desiredState, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<CameraCommandResult> SetCameraStateAsync(DesiredCameraState desired, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task ObserveEventsAsync(TimeSpan duration, Action<ControlEventReport> onEventObserved, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected) => null;
        public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected) => null;
        public IDisposable? SubscribeHandStateChanged(Action onStateChangeDetected) => null;
        public Task<ReactionDiagnosticReport> ObserveReactionsAsync(TimeSpan duration, Action<string>? onProgressMessage = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReactionDiagnosticReport());
        public Task<ReactionCommandResult> SendReactionAsync(string reaction, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public void Dispose() { }
    }

    [Fact]
    public async Task SetHand_ExactRevisionMatch_ExecutesAndPromotesToVerified()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed,
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        var initialRev = stateStore.StateRevision;
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Confirmed,
            FinalStateToReturn = TeamsControlState.Raised,
            PromoteToVerifiedToReturn = true
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised",
                ExpectedStateRevision = initialRev
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        Assert.NotNull(res);
        Assert.Equal(ProtocolMessageType.CommandResult, res.Type);

        var payload = res.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(1, mockController.SetHandCallCount);
        Assert.Equal(DesiredHandState.Raised, mockController.LastDesiredHand);

        // Verification promotes capability to verified and advances revision monotonically
        Assert.Equal(ControlCapabilityTier.Verified, stateStore.HandCapability);
        Assert.Equal(TeamsControlState.Raised, stateStore.HandState);
        Assert.Equal(initialRev + 1, stateStore.StateRevision);
    }

    [Fact]
    public async Task SetHand_StaleRevision_RejectsWithStaleCommand()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active");

        // Advance revision to 2
        stateStore.UpdateObserved(
            micState: TeamsControlState.Unmuted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active");

        Assert.Equal(2, stateStore.StateRevision);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised",
                ExpectedStateRevision = 1 // Stale revision
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.StaleCommand, payload.ErrorCode);
        Assert.Equal(0, mockController.SetHandCallCount);
        Assert.Equal(2, stateStore.StateRevision);
    }

    [Fact]
    public async Task SetHand_FutureRevision_RejectsWithInvalidRevision()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active");

        Assert.Equal(1, stateStore.StateRevision);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised",
                ExpectedStateRevision = 99 // Future revision
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidStateRevision, payload.ErrorCode);
        Assert.Equal(0, mockController.SetHandCallCount);
        Assert.Equal(1, stateStore.StateRevision);
    }

    [Fact]
    public async Task SetHand_AlreadyInDesiredState_IdempotentConfirmed()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Raised,
            isConfirmed: true,
            meetingStatus: "active");

        var initialRev = stateStore.StateRevision;
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised",
                ExpectedStateRevision = initialRev
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(0, mockController.SetHandCallCount); // Zero UIA invocations
        Assert.Equal(initialRev, stateStore.StateRevision); // Revision untouched
    }

    [Fact]
    public async Task SetHand_UnknownCurrentState_ReturnsStateUncertain()
    {
        var stateStore = new MeetingStateStore(); // Hand is Unknown / Unavailable
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised"
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("state_uncertain", payload.Status);
        Assert.Equal(ProtocolErrorCodes.StateUnknown, payload.ErrorCode);
        Assert.Equal(0, mockController.SetHandCallCount);
    }

    [Fact]
    public async Task SetHand_InvalidDesiredState_ReturnsRejected()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "clap" // Invalid
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidDesiredState, payload.ErrorCode);
        Assert.Equal(0, mockController.SetHandCallCount);
    }

    [Fact]
    public async Task SetHand_VerificationTimeout_ReturnsStateUncertain_MarksConfidenceStale()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active",
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        var initialRev = stateStore.StateRevision;
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.TimedOut,
            FinalStateToReturn = TeamsControlState.Lowered,
            ReasonToReturn = "Verification timed out.",
            PromoteToVerifiedToReturn = false
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised",
                ExpectedStateRevision = initialRev
            },
            devId);

        var res = await processor.ProcessEnvelopeAsync(env);
        var payload = res.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("state_uncertain", payload.Status);
        Assert.Equal(ProtocolErrorCodes.VerificationTimedOut, payload.ErrorCode);
        Assert.Equal(StateConfidence.Stale, stateStore.HandConfidence);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, stateStore.HandCapability);
    }

    [Fact]
    public void SetHand_LifecycleDemotion_ReResolutionResetsCapabilityToObserveOnly()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Raised,
            isConfirmed: true,
            meetingStatus: "active",
            handCapability: ControlCapabilityTier.Verified);

        Assert.Equal(ControlCapabilityTier.Verified, stateStore.HandCapability);
        var revBeforeReset = stateStore.StateRevision;

        // Simulate toolbar re-resolution / meeting redraw
        bool changed = stateStore.ResetHandCapabilityToObserveOnly();

        Assert.True(changed);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, stateStore.HandCapability);
        Assert.Equal(revBeforeReset + 1, stateStore.StateRevision);
    }

    [Fact]
    public async Task SetHand_Deduplication_ReturnsCachedResultWithoutReinvoking()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active",
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Confirmed,
            FinalStateToReturn = TeamsControlState.Raised,
            ReasonToReturn = "Hand raised successfully.",
            PromoteToVerifiedToReturn = true
        };
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var msgId = Guid.NewGuid();
        var corrId = Guid.NewGuid();
        var env1 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_hand", Desired = "raised", ExpectedStateRevision = 1 },
            devId,
            correlationId: corrId,
            messageId: msgId);

        var res1 = await processor.ProcessEnvelopeAsync(env1);
        Assert.Equal(1, mockController.SetHandCallCount);
        Assert.Equal(corrId, res1.CorrelationId);
        Assert.Equal(msgId, res1.MessageId);

        // Resend identical messageId (retry simulation)
        var env2 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "set_hand", Desired = "raised", ExpectedStateRevision = 1 },
            devId,
            correlationId: corrId,
            messageId: msgId);

        var res2 = await processor.ProcessEnvelopeAsync(env2);
        Assert.Equal(1, mockController.SetHandCallCount); // Still 1: cached result returned without re-invoking!
        Assert.Equal(corrId, res2.CorrelationId);
        Assert.Equal(msgId, res2.MessageId);

        var payload1 = res1.DeserializePayload<CommandResultPayload>();
        var payload2 = res2.DeserializePayload<CommandResultPayload>();
        Assert.Equal(payload1?.Status, payload2?.Status);
        Assert.Equal(payload1?.StateRevision, payload2?.StateRevision);
    }
}
