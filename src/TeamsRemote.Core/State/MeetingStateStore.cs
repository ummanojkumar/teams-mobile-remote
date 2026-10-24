using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.State;

public sealed class MeetingStateStore
{
    private readonly object _lock = new();

    public long StateRevision { get; private set; }
    public string MicrophoneState { get; private set; } = TeamsControlState.Unknown;
    public StateConfidence Confidence { get; private set; } = StateConfidence.Unknown;
    public string MeetingStatus { get; private set; } = "none";
    public DateTimeOffset LastObservedAt { get; private set; } = DateTimeOffset.UtcNow;

    public event Action? StateChanged;

    public void UpdateObserved(string newState, bool isConfirmed = true, string meetingStatus = "active")
    {
        lock (_lock)
        {
            MicrophoneState = newState;
            Confidence = isConfirmed ? StateConfidence.Confirmed : StateConfidence.Stale;
            MeetingStatus = meetingStatus;
            StateRevision++;
            LastObservedAt = DateTimeOffset.UtcNow;
        }

        StateChanged?.Invoke();
    }

    public void MarkPending()
    {
        lock (_lock)
        {
            Confidence = StateConfidence.Pending;
            LastObservedAt = DateTimeOffset.UtcNow;
        }

        StateChanged?.Invoke();
    }

    public void MarkStaleOrUncertain()
    {
        lock (_lock)
        {
            Confidence = StateConfidence.Stale;
            LastObservedAt = DateTimeOffset.UtcNow;
        }

        StateChanged?.Invoke();
    }

    public void Reset()
    {
        lock (_lock)
        {
            MicrophoneState = TeamsControlState.Unknown;
            Confidence = StateConfidence.Unknown;
            MeetingStatus = "none";
            StateRevision = 0;
            LastObservedAt = DateTimeOffset.UtcNow;
        }

        StateChanged?.Invoke();
    }

    public (long Revision, string State, StateConfidence Confidence, string Meeting) GetSnapshot()
    {
        lock (_lock)
        {
            return (StateRevision, MicrophoneState, Confidence, MeetingStatus);
        }
    }
}
