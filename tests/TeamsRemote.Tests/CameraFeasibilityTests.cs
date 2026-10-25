using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Classification;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;
using Xunit;

namespace TeamsRemote.Tests;

public class CameraFeasibilityTests
{
    private class CameraTestTeamsController : ITeamsController
    {
        public bool IsRunning { get; set; } = true;
        public bool MeetingDetected { get; set; } = true;
        public bool CameraFound { get; set; } = true;
        public string CurrentCameraState { get; set; } = TeamsControlState.Off;
        public string CurrentMicState { get; set; } = TeamsControlState.Muted;

        public int CameraSubscriptionCreatedCount { get; private set; }
        public int CameraSubscriptionDisposedCount { get; private set; }
        public Action? RegisteredCameraCallback { get; private set; }

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
                    Found = CameraFound,
                    ReadableState = CameraFound ? CurrentCameraState : TeamsControlState.Unknown,
                    CapabilityTier = CameraFound ? ControlCapabilityTier.ObserveOnly : ControlCapabilityTier.Unavailable
                });
            }

            return Task.FromResult(new TeamsDiagnosticReport
            {
                Process = processInfo,
                MeetingSurface = surface,
                Controls = controls,
                DiagnosticSummary = "Camera Feasibility Test Report"
            });
        }

        public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected) => null;

        public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected)
        {
            CameraSubscriptionCreatedCount++;
            RegisteredCameraCallback = onStateChangeDetected;

            return new SubscriptionToken(() =>
            {
                CameraSubscriptionDisposedCount++;
                if (RegisteredCameraCallback == onStateChangeDetected)
                {
                    RegisteredCameraCallback = null;
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
    [InlineData("Turn camera on (Ctrl+Shift+O)", TeamsControlState.Off)]
    [InlineData("Turn on camera", TeamsControlState.Off)]
    [InlineData("Start video", TeamsControlState.Off)]
    [InlineData("Camera is off", TeamsControlState.Off)]
    [InlineData("Video disabled", TeamsControlState.Off)]
    [InlineData("Turn camera off (Ctrl+Shift+O)", TeamsControlState.On)]
    [InlineData("Turn off camera", TeamsControlState.On)]
    [InlineData("Stop video", TeamsControlState.On)]
    [InlineData("Camera is on", TeamsControlState.On)]
    [InlineData("Video enabled", TeamsControlState.On)]
    [InlineData("Share screen", TeamsControlState.Unknown)]
    public void ParseState_FromAccessibleName_ResolvesCameraStateCorrectly(string accessibleName, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Camera,
            accessibleName,
            toggleState: null);

        Assert.Equal(expectedState, state);
        if (expectedState != TeamsControlState.Unknown)
        {
            Assert.Equal("AccessibleName", source);
        }
    }

    [Theory]
    [InlineData("On", TeamsControlState.On)]
    [InlineData("Off", TeamsControlState.Off)]
    [InlineData("Indeterminate", TeamsControlState.Unknown)]
    public void ParseState_FromTogglePattern_ResolvesCameraStateCorrectly(string toggleState, string expectedState)
    {
        var (state, source) = ControlCapabilityClassifier.ParseState(
            TargetControl.Camera,
            accessibleName: null,
            toggleState: toggleState);

        Assert.Equal(expectedState, state);
        if (expectedState != TeamsControlState.Unknown)
        {
            Assert.Equal("TogglePattern", source);
        }
    }

    [Fact]
    public void Classify_CameraCapability_StrictlyObserveOnlyInPhase4B()
    {
        // Even when actionable patterns (Toggle, Invoke) are present and element is enabled,
        // Phase 4B mandates camera capability must remain ObserveOnly.
        var (tier, reason) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Camera,
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.On,
            supportedPatterns: new[] { "TogglePattern", "InvokePattern" });

        Assert.Equal(ControlCapabilityTier.ObserveOnly, tier);
        Assert.Contains("observe-only", reason, StringComparison.OrdinalIgnoreCase);

        // Missing control -> Unavailable
        var (tierMissing, _) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Camera,
            isFound: false,
            isEnabled: false,
            readableState: TeamsControlState.Unknown,
            supportedPatterns: Array.Empty<string>());

        Assert.Equal(ControlCapabilityTier.Unavailable, tierMissing);

        // State unknown -> Unavailable
        var (tierUnknown, _) = ControlCapabilityClassifier.Classify(
            targetControl: TargetControl.Camera,
            isFound: true,
            isEnabled: true,
            readableState: TeamsControlState.Unknown,
            supportedPatterns: new[] { "TogglePattern" });

        Assert.Equal(ControlCapabilityTier.Unavailable, tierUnknown);
    }

    [Fact]
    public void MeetingStateStore_DualControlDeduplication_BehavesCorrectly()
    {
        var store = new MeetingStateStore();
        int changeCount = 0;
        store.StateChanged += () => changeCount++;

        // 1. Initial observation
        var changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed);

        Assert.True(changed);
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(TeamsControlState.Off, store.CameraState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.CameraCapability);
        Assert.Equal(StateConfidence.Confirmed, store.CameraConfidence);
        Assert.Equal(1, changeCount);

        // 2. Duplicate observation (no change in mic or camera) -> No-op
        changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.Off,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed);

        Assert.False(changed);
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(1, changeCount);

        // 3. Camera state changes (Off -> On) -> Monotonic revision increment
        changed = store.UpdateObserved(
            micState: TeamsControlState.Muted,
            cameraState: TeamsControlState.On,
            isConfirmed: true,
            meetingStatus: "active",
            cameraCapability: ControlCapabilityTier.ObserveOnly,
            cameraConfidence: StateConfidence.Confirmed);

        Assert.True(changed);
        Assert.Equal(2, store.StateRevision);
        Assert.Equal(TeamsControlState.On, store.CameraState);
        Assert.Equal(2, changeCount);

        // 4. Full Snapshot query returns all values
        var (rev, micState, micConf, cameraState, cameraCap, cameraConf, meeting) = store.GetFullSnapshot();
        Assert.Equal(2, rev);
        Assert.Equal(TeamsControlState.Muted, micState);
        Assert.Equal(StateConfidence.Confirmed, micConf);
        Assert.Equal(TeamsControlState.On, cameraState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, cameraCap);
        Assert.Equal(StateConfidence.Confirmed, cameraConf);
        Assert.Equal("active", meeting);
    }

    [Fact]
    public async Task PassiveObserver_CameraUiaEvent_DebouncesAndPublishes()
    {
        var controller = new CameraTestTeamsController
        {
            CurrentMicState = TeamsControlState.Muted,
            CurrentCameraState = TeamsControlState.Off
        };
        var store = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(
            controller,
            store,
            debounceDuration: TimeSpan.FromMilliseconds(50),
            pollingInterval: TimeSpan.FromSeconds(10));

        await observer.PollOnceAsync();
        Assert.Equal(1, store.StateRevision);
        Assert.Equal(TeamsControlState.Off, store.CameraState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.CameraCapability);

        // User turns camera on in Teams
        controller.CurrentCameraState = TeamsControlState.On;

        // UIA property change event fires
        observer.OnUiaEventRaised();

        // Wait for debounce window
        await Task.Delay(120);

        Assert.Equal(2, store.StateRevision);
        Assert.Equal(TeamsControlState.On, store.CameraState);
        Assert.Equal(ControlCapabilityTier.ObserveOnly, store.CameraCapability);
        Assert.Equal(StateConfidence.Confirmed, store.CameraConfidence);
    }

    [Fact]
    public async Task PassiveObserver_CameraLifecycle_DisposesOldSubscriptions()
    {
        var controller = new CameraTestTeamsController
        {
            CurrentCameraState = TeamsControlState.Off
        };
        var store = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, store);

        // 1. Initial scan: establishes camera subscription
        await observer.PollOnceAsync();
        Assert.Equal(1, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(0, controller.CameraSubscriptionDisposedCount);

        // 2. Meeting ends: disposes camera subscription
        controller.MeetingDetected = false;
        await observer.PollOnceAsync();
        Assert.Equal(1, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(1, controller.CameraSubscriptionDisposedCount);
        Assert.Equal(TeamsControlState.Unknown, store.CameraState);
        Assert.Equal(ControlCapabilityTier.Unavailable, store.CameraCapability);

        // 3. Meeting resumes: creates replacement subscription cleanly
        controller.MeetingDetected = true;
        controller.CurrentCameraState = TeamsControlState.On;
        await observer.PollOnceAsync();
        Assert.Equal(2, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(1, controller.CameraSubscriptionDisposedCount);
        Assert.Equal(TeamsControlState.On, store.CameraState);

        // 4. Dispose observer: cleans up remaining subscription
        observer.Dispose();
        Assert.Equal(2, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(2, controller.CameraSubscriptionDisposedCount);
    }

    [Fact]
    public async Task CommandProcessor_HandCommand_StrictlyRejectedInPhase4C()
    {
        var stateStore = new MeetingStateStore();
        var devId = Guid.NewGuid();
        var controller = new CameraTestTeamsController();
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
