namespace TeamsRemote.Core.Protocol;

public static class ProtocolMessageType
{
    public const string Command = "command";
    public const string CommandResult = "command_result";
    public const string StateSnapshot = "state_snapshot";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string PairChallenge = "pair_challenge";
    public const string PairRequest = "pair_request";
    public const string PairApprove = "pair_approve";
    public const string PairResponse = "pair_response";
    public const string RevokeDevice = "revoke_device";
}
