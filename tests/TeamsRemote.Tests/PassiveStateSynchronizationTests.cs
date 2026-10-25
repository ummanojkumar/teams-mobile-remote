using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Models;
using TeamsRemote.Core.State;
using Xunit;

namespace TeamsRemote.Tests;

public class PassiveStateSynchronizationTests
{
    private class TestTeamsController : ITeamsController
    {
        public bool IsRunning { get; set; } = true;
        public bool MeetingDetected { get; set; } = true;
        public bool MicFound { get; set; } = true;
        public string CurrentMicState { get; set; } = TeamsControlState.Muted;

        public int MicSubscriptionCreatedCount { get; private set; }
        public int MicSubscriptionDisposedCount { get; private set; }
        public int CameraSubscriptionCreatedCount { get; private set; }
        public int CameraSubscriptionDisposedCount { get; private set; }

        public int SubscriptionCreatedCount => MicSubscriptionCreatedCount + CameraSubscriptionCreatedCount;
        public int SubscriptionDisposedCount => MicSubscriptionDisposedCount + CameraSubscriptionDisposedCount;

        public Action? RegisteredUiaCallback { get; private set; }

        public Task<TeamsDiagnosticReport> InspectAsync(CancellationToken cancellationToken = default)
        {
            var processInfo = new TeamsProcessInfo
            {
                IsRunning = IsRunning,
                ProcessId = IsRunning ? 1234 : null,
                ProcessName = IsRunning ? "ms-teams" : null,
                AllProcessIds = IsRunning ? new[] { 1234 } : Array.Empty<int>()
            };

            var surface = new MeetingSurfaceInfo
            {
                IsDetected = IsRunning && MeetingDetected,
                WindowHandle = (IsRunning && MeetingDetected) ? (nint)5678 : 0,
                RedactedWindowTitle = (IsRunning && MeetingDetected) ? "Meeting Window" : null
            };

            var controls = new List<ControlDiagnosticInfo>();
            if (IsRunning && MeetingDetected)
            {
                controls.Add(new ControlDiagnosticInfo
                {
                    TargetControl = TargetControl.Microphone,
                    Found = MicFound,
                    ReadableState = MicFound ? CurrentMicState : TeamsControlState.Unknown,
                    CapabilityTier = ControlCapabilityTier.Verified
                });
            }

            return Task.FromResult(new TeamsDiagnosticReport
            {
                Process = processInfo,
                MeetingSurface = surface,
                Controls = controls,
                DiagnosticSummary = "Test report"
            });
        }

        public IDisposable? SubscribeMicrophoneStateChanged(Action onStateChangeDetected)
        {
            MicSubscriptionCreatedCount++;
            RegisteredUiaCallback = onStateChangeDetected;

            return new SubscriptionToken(() =>
            {
                MicSubscriptionDisposedCount++;
                if (RegisteredUiaCallback == onStateChangeDetected)
                {
                    RegisteredUiaCallback = null;
                }
            });
        }

        public IDisposable? SubscribeCameraStateChanged(Action onStateChangeDetected)
        {
            CameraSubscriptionCreatedCount++;
            return new SubscriptionToken(() =>
            {
                CameraSubscriptionDisposedCount++;
            });
        }

        public Task ObserveEventsAsync(TimeSpan duration, Action<ControlEventReport> onEventObserved, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<MicrophoneCommandResult> SetMicrophoneStateAsync(DesiredMicrophoneState desiredState, CancellationToken cancellationToken = default) =>
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

    [Fact]
    public async Task UiaEvent_StateChange_DebouncedAndPublished()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(
            controller,
            stateStore,
            debounceDuration: TimeSpan.FromMilliseconds(50),
            pollingInterval: TimeSpan.FromSeconds(10));

        // Initial scan
        await observer.PollOnceAsync();
        Assert.Equal(TeamsControlState.Muted, stateStore.MicrophoneState);
        Assert.Equal(1, stateStore.StateRevision);

        int stateChangeFired = 0;
        stateStore.StateChanged += () => stateChangeFired++;

        // User toggles mic directly in Teams desktop
        controller.CurrentMicState = TeamsControlState.Unmuted;

        // UIA event fires
        observer.OnUiaEventRaised();

        // Wait for debounce window (50ms + margin)
        await Task.Delay(120);

        Assert.Equal(TeamsControlState.Unmuted, stateStore.MicrophoneState);
        Assert.Equal(StateConfidence.Confirmed, stateStore.Confidence);
        Assert.Equal(2, stateStore.StateRevision);
        Assert.Equal(1, stateChangeFired);
    }

    [Fact]
    public async Task MissedEvent_RecoveredThroughPollingFallback()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(
            controller,
            stateStore,
            debounceDuration: TimeSpan.FromMilliseconds(50),
            pollingInterval: TimeSpan.FromMilliseconds(100));

        await observer.PollOnceAsync();
        Assert.Equal(1, stateStore.StateRevision);

        int stateChangeFired = 0;
        stateStore.StateChanged += () => stateChangeFired++;

        // Teams state changed, but no UIA event fired (dropped event)
        controller.CurrentMicState = TeamsControlState.Unmuted;

        // Trigger polling recovery
        var changed = await observer.PollOnceAsync();

        Assert.True(changed);
        Assert.Equal(TeamsControlState.Unmuted, stateStore.MicrophoneState);
        Assert.Equal(2, stateStore.StateRevision);
        Assert.Equal(1, stateChangeFired);
    }

    [Fact]
    public async Task NoDuplicateRevisionOrBroadcast_WhenStateUnchanged()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(
            controller,
            stateStore,
            debounceDuration: TimeSpan.FromMilliseconds(50),
            pollingInterval: TimeSpan.FromMilliseconds(100));

        await observer.PollOnceAsync();
        Assert.Equal(1, stateStore.StateRevision);

        int stateChangeFired = 0;
        stateStore.StateChanged += () => stateChangeFired++;

        // Repeated polling ticks with identical state
        for (int i = 0; i < 5; i++)
        {
            var changed = await observer.PollOnceAsync();
            Assert.False(changed);
        }

        // Also UIA event with unchanged state
        observer.OnUiaEventRaised();
        await Task.Delay(100);

        // Verification: Revision must NOT increment and StateChanged must NOT fire
        Assert.Equal(1, stateStore.StateRevision);
        Assert.Equal(TeamsControlState.Muted, stateStore.MicrophoneState);
        Assert.Equal(0, stateChangeFired);
    }

    [Fact]
    public async Task TeamsCloses_ImmediatelyPublishesUnavailableUnknown()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, stateStore);

        await observer.PollOnceAsync();
        Assert.Equal(TeamsControlState.Muted, stateStore.MicrophoneState);
        Assert.Equal(1, stateStore.StateRevision);

        // Teams process exits
        controller.IsRunning = false;

        var changed = await observer.PollOnceAsync();

        Assert.True(changed);
        Assert.Equal(TeamsControlState.Unknown, stateStore.MicrophoneState);
        Assert.Equal(StateConfidence.Unknown, stateStore.Confidence);
        Assert.Equal("none", stateStore.MeetingStatus);
        Assert.Equal(2, stateStore.StateRevision);
    }

    [Fact]
    public async Task MeetingDisappears_ImmediatelyPublishesUnavailableUnknown()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Unmuted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, stateStore);

        await observer.PollOnceAsync();
        Assert.Equal(TeamsControlState.Unmuted, stateStore.MicrophoneState);

        // Meeting ends while Teams process remains running
        controller.MeetingDetected = false;

        var changed = await observer.PollOnceAsync();

        Assert.True(changed);
        Assert.Equal(TeamsControlState.Unknown, stateStore.MicrophoneState);
        Assert.Equal(StateConfidence.Unknown, stateStore.Confidence);
        Assert.Equal("none", stateStore.MeetingStatus);
    }

    [Fact]
    public async Task MeetingReopens_RecoversPassiveUpdates()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted, MeetingDetected = false };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, stateStore);

        await observer.PollOnceAsync();
        Assert.Equal(TeamsControlState.Unknown, stateStore.MicrophoneState);

        // User starts a meeting with microphone unmuted
        controller.MeetingDetected = true;
        controller.CurrentMicState = TeamsControlState.Unmuted;

        var changed = await observer.PollOnceAsync();

        Assert.True(changed);
        Assert.Equal(TeamsControlState.Unmuted, stateStore.MicrophoneState);
        Assert.Equal(StateConfidence.Confirmed, stateStore.Confidence);
        Assert.Equal("active", stateStore.MeetingStatus);
    }

    [Fact]
    public async Task LifecycleRecovery_DisposesOldUiaSubscriptions_NoDuplicates()
    {
        var controller = new TestTeamsController { CurrentMicState = TeamsControlState.Muted };
        var stateStore = new MeetingStateStore();

        using var observer = new TeamsPassiveStateObserver(controller, stateStore);

        // Initial scan: establishes subscriptions (1 mic + 1 camera)
        await observer.PollOnceAsync();
        Assert.Equal(1, controller.MicSubscriptionCreatedCount);
        Assert.Equal(1, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(2, controller.SubscriptionCreatedCount);
        Assert.Equal(0, controller.SubscriptionDisposedCount);

        // Meeting drops: disposes both subscriptions
        controller.MeetingDetected = false;
        await observer.PollOnceAsync();
        Assert.Equal(2, controller.SubscriptionCreatedCount);
        Assert.Equal(1, controller.MicSubscriptionDisposedCount);
        Assert.Equal(1, controller.CameraSubscriptionDisposedCount);
        Assert.Equal(2, controller.SubscriptionDisposedCount);

        // Meeting resumes: re-establishes both subscriptions
        controller.MeetingDetected = true;
        await observer.PollOnceAsync();
        Assert.Equal(2, controller.MicSubscriptionCreatedCount);
        Assert.Equal(2, controller.CameraSubscriptionCreatedCount);
        Assert.Equal(4, controller.SubscriptionCreatedCount);
        Assert.Equal(2, controller.SubscriptionDisposedCount);

        // Observer disposed: disposes both subscriptions cleanly
        observer.Dispose();
        Assert.Equal(4, controller.SubscriptionCreatedCount);
        Assert.Equal(2, controller.MicSubscriptionDisposedCount);
        Assert.Equal(2, controller.CameraSubscriptionDisposedCount);
        Assert.Equal(4, controller.SubscriptionDisposedCount);
    }
}
