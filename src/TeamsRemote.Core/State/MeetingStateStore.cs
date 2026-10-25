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
    public string HandState { get; private set; } = TeamsControlState.Unknown;
    public ControlCapabilityTier HandCapability { get; private set; } = ControlCapabilityTier.Unavailable;
    public StateConfidence HandConfidence { get; private set; } = StateConfidence.Unknown;
    public string MeetingStatus { get; private set; } = "none";
    public DateTimeOffset LastObservedAt { get; private set; } = DateTimeOffset.UtcNow;

    public event Action? StateChanged;

    public bool UpdateObserved(
        string micState,
        string cameraState,
        string handState,
        bool isConfirmed = true,
        string meetingStatus = "active",
        ControlCapabilityTier cameraCapability = ControlCapabilityTier.ObserveOnly,
        StateConfidence cameraConfidence = StateConfidence.Confirmed,
        ControlCapabilityTier handCapability = ControlCapabilityTier.ObserveOnly,
        StateConfidence handConfidence = StateConfidence.Confirmed)
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
                string.Equals(HandState, handState, StringComparison.OrdinalIgnoreCase) &&
                HandCapability == handCapability &&
                HandConfidence == handConfidence &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = micState;
            Confidence = newMicConfidence;
            CameraState = cameraState;
            CameraCapability = cameraCapability;
            CameraConfidence = cameraConfidence;
            HandState = handState;
            HandCapability = handCapability;
            HandConfidence = handConfidence;
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

    public bool UpdateObserved(
        string micState,
        string cameraState,
        bool isConfirmed = true,
        string meetingStatus = "active",
        ControlCapabilityTier cameraCapability = ControlCapabilityTier.ObserveOnly,
        StateConfidence cameraConfidence = StateConfidence.Confirmed) =>
        UpdateObserved(
            micState,
            cameraState,
            HandState,
            isConfirmed,
            meetingStatus,
            cameraCapability,
            cameraConfidence,
            HandCapability,
            HandConfidence);

    public bool UpdateObserved(string newState, bool isConfirmed = true, string meetingStatus = "active") =>
        UpdateObserved(newState, CameraState, HandState, isConfirmed, meetingStatus, CameraCapability, CameraConfidence, HandCapability, HandConfidence);

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
                string.Equals(HandState, TeamsControlState.Unknown, StringComparison.OrdinalIgnoreCase) &&
                HandCapability == ControlCapabilityTier.Unavailable &&
                HandConfidence == StateConfidence.Unknown &&
                string.Equals(MeetingStatus, meetingStatus, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MicrophoneState = TeamsControlState.Unknown;
            Confidence = StateConfidence.Unknown;
            CameraState = TeamsControlState.Unknown;
            CameraCapability = ControlCapabilityTier.Unavailable;
            CameraConfidence = StateConfidence.Unknown;
            HandState = TeamsControlState.Unknown;
            HandCapability = ControlCapabilityTier.Unavailable;
            HandConfidence = StateConfidence.Unknown;
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

    public void MarkCameraPending()
    {
        lock (_lock)
        {
            CameraConfidence = StateConfidence.Pending;
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

    public void MarkCameraStaleOrUncertain()
    {
        lock (_lock)
        {
            CameraConfidence = StateConfidence.Stale;
            CameraCapability = ControlCapabilityTier.ObserveOnly;
            LastObservedAt = DateTimeOffset.UtcNow;
        }

        StateChanged?.Invoke();
    }

    public bool ResetCameraCapabilityToObserveOnly()
    {
        bool changed = false;
        lock (_lock)
        {
            if (CameraCapability == ControlCapabilityTier.Verified)
            {
                CameraCapability = ControlCapabilityTier.ObserveOnly;
                StateRevision++;
                LastObservedAt = DateTimeOffset.UtcNow;
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }

        return changed;
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
            HandState = TeamsControlState.Unknown;
            HandCapability = ControlCapabilityTier.Unavailable;
            HandConfidence = StateConfidence.Unknown;
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

    public (long Revision, string MicState, StateConfidence MicConfidence, string CameraState, ControlCapabilityTier CameraCapability, StateConfidence CameraConfidence, string HandState, ControlCapabilityTier HandCapability, StateConfidence HandConfidence, string Meeting) GetFullSnapshot()
    {
        lock (_lock)
        {
            return (StateRevision, MicrophoneState, Confidence, CameraState, CameraCapability, CameraConfidence, HandState, HandCapability, HandConfidence, MeetingStatus);
        }
    }
}
