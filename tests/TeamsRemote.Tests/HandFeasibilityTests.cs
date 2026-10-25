using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class HandFeasibilityTests
{
    private class HandTestTeamsController : ITeamsController
    {
        public bool IsRunning { get; set; } = true;
        public bool MeetingDetected { get; set; } = true;
        public bool HandFound { get; set; } = true;
        public string CurrentHandState { get; set; } = TeamsControlState.Lowered;
        public string CurrentCameraState { get; set; } = TeamsControlState.Off;
        public string CurrentMicState { get; set; } = TeamsControlState.Muted;

        public int HandSubscriptionCreatedCount { get; private set; }
        public int HandSubscriptionDisposedCount { get; private set; }
        public Action? RegisteredHandCallback { get; private set; }

        public Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default)
        {
            var processInfo = new TeamsProcessInfo
            {
                IsRunning = IsRunning,
                ProcessId = IsRunning ? 4321 : null,
                ProcessName = IsRunning ? "ms-teams" : null,
                AllProcessIds = IsRunning ? new[] { 4321 } : Array.Empty<int>()
            };

            var surface = new MeetingSurfaceInfo
            {
                IsDetected = IsRunning && MeetingDetected,
                WindowHandle = (IsRunning && MeetingDetected) ? (nint)8765 : 0,
                RedactedWindowTitle = (IsRunning && MeetingDetected) ? "Teams Meeting" : null
            };

            var controls = new List<ControlDiagnosticInfo>();
            if (IsRunning && MeetingDetected)
            {
                controls.Add(new ControlDiagnosticInfo
                {
                    TargetControl = TargetControl.Microphone,
                    Found = true,
                    ReadableState = CurrentMicState,
                    CapabilityTier = ControlCapabilityTier.Verified
                });

                controls.Add(new ControlDiagnosticInfo
                {
                    TargetControl = TargetControl.Camera,
                    Found = true,
                    ReadableState = CurrentCameraState,
                    CapabilityTier = ControlCapabilityTier.Verified
                });

                controls.Add(new ControlDiagnosticInfo
                {
                    TargetControl = TargetControl.Hand,
                    Found = HandFound,
                    ReadableState = HandFound ? CurrentHandState : TeamsControlState.Unknown,
                    CapabilityTier = HandFound ? ControlCapabilityTier.ObserveOnly : ControlCapabilityTier.Unavailable
                });
            }

            return Task.FromResult(new TeamsDiagnosticReport
            {
                Process = processInfo,
                MeetingSurface = surface,
                Controls = controls,
                DiagnosticSummary = "Hand Feasibility Test Report"
            });
        }

        public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected) => null;

        public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected) => null;

        public IDisposable? SubscribeHandStateChanged(Action onStateChangeDetected)
        {
            HandSubscriptionCreatedCount++;
            RegisteredHandCallback = onStateChangeDetected;

            return new SubscriptionToken(() =>
            {
                HandSubscriptionDisposedCount++;
                if (RegisteredHandCallback == onStateChangeDetected)
                {
                    RegisteredHandCallback = null;
                }
            });
        }

        public Task ObserveEventsAsync(TimeSpan duration, Action<ControlEventReport> onEventObserved, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(DesiredMicrophoneState desiredState, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<CameraCommandResult> SetCameraStateAsync(DesiredCameraState desiredState, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void Dispose() { }

        private class SubscriptionToken : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public SubscriptionToken(Action onDispose) => _onDispose = onDispose;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose();
            }
        }
    }

    [Theory]
    [InlineData("Raise hand", TeamsControlState.Lowered)]
    [InlineData("Raise your hand", TeamsControlState.Lowered)]
    [InlineData("Lower hand", TeamsControlState.Raised)]
    [InlineData("Lower your hand", TeamsControlState.Raised)]
    [InlineData("Hand is raised", TeamsControlState.Raised)]
    [InlineData("Your hand is raised", TeamsControlState.Raised)]
    [InlineData("Hand is lowered", TeamsControlState.Lowered)]
    [InlineData("Your hand is lowered", TeamsControlState.Lowered)]
    public void ParseState_ExplicitHandLabels_IdentifiedCorrectly(string accessibleName, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Hand,
            accessibleName,
            toggleState: null);

        Assert.Equal(expectedState, state);
        Assert.Equal("AccessibleName", source);
    }

    [Theory]
    [InlineData("React and raise hand")]
    [InlineData("react and raise hand")]
    [InlineData("Reactions")]
    [InlineData("Show reactions")]
    [InlineData("React")]
    [InlineData("Chat")]
    [InlineData("People")]
    public void ParseState_GenericReactions_TreatedAsNavigationOnly_NeverGuesses(string accessibleName)
    {
        // CRITICAL REQUIREMENT: Controls such as "React and raise hand" or generic reactions buttons
        // must be treated only as navigation candidates for locating the actual Raise Hand / Lower Hand menu item.
        // State must NEVER be inferred from the generic reactions button.
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Hand,
            accessibleName,
            toggleState: null);

        Assert.Equal(TeamsControlState.Unknown, state);
        Assert.Equal("None", source);
    }

    [Theory]
    [InlineData("On", TeamsControlState.Raised)]
    [InlineData("Off", TeamsControlState.Lowered)]
    [InlineData("Indeterminate", TeamsControlState.Unknown)]
    public void ParseState_FromTogglePattern_ResolvesHandStateCorrectly(string toggleState, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Hand,
            accessibleName: null,
            toggleState: toggleState);

        Assert.Equal(expectedState, state);
        if (expectedState != TeamsControlState.Unknown)
        {
            Assert.Equal("TogglePattern", source);
        }
    }

    [Fact]
    public void Classify_HandCapability_StrictlyObserveOnlyInPhase5A()
    {
        // In Phase 5A, Hand capability MUST strictly be ObserveOnly, NEVER Verified
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Hand,
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Lowered,
            supportedPatterns: new[] { "TogglePattern", "InvokePattern" });

        Assert.Equal(ControlCapabilityTier.ObserveOnly, tier);
        Assert.Contains("observe-only", reason, StringComparison.OrdinalIgnoreCase);

        // Missing control -> Unavailable
        var (tierMissing, _) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Hand,
            isFound: false,
            isEnabled: false,
            readableState: TeamsControlState.Unknown,
            supportedPatterns: Array.Empty<string>());

        Assert.Equal(ControlCapabilityTier.Unavailable, tierMissing);

        // State unknown -> Unavailable
        var (tierUnknown, _) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Hand,
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Unknown,
            supportedPatterns: new[] { "TogglePattern" });

        Assert.Equal(ControlCapabilityTier.Unavailable, tierUnknown);
    }

    [Fact]
    public void MeetingStateStore_TripleControlDeduplication_BehavesCorrectly()
    {
        var store = new MeetingStateStore();
        int changeCount = 0;
        store.StateChanged += () => changeCount++;

        // Initial state is unknown/unavailable
        Assert.Equal(0, store.StateRevision);
        Assert.Equal(TeamsControlState.Unknown, store.HandState);
        Assert.Equal(ControlCapabilityTier.Unavailable, store.HandCapability);
        Assert.Equal(StateConfidence.Unknown, store.HandConfidence);

        // 1. Initial observation
        bool changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed,
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        Assert.True(changed);
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(TeamsControlState.Lowered, store.HandState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.HandCapability);
        Assert.Equal(StateConfidence.Confirmed, store.HandConfidence);
        Assert.Equal(1, changeCount);

        // 2. Duplicate observation (no change across any control) -> No-op
        changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Lowered,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed,
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        Assert.False(changed);
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(1, changeCount);

        // 3. Hand state changes (Lowered -> Raised) -> Monotonic revision increment
        changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            handState: TeamsControlState.Raised,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed,
            handCapability: ControlCapabilityTier.ObserveOnly,
            handConfidence: StateConfidence.Confirmed);

        Assert.True(changed);
        Assert.Equal(2, store.StateRevision);
        Assert.Equal(TeamsControlState.Raised, store.HandState);
        Assert.Equal(2, changeCount);

        // 4. Full Snapshot query returns 10 values
        var (rev, micState, micConf, cameraState, cameraCap, cameraConf, handState, handCap, handConf, meeting) = store.GetFullSnapshot();
        Assert.Equal(2, rev);
        Assert.Equal(TeamsControlState.Muted, micState);
        Assert.Equal(StateConfidence.Confirmed, micConf);
        Assert.Equal(TeamsControlState.Off, cameraState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, cameraCap);
        Assert.Equal(StateConfidence.Confirmed, cameraConf);
        Assert.Equal(TeamsControlState.Raised, handState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, handCap);
        Assert.Equal(StateConfidence.Confirmed, handConf);
        Assert.Equal("active", meeting);

        // 5. MarkUnavailable resets Hand to Unknown and Unavailable
        store.MarkUnavailable();
        Assert.Equal(3, store.StateRevision);
        Assert.Equal(TeamsControlState.Unknown, store.HandState);
        Assert.Equal(ControlCapabilityTier.Unavailable, store.HandCapability);
        Assert.Equal(StateConfidence.Unknown, store.HandConfidence);
    }

    [Fact]
    public async Task PassiveObserver_HandUiaEvent_DebouncesAndPublishes()
    {
        var controller = new HandTestTeamsController
        {
            CurrentMicState = TeamsControlState.Muted,
            CurrentCameraState = TeamsControlState.Off,
            CurrentHandState = TeamsControlState.Lowered
        };
        var store = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(
            controller,
            store,
            debounceDuration: TimeSpan.FromMilliseconds(50),
            pollingInterval: TimeSpan.FromSeconds(10));

        await observer.PollOnceAsync();
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(TeamsControlState.Lowered, store.HandState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.HandCapability);

        // User raises hand in Teams
        controller.CurrentHandState = TeamsControlState.Raised;

        // UIA property change event fires
        observer.OnUiaEventRaised();

        // Wait for debounce window
        await Task.Delay(120);

        Assert.Equal(2, store.StateRevision);
        Assert.Equal(TeamsControlState.Raised, store.HandState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.HandCapability);
        Assert.Equal(StateConfidence.Confirmed, store.HandConfidence);
    }

    [Fact]
    public async Task PassiveObserver_HandLifecycle_DisposesOldSubscriptions()
    {
        var controller = new HandTestTeamsController
        {
            CurrentHandState = TeamsControlState.Lowered
        };
        var store = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, store);

        // 1. Initial scan: establishes hand subscription
        await observer.PollOnceAsync();
        Assert.Equal(1, controller.HandSubscriptionCreatedCount);
        Assert.Equal(0, controller.HandSubscriptionDisposedCount);

        // 2. Meeting ends: disposes hand subscription
        controller.MeetingDetected = false;
        await observer.PollOnceAsync();
        Assert.Equal(1, controller.HandSubscriptionCreatedCount);
        Assert.Equal(1, controller.HandSubscriptionDisposedCount);
        Assert.Equal(TeamsControlState.Unknown, store.HandState);
        Assert.Equal(ControlCapabilityTier.Unavailable, store.HandCapability);

        // 3. Meeting resumes: creates replacement subscription cleanly
        controller.MeetingDetected = true;
        controller.CurrentHandState = TeamsControlState.Raised;
        await observer.PollOnceAsync();
        Assert.Equal(2, controller.HandSubscriptionCreatedCount);
        Assert.Equal(1, controller.HandSubscriptionDisposedCount);
        Assert.Equal(TeamsControlState.Raised, store.HandState);

        // 4. Dispose observer: cleans up remaining subscription
        observer.Dispose();
        Assert.Equal(2, controller.HandSubscriptionCreatedCount);
        Assert.Equal(2, controller.HandSubscriptionDisposedCount);
    }

    [Fact]
    public async Task CommandProcessor_HandCommand_StrictlyRejectedInPhase5A()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var controller = new HandTestTeamsController();
        var processor = new CommandProcessor(stateStore, () => devId, controller);

        var handCmdEnv = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload
            {
                Command = "set_hand",
                Desired = "raised"
            },
            devId);

        var responseEnv = await processor.ProcessEnvelopeAsync(handCmdEnv);
        Assert.NotNull(responseEnv);
        Assert.Equal(ProtocolMessageType.CommandResult, responseEnv.Type);

        var result = responseEnv.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(result);
        Assert.Equal("unsupported", result.Status);
        Assert.Equal(ProtocolErrorCodes.UnsupportedInPhase4A, result.ErrorCode);
    }
}
