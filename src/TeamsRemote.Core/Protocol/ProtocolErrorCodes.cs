namespace TeamsRemote.Core.Protocol;

public static class ProtocolErrorCodes
{
    public const string UnsupportedProtocolVersion = "UNSUPPORTED_PROTOCOL_VERSION";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string PairingRequired = "PAIRING_REQUIRED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string PairingExpired = "PAIRING_EXPIRED";
    public const string InvalidPairingCode = "INVALID_PAIRING_CODE";
    public const string PairingRejected = "PAIRING_REJECTED";
    public const string RateLimited = "RATE_LIMITED";
    public const string StaleCommand = "STALE_COMMAND";
    public const string UnsupportedInPhase2 = "UNSUPPORTED_IN_PHASE_2";
    public const string NoActiveMeeting = "NO_ACTIVE_MEETING";
    public const string ControlNotFound = "CONTROL_NOT_FOUND";
    public const string LockedSession = "LOCKED_SESSION";
    public const string VerificationFailed = "VERIFICATION_FAILED";
    public const string VerificationTimedOut = "VERIFICATION_TIMED_OUT";
}
