using System.Text.Json;
using TeamsRemote.Core.Protocol;

namespace TeamsRemote.Relay.Routing;

public sealed class DeviceRouteManager
{
    private record EphemeralChallenge(PairChallengePayload Challenge, Guid CompanionSessionId);
    private record PendingPairRequest(Guid ClientSessionId, Guid CompanionSessionId, DateTimeOffset RequestedAt);
    private record ActiveRoute(Guid? ClientSessionId, Guid CompanionSessionId, string SessionToken);

    private readonly Dictionary<Guid, RelaySession> _sessions = new();
    private readonly Dictionary<Guid, EphemeralChallenge> _challenges = new();
    private readonly Dictionary<Guid, PendingPairRequest> _pendingPairRequests = new(); // Keyed by ChallengeId
    private readonly Dictionary<Guid, ActiveRoute> _routes = new(); // Keyed by DeviceId
    private readonly object _lock = new();

    public void RegisterSession(RelaySession session)
    {
        lock (_lock)
        {
            _sessions[session.SessionId] = session;
        }
    }

    public void UnregisterSession(Guid sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);

            // Clean up routes if companion session disconnected
            var companionDeadRoutes = _routes
                .Where(r => r.Value.CompanionSessionId == sessionId)
                .Select(r => r.Key)
                .ToList();

            foreach (var devId in companionDeadRoutes)
            {
                _routes.Remove(devId);
            }

            // Detach client session if client disconnected, preserving route for reconnect
            var clientDetachedRoutes = _routes
                .Where(r => r.Value.ClientSessionId == sessionId)
                .Select(r => r.Key)
                .ToList();

            foreach (var devId in clientDetachedRoutes)
            {
                var route = _routes[devId];
                _routes[devId] = route with { ClientSessionId = null };
            }

            // Clean up challenges referencing this companion session
            var deadChallenges = _challenges
                .Where(c => c.Value.CompanionSessionId == sessionId)
                .Select(c => c.Key)
                .ToList();

            foreach (var chId in deadChallenges)
            {
                _challenges.Remove(chId);
            }

            // Clean up pending requests referencing this session
            var deadPending = _pendingPairRequests
                .Where(p => p.Value.ClientSessionId == sessionId || p.Value.CompanionSessionId == sessionId)
                .Select(p => p.Key)
                .ToList();

            foreach (var chId in deadPending)
            {
                _pendingPairRequests.Remove(chId);
            }
        }
    }

    public bool TryResumeClientSession(RelaySession clientSession, Guid deviceId, string sessionToken)
    {
        lock (_lock)
        {
            if (!_routes.TryGetValue(deviceId, out var route))
            {
                return false;
            }

            if (!string.Equals(route.SessionToken, sessionToken, StringComparison.Ordinal))
            {
                return false;
            }

            // Re-bind the new client session to the active companion route
            _routes[deviceId] = route with { ClientSessionId = clientSession.SessionId };
            clientSession.PairedDeviceId = deviceId;
            return true;
        }
    }

    public bool RegisterCompanionRoute(RelaySession companionSession, Guid deviceId, string sessionToken)
    {
        lock (_lock)
        {
            if (_routes.TryGetValue(deviceId, out var existing))
            {
                if (!string.Equals(existing.SessionToken, sessionToken, StringComparison.Ordinal))
                {
                    return false;
                }
                _routes[deviceId] = existing with { CompanionSessionId = companionSession.SessionId };
            }
            else
            {
                _routes[deviceId] = new ActiveRoute(null, companionSession.SessionId, sessionToken);
            }
            return true;
        }
    }

    public bool RegisterChallenge(PairChallengePayload challenge, Guid companionSessionId)
    {
        lock (_lock)
        {
            PruneExpiredChallenges();

            if (!_sessions.ContainsKey(companionSessionId))
            {
                return false;
            }

            _challenges[challenge.ChallengeId] = new EphemeralChallenge(challenge, companionSessionId);
            return true;
        }
    }

    public async Task<bool> ProcessPairRequestAsync(RelaySession clientSession, PairRequestPayload request)
    {
        EphemeralChallenge? challengeEntry;

        lock (_lock)
        {
            PruneExpiredChallenges();

            if (!_challenges.TryGetValue(request.ChallengeId, out challengeEntry))
            {
                return false; // Challenge not found or expired
            }

            if (challengeEntry.Challenge.IsExpired)
            {
                _challenges.Remove(request.ChallengeId);
                return false;
            }

            if (!string.Equals(challengeEntry.Challenge.ShortCode, request.ShortCode, StringComparison.OrdinalIgnoreCase))
            {
                return false; // Code mismatch
            }

            // Remove challenge so it cannot be reused (single-use)
            _challenges.Remove(request.ChallengeId);

            clientSession.DeviceLabel = request.DeviceLabel;

            _pendingPairRequests[request.ChallengeId] = new PendingPairRequest(
                clientSession.SessionId,
                challengeEntry.CompanionSessionId,
                DateTimeOffset.UtcNow);
        }

        // Forward to companion for user approval
        RelaySession? companionSession;
        lock (_lock)
        {
            _sessions.TryGetValue(challengeEntry.CompanionSessionId, out companionSession);
        }

        if (companionSession == null) return false;

        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.PairRequest,
            request,
            Guid.Empty,
            correlationId: request.ChallengeId);

        await companionSession.SendMessageAsync(envelope.Serialize());
        return true;
    }

    public async Task<bool> ProcessPairApproveAsync(RelaySession companionSession, PairApprovePayload approve)
    {
        PendingPairRequest? pending;
        lock (_lock)
        {
            if (!_pendingPairRequests.TryGetValue(approve.ChallengeId, out pending))
            {
                return false;
            }

            _pendingPairRequests.Remove(approve.ChallengeId);
        }

        RelaySession? targetClient;
        lock (_lock)
        {
            _sessions.TryGetValue(pending.ClientSessionId, out targetClient);
        }

        if (targetClient == null) return false;

        if (!approve.Approved)
        {
            var rejectEnvelope = ProtocolEnvelope.Create(
                ProtocolMessageType.PairResponse,
                new { approved = false, reason = "Pairing declined by companion user." },
                approve.DeviceId,
                approve.ChallengeId);

            await targetClient.SendMessageAsync(rejectEnvelope.Serialize());
            return false;
        }

        lock (_lock)
        {
            // Enforce 1 active paired phone per companion: clear existing routes for this companion
            var existingRoutes = _routes
                .Where(r => r.Value.CompanionSessionId == companionSession.SessionId)
                .Select(r => r.Key)
                .ToList();

            foreach (var oldDeviceId in existingRoutes)
            {
                _routes.Remove(oldDeviceId);
            }

            // Establish new isolated route
            _routes[approve.DeviceId] = new ActiveRoute(
                targetClient.SessionId,
                companionSession.SessionId,
                approve.SessionToken ?? Guid.NewGuid().ToString("N"));

            targetClient.PairedDeviceId = approve.DeviceId;
        }

        // Forward approval and token to client
        var approveEnvelope = ProtocolEnvelope.Create(
            ProtocolMessageType.PairResponse,
            approve,
            approve.DeviceId,
            approve.ChallengeId);

        await targetClient.SendMessageAsync(approveEnvelope.Serialize());
        return true;
    }

    public async Task<bool> RouteMessageAsync(RelaySession sender, ProtocolEnvelope envelope)
    {
        RelaySession? destination = null;

        lock (_lock)
        {
            if (sender.Type == SessionType.Client)
            {
                // Strict isolation: Client can only send messages for its own paired device ID
                if (sender.PairedDeviceId == null || sender.PairedDeviceId != envelope.DeviceId)
                {
                    return false; // Cross-device or unauthenticated attempt rejected
                }

                if (!_routes.TryGetValue(envelope.DeviceId, out var route))
                {
                    return false; // No route established
                }

                if (route.ClientSessionId != sender.SessionId)
                {
                    return false; // Route belongs to different client session
                }

                _sessions.TryGetValue(route.CompanionSessionId, out destination);
            }
            else if (sender.Type == SessionType.Companion)
            {
                // Companion sending to paired client
                if (envelope.DeviceId == Guid.Empty)
                {
                    // Broadcast to paired client if only 1 active route
                    var companionRoute = _routes.Values.FirstOrDefault(r => r.CompanionSessionId == sender.SessionId);
                    if (companionRoute?.ClientSessionId is { } clientSessionId)
                    {
                        _sessions.TryGetValue(clientSessionId, out destination);
                    }
                }
                else if (_routes.TryGetValue(envelope.DeviceId, out var route))
                {
                    if (route.CompanionSessionId == sender.SessionId && route.ClientSessionId is { } clientSessionId)
                    {
                        _sessions.TryGetValue(clientSessionId, out destination);
                    }
                }
            }
        }

        if (destination == null)
        {
            return false;
        }

        await destination.SendMessageAsync(envelope.Serialize());
        return true;
    }

    public void RevokeDevice(Guid deviceId)
    {
        RelaySession? clientSession = null;

        lock (_lock)
        {
            if (_routes.TryGetValue(deviceId, out var route))
            {
                if (route.ClientSessionId is { } clientSessionId)
                {
                    _sessions.TryGetValue(clientSessionId, out clientSession);
                }
                _routes.Remove(deviceId);
            }
        }

        if (clientSession != null)
        {
            try
            {
                clientSession.Socket.Abort();
            }
            catch
            {
                // Ignore abort errors
            }
        }
    }

    private void PruneExpiredChallenges()
    {
        var expired = _challenges
            .Where(c => c.Value.Challenge.IsExpired)
            .Select(c => c.Key)
            .ToList();

        foreach (var key in expired)
        {
            _challenges.Remove(key);
        }
    }
}
