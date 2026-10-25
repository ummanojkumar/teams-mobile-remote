using TeamsRemote.Core.Models;

namespace TeamsRemote.Core.State;

public sealed class MeetingStateStore
{
    private readonly object _lock = new();

    public long StateRevision { get; private set; }
    public string MicrophoneState { get; private set; } = TeamsControlState.Unknown;
    public StateConfidence Confidence { get; private set; } = StateConfidence.Unknown;
    public string CameraState { get; private set; } = TeamsControlState.Unknown;
    public ControlCapabilityTier CameraCapability { get; private set; } = ControlCapabilityTier.Unavailable;
    public StateConfidence CameraConfidence { get; private set; } = StateConfidence.Unknown;
    public string MeetingStatus { get; private set; } = "none";
    public DateTimeOffset LastObservedAt { get; private set; } = DateTimeOffset.UtcNow;

    public event Action? StateChanged;

    public bool UpdateObserved(
        string micState,
        string cameraState,
        bool isConfirmed = true,
        string meetingStatus = "active",
        ControlCapabilityTier cameraCapability = ControlCapabilityTier.ObserveOnly,
        StateConfidence cameraConfidence = StateConfidence.Confirmed)
    {
        var newMicConfidence = isConfirmed ? StateConfidence.Confirmed : StateConfidence.Stale;
        bool changed = false;

        lock (_lock)
        {
            if (string.Equals(MicrophoneState, micState, StringComparison.OrdinalIgnoreCase) &&
                Confidence == newMicConfidence &&
                string.Equals(CameraState, cameraState, StringComparison.OrdinalIgnoreCase) &&
                CameraCapability == cameraCapability &&
                CameraConfidence == cameraConfidence &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = micState;
            Confidence = newMicConfidence;
            CameraState = cameraState;
            CameraCapability = cameraCapability;
            CameraConfidence = cameraConfidence;
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

    public bool UpdateObserved(string newState, bool isConfirmed = true, string meetingStatus = "active") =>
        UpdateObserved(newState, CameraState, isConfirmed, meetingStatus, CameraCapability, CameraConfidence);

    public bool MarkUnavailable(string meetingStatus = "none")
    {
        bool changed = false;

        lock (_lock)
        {
            if (string.Equals(MicrophoneState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase) &&
                Confidence == StateConfidence.Unknown &&
                string.Equals(CameraState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase) &&
                CameraCapability == ControlCapabilityTier.Unavailable &&
                CameraConfidence == StateConfidence.Unknown &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = TeamsControlState.Unknown;
            Confidence = StateConfidence.Unknown;
            CameraState = TeamsControlState.Unknown;
            CameraCapability = ControlCapabilityTier.Unavailable;
            CameraConfidence = StateConfidence.Unknown;
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
            CameraState = TeamsControlState.Unknown;
            CameraCapability = ControlCapabilityTier.Unavailable;
            CameraConfidence = StateConfidence.Unknown;
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

    public (long Revision, string MicState, StateConfidence MicConfidence, string CameraState, ControlCapabilityTier CameraCapability, StateConfidence CameraConfidence, string Meeting) GetFullSnapshot()
    {
        lock (_lock)
        {
            return (StateRevision, MicrophoneState, Confidence, CameraState, CameraCapability, CameraConfidence, MeetingStatus);
        }
    }
}
