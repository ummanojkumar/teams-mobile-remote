using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Security;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class ReactionRemoteExecutionTests
{
    private class MockTeamsController : ITeamsController
    {
        public CommandExecutionStatus StatusToReturn { get; set; } = CommandExecutionStatus.Confirmed;
        public string? ErrorCodeToReturn { get; set; }
        public string ReasonToReturn { get; set; } = "Reaction sent successfully.";

        public int SendReactionCallCount { get; private set; }
        public string? LastReactionSent { get; private set; }

        public Task<ReactionCommandResult> SendReactionAsync(string reaction, CancellationToken cancellationToken = default)
        {
            SendReactionCallCount++;
            LastReactionSent = reaction;

            return Task.FromResult(new ReactionCommandResult
            {
                Status = StatusToReturn,
                Reaction = reaction,
                Reason = ReasonToReturn,
                ErrorCode = ErrorCodeToReturn,
                Duration = TimeSpan.FromMilliseconds(45)
            });
        }

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(DesiredMicrophoneState desiredState, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<CameraCommandResult> SetCameraStateAsync(DesiredCameraState desired, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<HandCommandResult> SetHandStateAsync(DesiredHandState desired, CancellationToken cancellationToken = default) =>
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
        public void Dispose() { }
    }

    [Fact]
    public async Task SendReaction_ValidReaction_ExecutesSuccessfullyAndReturnsConfirmedResult()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active");

        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Confirmed
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var msgId = Guid.NewGuid();
        var corrId = Guid.NewGuid();
        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = ReactionTypes.Like
            },
            messageId: msgId,
            correlationId: corrId,
            deviceId: devId);

        var res = await processor.ProcessEnvelopeAsync(env);

        Assert.NotNull(res);
        Assert.Equal(ProtocolMessageType.CommandResult, res.Type);
        Assert.Equal(corrId, res.CorrelationId);

        var payload = res.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload);
        Assert.Equal("confirmed", payload.Status);
        Assert.Null(payload.ErrorCode);
        Assert.Equal(1, mockController.SendReactionCallCount);
        Assert.Equal(ReactionTypes.Like, mockController.LastReactionSent);
        Assert.NotNull(payload.State);
        Assert.True(payload.State.ContainsKey("reaction"));
        Assert.Equal(ReactionTypes.Like, payload.State["reaction"]);
    }

    [Theory]
    [InlineData("thumbsup")]
    [InlineData("dislike")]
    [InlineData("")]
    [InlineData("heart")]
    [InlineData("angry")]
    public async Task SendReaction_InvalidReaction_RejectedWithInvalidDesiredState(string invalidReaction)
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = invalidReaction
            },
            deviceId: devId);

        var res = await processor.ProcessEnvelopeAsync(env);

        Assert.NotNull(res);
        var payload = res.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.InvalidDesiredState, payload.ErrorCode);
        Assert.Equal(0, mockController.SendReactionCallCount);
    }

    [Fact]
    public async Task SendReaction_RateLimited_RejectedWithRateLimited()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var rateLimiter = new ReactionRateLimiter(
            perDeviceCooldown: TimeSpan.FromSeconds(2),
            maxBurstCount: 3,
            burstWindow: TimeSpan.FromSeconds(10),
            globalCooldown: TimeSpan.FromMilliseconds(500));

        var processor = new CommandProcessor(
            stateStore,
            () => devId,
            mockController,
            reactionRateLimiter: rateLimiter);

        // First attempt succeeds
        var env1 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = ReactionTypes.Love
            },
            deviceId: devId);

        var res1 = await processor.ProcessEnvelopeAsync(env1);
        var payload1 = res1.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload1);
        Assert.Equal("confirmed", payload1.Status);
        Assert.Equal(1, mockController.SendReactionCallCount);

        // Immediate second attempt from same device is rate limited
        var env2 = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = ReactionTypes.Applause
            },
            deviceId: devId);

        var res2 = await processor.ProcessEnvelopeAsync(env2);
        var payload2 = res2.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload2);
        Assert.Equal("rejected", payload2.Status);
        Assert.Equal(ProtocolErrorCodes.RateLimited, payload2.ErrorCode);
        Assert.Contains("cooldown active", payload2.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, mockController.SendReactionCallCount); // Not called second time
    }

    [Fact]
    public async Task SendReaction_PolicyRestricted_RejectedWithPolicyRestricted()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController
        {
            StatusToReturn = CommandExecutionStatus.Rejected,
            ErrorCodeToReturn = ProtocolErrorCodes.PolicyRestricted,
            ReasonToReturn = "Reactions are disabled by meeting policy."
        };

        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = ReactionTypes.Surprise
            },
            deviceId: devId);

        var res = await processor.ProcessEnvelopeAsync(env);

        Assert.NotNull(res);
        var payload = res.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(payload);
        Assert.Equal("rejected", payload.Status);
        Assert.Equal(ProtocolErrorCodes.PolicyRestricted, payload.ErrorCode);
        Assert.Equal("Reactions are disabled by meeting policy.", payload.Reason);
    }

    [Fact]
    public async Task SendReaction_DuplicateMessageId_DeduplicatedWithoutCallingControllerAgain()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var mockController = new MockTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, mockController);

        var msgId = Guid.NewGuid();
        var env = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "send_reaction",
                Reaction = ReactionTypes.Laugh
            },
            messageId: msgId,
            deviceId: devId);

        var res1 = await processor.ProcessEnvelopeAsync(env);
        var res2 = await processor.ProcessEnvelopeAsync(env);

        Assert.NotNull(res1);
        Assert.NotNull(res2);
        Assert.Equal(1, mockController.SendReactionCallCount);
        Assert.Equal(res1.MessageId, res2.MessageId);
        Assert.Equal(res1.CorrelationId, res2.CorrelationId);
    }

    [Fact]
    public async Task SendReaction_DoesNotAffectMeetingStateRevision()
    {
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
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
                Command = "send_reaction",
                Reaction = ReactionTypes.Applause
            },
            deviceId: devId);

        await processor.ProcessEnvelopeAsync(env);

        Assert.Equal(initialRev, stateStore.StateRevision);
    }
}
