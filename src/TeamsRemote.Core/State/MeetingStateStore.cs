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

    public bool UpdateObserved(string newState, bool isConfirmed = true, string meetingStatus = "active")
    {
        var newConfidence = isConfirmed ? StateConfidence.Confirmed : StateConfidence.Stale;
        bool changed = false;

        lock (_lock)
        {
            if (string.Equals(MicrophoneState, newState, StringComparison.OrdinalIgnoreCase) &&
                Confidence == newConfidence &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = newState;
            Confidence = newConfidence;
            MeetingStatus = meetingStatus;
            StateRevision++;
            LastObservedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }

        return changed;
    }

    public bool MarkUnavailable(string meetingStatus = "none")
    {
        bool changed = false;

        lock (_lock)
        {
            if (string.Equals(MicrophoneState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase) &&
                Confidence == StateConfidence.Unknown &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = TeamsControlState.Unknown;
            Confidence = StateConfidence.Unknown;
            MeetingStatus = meetingStatus;
            StateRevision++;
            LastObservedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }

        return changed;
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
