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

    private IDisposable? _currentUiaSubscription;
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
    /// Simulates or receives a notification that a UIA property change occurred.
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
                DisposeCurrentSubscription();
                return _stateStore.MarkUnavailable("none");
            }

            // Case 2: Teams is running, but no meeting window detected
            if (!report.MeetingSurface.IsDetected)
            {
                DisposeCurrentSubscription();
                return _stateStore.MarkUnavailable("none");
            }

            // Case 3: Active meeting surface detected
            var mic = report.Controls.FirstOrDefault(c => c.TargetControl == TargetControl.Microphone);
            if (mic == null || !mic.Found || mic.ReadableState == TeamsControlState.Unknown)
            {
                DisposeCurrentSubscription();
                return _stateStore.UpdateObserved(TeamsControlState.Unknown, isConfirmed: false, meetingStatus: "active");
            }

            // Case 4: Microphone is readable and valid in active meeting
            EnsureUiaSubscription();

            return _stateStore.UpdateObserved(mic.ReadableState, isConfirmed: true, meetingStatus: "active");
        }
        catch
        {
            DisposeCurrentSubscription();
            return _stateStore.UpdateObserved(TeamsControlState.Unknown, isConfirmed: false, meetingStatus: "active");
        }
        finally
        {
            _reQueryLock.Release();
        }
    }

    private void EnsureUiaSubscription()
    {
        lock (_subLock)
        {
            if (_currentUiaSubscription != null) return;

            try
            {
                _currentUiaSubscription = _teamsController.SubscribeMicrophoneStateChanged(() => OnUiaEventRaised());
            }
            catch
            {
                _currentUiaSubscription = null;
            }
        }
    }

    private void DisposeCurrentSubscription()
    {
        lock (_subLock)
        {
            if (_currentUiaSubscription != null)
            {
                try
                {
                    _currentUiaSubscription.Dispose();
                }
                catch
                {
                    // Ignore disposal error
                }
                finally
                {
                    _currentUiaSubscription = null;
                }
            }
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

        DisposeCurrentSubscription();
        _reQueryLock.Dispose();
    }
}
