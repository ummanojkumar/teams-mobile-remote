using System.Diagnostics;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.State;

/// <summary>
/// Passive state observation engine for Teams meeting controls.
/// Listens to UIA property changes (Name and ToggleState), debounces burst events,
/// authoritatively re-queries the control state before publishing, and maintains a
/// conservative polling fallback (every 1-2 seconds) to recover from dropped events.
///
/// Guarantees that duplicate event handlers are disposed before re-subscription,
/// and immediately updates the state store to unavailable/unknown when Teams closes
/// or the meeting surface is lost.
/// </summary>
public sealed class TeamsPassiveStateObserver : IDisposable
{
    private readonly ITeamsController _teamsController;
    private readonly MeetingStateStore _stateStore;
    private readonly Func<bool>? _isEnabledFunc;
    private readonly TimeSpan _pollingInterval;
    private readonly TimeSpan _debounceDuration;

    private readonly SemaphoreSlim _reQueryLock = new(1, 1);
    private readonly object _subLock = new();

    private IDisposable? _currentMicSubscription;
    private IDisposable? _currentCameraSubscription;
    private IDisposable? _currentHandSubscription;
    private Timer? _debounceTimer;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _disposed;

    public TeamsPassiveStateObserver(
        ITeamsController teamsController,
        MeetingStateStore stateStore,
        Func<bool>? isEnabledFunc = null,
        TimeSpan? pollingInterval = null,
        TimeSpan? debounceDuration = null)
    {
        _teamsController = teamsController ?? throw new ArgumentNullException(nameof(teamsController));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _isEnabledFunc = isEnabledFunc;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(2);
        _debounceDuration = debounceDuration ?? TimeSpan.FromMilliseconds(150);
    }

    /// <summary>
    /// Starts the passive observation loop and polling fallback.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pollingTask != null) return;

        _pollingCts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollingLoopAsync(_pollingCts.Token));

        // Initial scan and subscription attempt
        _ = Task.Run(async () =>
        {
            try
            {
                await ReQueryAndPublishAsync(CancellationToken.None);
            }
            catch
            {
                // Ignore transient startup errors
            }
        });
    }

    /// <summary>
    /// Simulates or receives a notification that a UIA property change occurred on Mic or Camera.
    /// Coalesces burst events using a debounce timer, then re-queries Teams state.
    /// </summary>
    public void OnUiaEventRaised()
    {
        if (_disposed) return;

        lock (_subLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                callback: async _ =>
                {
                    try
                    {
                        await ReQueryAndPublishAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // Safe swallow on debounce callback
                    }
                },
                state: null,
                dueTime: _debounceDuration,
                period: Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Executes a single authoritative re-query, lifecycle check, and publication.
    /// Can be called manually (e.g. during manual "Scan Teams" or testing).
    /// </summary>
    public async Task<bool> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        return await ReQueryAndPublishAsync(cancellationToken);
    }

    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollingInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }

                // Only poll if observer is enabled (e.g. companion running, paired client exists)
                if (_isEnabledFunc == null || _isEnabledFunc())
                {
                    await ReQueryAndPublishAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Prevent polling loop crash
            }
        }
    }

    private async Task<bool> ReQueryAndPublishAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return false;

        if (!await _reQueryLock.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken))
        {
            return false;
        }

        try
        {
            var report = await _teamsController.InspectAsync(cancellationToken);

            // Case 1: Teams is not running
            if (!report.Process.IsRunning)
            {
                DisposeCurrentSubscriptions();
                return _stateStore.MarkUnavailable("none");
            }

            // Case 2: Teams is running, but no meeting window detected
            if (!report.MeetingSurface.IsDetected)
            {
                DisposeCurrentSubscriptions();
                return _stateStore.MarkUnavailable("none");
            }

            // Case 3: Active meeting surface detected - inspect Microphone, Camera, and Hand
            var mic = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Microphone);
            var camera = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Camera);
            var hand = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Hand);

            string micState = (mic != null && mic.Found && mic.ReadableState != TeamsControlState.Unknown)
                ? mic.ReadableState
                : TeamsControlState.Unknown;
            bool isMicConfirmed = micState != TeamsControlState.Unknown;

            string cameraState = (camera != null && camera.Found && camera.ReadableState != TeamsControlState.Unknown)
                ? camera.ReadableState
                : TeamsControlState.Unknown;

            // Phase 4C Rule: If camera was already verified for this active meeting surface, preserve Verified.
            // If the meeting was re-resolved, window closed, or verification failed, it resets back to ObserveOnly.
            ControlCapabilityTier cameraCap = (cameraState != TeamsControlState.Unknown)
                ? (_stateStore.CameraCapability == ControlCapabilityTier.Verified ? ControlCapabilityTier.Verified : ControlCapabilityTier.ObserveOnly)
                : ControlCapabilityTier.Unavailable;

            StateConfidence cameraConf = (cameraState != TeamsControlState.Unknown)
                ? StateConfidence.Confirmed
                : StateConfidence.Unknown;

            // Phase 5A Rule: Hand capability strictly remains ObserveOnly when readable (never Verified in Phase 5A)
            string handState = (hand != null && hand.Found && hand.ReadableState != TeamsControlState.Unknown)
                ? hand.ReadableState
                : TeamsControlState.Unknown;

            ControlCapabilityTier handCap = (handState != TeamsControlState.Unknown)
                ? ControlCapabilityTier.ObserveOnly
                : ControlCapabilityTier.Unavailable;

            StateConfidence handConf = (handState != TeamsControlState.Unknown)
                ? StateConfidence.Confirmed
                : StateConfidence.Unknown;

            if (micState == TeamsControlState.Unknown && cameraState == TeamsControlState.Unknown && handState == TeamsControlState.Unknown)
            {
                DisposeCurrentSubscriptions();
                return _stateStore.UpdateObserved(
                    TeamsControlState.Unknown,
                    TeamsControlState.Unknown,
                    TeamsControlState.Unknown,
                    isConfirmed: false,
                    meetingStatus: "active",
                    cameraCapability: ControlCapabilityTier.Unavailable,
                    cameraConfidence: StateConfidence.Unknown,
                    handCapability: ControlCapabilityTier.Unavailable,
                    handConfidence: StateConfidence.Unknown);
            }

            EnsureUiaSubscriptions();

            return _stateStore.UpdateObserved(
                micState,
                cameraState,
                handState,
                isConfirmed: isMicConfirmed,
                meetingStatus: "active",
                cameraCapability: cameraCap,
                cameraConfidence: cameraConf,
                handCapability: handCap,
                handConfidence: handConf);
        }
        catch
        {
            DisposeCurrentSubscriptions();
            return _stateStore.UpdateObserved(
                TeamsControlState.Unknown,
                TeamsControlState.Unknown,
                TeamsControlState.Unknown,
                isConfirmed: false,
                meetingStatus: "active",
                cameraCapability: ControlCapabilityTier.Unavailable,
                cameraConfidence: StateConfidence.Unknown,
                handCapability: ControlCapabilityTier.Unavailable,
                handConfidence: StateConfidence.Unknown);
        }
        finally
        {
            _reQueryLock.Release();
        }
    }

    private void EnsureUiaSubscriptions()
    {
        lock (_subLock)
        {
            if (_currentMicSubscription == null)
            {
                try
                {
                    _currentMicSubscription = _teamsController.SubscribeMicrophoneStateChanged(() => OnUiaEventRaised());
                }
                catch
                {
                    _currentMicSubscription = null;
                }
            }

            if (_currentCameraSubscription == null)
            {
                try
                {
                    _currentCameraSubscription = _teamsController.SubscribeCameraStateChanged(() => OnUiaEventRaised());
                }
                catch
                {
                    _currentCameraSubscription = null;
                }
            }

            if (_currentHandSubscription == null)
            {
                try
                {
                    _currentHandSubscription = _teamsController.SubscribeHandStateChanged(() => OnUiaEventRaised());
                }
                catch
                {
                    _currentHandSubscription = null;
                }
            }
        }
    }

    private void DisposeCurrentSubscriptions()
    {
        lock (_subLock)
        {
            if (_currentMicSubscription != null)
            {
                try
                {
                    _currentMicSubscription.Dispose();
                }
                catch
                {
                    // Ignore disposal error
                }
                finally
                {
                    _currentMicSubscription = null;
                }
            }

            if (_currentCameraSubscription != null)
            {
                try
                {
                    _currentCameraSubscription.Dispose();
                }
                catch
                {
                    // Ignore disposal error
                }
                finally
                {
                    _currentCameraSubscription = null;
                }
            }

            if (_currentHandSubscription != null)
            {
                try
                {
                    _currentHandSubscription.Dispose();
                }
                catch
                {
                    // Ignore disposal error
                }
                finally
                {
                    _currentHandSubscription = null;
                }
            }

            // Phase 4C Rule: Reset verified capability to observe-only whenever controls/subscriptions are re-resolved
            _stateStore.ResetCameraCapabilityToObserveOnly();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollingCts?.Cancel();
        _pollingCts?.Dispose();

        lock (_subLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        DisposeCurrentSubscriptions();
        _reQueryLock.Dispose();
    }
}
