using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class CameraRemoteExecutionTests
{
    private class MockTeamsController : ITeamsController
    {
        public int CameraInvocationCount { get; private set; }
        public int MicInvocationCount { get; private set; }
        public DesiredCameraState LastCameraDesired { get; private set; }
        public CommandExecutionStatus StatusToReturn { get; set; } = CommandExecutionStatus.Confirmed;
        public string FinalStateToReturn { get; set; } = TeamsControlState.On;
        public string? ErrorCodeToReturn { get; set; }
        public string ReasonToReturn { get; set; } = "Mock camera execution completed.";
        public bool PromoteToVerifiedToReturn { get; set; } = true;

        public Task<CameraCommandResult> SetCameraStateAsync(
            DesiredCameraState desired,
            CancellationToken cancellationToken = default)
        {
            CameraInvocationCount++;
            LastCameraDesired = desired;
            return Task.FromResult(new CameraCommandResult
            {
                Status = StatusToReturn,
                FinalState = FinalStateToReturn,
                ErrorCode = ErrorCodeToReturn,
                Reason = ReasonToReturn,
                Duration = TimeSpan.FromMilliseconds(50),
                PromoteToVerified = PromoteToVerifiedToReturn
            });
        }

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(
            DesiredMicrophoneState desired,
            CancellationToken cancellationToken = default)
        {
            MicInvocationCount++;
            return Task.FromResult(new MicrophoneCommandResult
            {
                Status = CommandExecutionStatus.Confirmed,
                FinalState = desired.ToWireString(),
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
        public void Dispose() { }
    }

    [Fact]
    public async Task SetCamera_ExactRevisionMatch_ExecutesAndPromotesToVerified()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly);

        var initialRev = stateStore.StateRevision;
        Assert.Equal(ControlCapabilityTier.ObserveOnly, stateStore.CameraCapability);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Confirmed,
            FinalStateToReturn = TeamsControlState.On,
            PromoteToVerifiedToReturn = true
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)initialRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(initialRev + 1, payload.StateRevision);
        Assert.Equal("on", payload.State?["camera"]);
        Assert.Equal("confirmed", payload.State?["confidence"]);
        Assert.Equal("verified", payload.State?["capability"]);
        Assert.Equal(1, mockController.CameraInvocationCount);
        Assert.Equal(DesiredCameraState.On, mockController.LastCameraDesired);

        // Authoritative store state has been promoted to Verified
        Assert.Equal(ControlCapabilityTier.Verified, stateStore.CameraCapability);
        Assert.Equal(TeamsControlState.On, stateStore.CameraState);
    }

    [Fact]
    public async Task SetCamera_LowerRevision_ReturnsRejectedStaleCommand()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly);

        // Advance revision
        stateStore.UpdateObserved(
            micState: TeamsControlState.Unmuted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly);

        var currentRev = stateStore.StateRevision;
        var staleRev = currentRev - 1;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)staleRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.StaleCommand, payload.ErrorCode);
        Assert.Equal(0, mockController.CameraInvocationCount);
    }

    [Fact]
    public async Task SetCamera_HigherRevision_ReturnsRejectedInvalidStateRevision()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active");

        var currentRev = stateStore.StateRevision;
        var futureRev = currentRev + 5;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)futureRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidStateRevision, payload.ErrorCode);
        Assert.Equal(0, mockController.CameraInvocationCount);
    }

    [Fact]
    public async Task SetCamera_AlreadyInDesiredState_ReturnsConfirmedWithoutInvoke()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.On,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly);

        var currentRev = stateStore.StateRevision;

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)currentRev
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Equal(0, mockController.CameraInvocationCount);
        Assert.Equal(currentRev, payload.StateRevision);
    }

    [Fact]
    public async Task SetCamera_InvalidDesiredState_ReturnsRejected()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "half_open",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidDesiredState, payload.ErrorCode);
        Assert.Equal(0, mockController.CameraInvocationCount);
    }

    [Fact]
    public async Task SetCamera_NoPrematurePromotion_InvokeTimeoutRemainsObserveOnly()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly);

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.TimedOut,
            FinalStateToReturn = TeamsControlState.Off,
            ErrorCodeToReturn = ProtocolErrorCodes.VerificationTimedOut,
            ReasonToReturn = "Verification timed out.",
            PromoteToVerifiedToReturn = false
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId);

        var resultEnv = await processor.ProcessEnvelopeAsync(env);
        var payload = resultEnv.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("state_uncertain", payload.Status);
        Assert.Equal(ProtocolErrorCodes.VerificationTimedOut, payload.ErrorCode);
        // Crucial invariant: never promoted to verified on verification failure
        Assert.Equal(ControlCapabilityTier.ObserveOnly, stateStore.CameraCapability);
    }

    [Fact]
    public async Task SetCamera_PreActionEligibility_GatedInCoordinator()
    {
        var coordinator = new CameraActionCoordinator();

        // 1. Gated when control is disabled
        var disabledResult = await coordinator.ExecuteAsync(
            DesiredCameraState.On,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Off,
                hasPattern: true,
                isEnabled: false,
                invokeAction: (Action)(() => { }),
                fingerprint: (string?)"fp1")),
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Off),
            verificationTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Equal(CommandExecutionStatus.Unsupported, disabledResult.Status);
        Assert.False(disabledResult.PromoteToVerified);

        // 2. Gated when state is unknown/unreadable
        var unknownResult = await coordinator.ExecuteAsync(
            DesiredCameraState.On,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Unknown,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => { }),
                fingerprint: (string?)"fp1")),
            reQueryStateFunc: () => Task.FromResult(TeamsControlState.Unknown),
            verificationTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Equal(CommandExecutionStatus.StateUncertain, unknownResult.Status);
        Assert.False(unknownResult.PromoteToVerified);

        // 3. Action-eligible: executes and verifies successfully
        bool invoked = false;
        var successResult = await coordinator.ExecuteAsync(
            DesiredCameraState.On,
            controlProvider: () => Task.FromResult((
                found: true,
                currentState: TeamsControlState.Off,
                hasPattern: true,
                isEnabled: true,
                invokeAction: (Action)(() => { invoked = true; }),
                fingerprint: (string?)"fp1")),
            reQueryStateFunc: () => Task.FromResult(invoked ? TeamsControlState.On : TeamsControlState.Off),
            verificationTimeout: TimeSpan.FromMilliseconds(500));

        Assert.True(invoked);
        Assert.Equal(CommandExecutionStatus.Confirmed, successResult.Status);
        Assert.True(successResult.PromoteToVerified);
    }

    [Fact]
    public async Task SetCamera_DuplicateMessageId_ReturnsCachedResultWithoutReInvoke()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            FinalStateToReturn = TeamsControlState.On,
            PromoteToVerifiedToReturn = true
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var msgId = Guid.NewGuid();
        var env1 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId,
            messageId: msgId);

        var env2 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId,
            messageId: msgId);

        var res1 = await processor.ProcessEnvelopeAsync(env1);
        var res2 = await processor.ProcessEnvelopeAsync(env2);

        Assert.Equal(1, mockController.CameraInvocationCount);
        Assert.Equal(res1.DeserializePayload<CommandResultPayload>()?.StateRevision,
                     res2.DeserializePayload<CommandResultPayload>()?.StateRevision);
    }

    [Fact]
    public async Task SetCamera_MicrophoneIsolation_NoInterference()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        // Command to camera does not invoke microphone
        var camEnv = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_camera",
                Desired = "on",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId);

        await processor.ProcessEnvelopeAsync(camEnv);
        Assert.Equal(1, mockController.CameraInvocationCount);
        Assert.Equal(0, mockController.MicInvocationCount);

        // Command to microphone does not invoke camera
        var micEnv = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_microphone",
                Desired = "unmuted",
                ExpectedStateRevision = (int)stateStore.StateRevision
            },
            devId);

        await processor.ProcessEnvelopeAsync(micEnv);
        Assert.Equal(1, mockController.CameraInvocationCount);
        Assert.Equal(1, mockController.MicInvocationCount);
    }

    [Fact]
    public async Task SetHand_StillRejectedAsUnsupported()
    {
        var stateStore = new MeetingStateStore();
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
        Assert.Equal("unsupported", payload.Status);
    }

    [Fact]
    public void SetCamera_LifecycleDemotion_ReResolutionResetsCapabilityToObserveOnly()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.On,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.Verified);

        Assert.Equal(ControlCapabilityTier.Verified, stateStore.CameraCapability);
        var revBeforeReset = stateStore.StateRevision;

        // Simulate toolbar re-resolution / meeting redraw
        bool changed = stateStore.ResetCameraCapabilityToObserveOnly();

        Assert.True(changed);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, stateStore.CameraCapability);
        Assert.Equal(revBeforeReset + 1, stateStore.StateRevision);
    }
}
