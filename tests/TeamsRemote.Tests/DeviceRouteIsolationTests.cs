using System.Net.WebSockets;
using System.Text;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Relay.Routing;

namespace TeamsRemote.Tests;

public sealed class DeviceRouteIsolationTests
{
    private class MockWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public List<string> SentMessages { get; } = new();

        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            if (buffer.Array != null)
            {
                var text = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
                SentMessages.Add(text);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Client_CannotRoute_BeforePairingIsEstablished()
    {
        var manager = new DeviceRouteManager();
        var clientSocket = new MockWebSocket();
        var clientSession = new RelaySession(clientSocket, SessionType.Client);
        manager.RegisterSession(clientSession);

        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "test_echo" },
            Guid.NewGuid());

        bool routed = await manager.RouteMessageAsync(clientSession, envelope);
        Assert.False(routed);
    }

    [Fact]
    public async Task Client_CannotRoute_ToDifferentDeviceRoute()
    {
        var manager = new DeviceRouteManager();
        var compSocket = new MockWebSocket();
        var compSession = new RelaySession(compSocket, SessionType.Companion);
        manager.RegisterSession(compSession);

        var clientSocket = new MockWebSocket();
        var clientSession = new RelaySession(clientSocket, SessionType.Client);
        manager.RegisterSession(clientSession);

        // Register challenge and pair
        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "123-456",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        var req = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "123-456",
            DeviceLabel = "Test Phone"
        };
        bool reqForwarded = await manager.ProcessPairRequestAsync(clientSession, req);
        Assert.True(reqForwarded);

        var pairedDeviceId = Guid.NewGuid();
        var approve = new PairApprovePayload
        {
            ChallengeId = challenge.ChallengeId,
            Approved = true,
            DeviceId = pairedDeviceId,
            SessionToken = "token_abc"
        };
        bool approved = await manager.ProcessPairApproveAsync(compSession, approve);
        Assert.True(approved);

        // Valid route for pairedDeviceId
        var validEnvelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "test_echo" },
            pairedDeviceId);

        bool validRouted = await manager.RouteMessageAsync(clientSession, validEnvelope);
        Assert.True(validRouted);

        // Invalid route spoofing a different device ID
        var spoofedEnvelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "test_echo" },
            Guid.NewGuid());

        bool spoofedRouted = await manager.RouteMessageAsync(clientSession, spoofedEnvelope);
        Assert.False(spoofedRouted);
    }

    [Fact]
    public async Task Challenge_ExpiresAfter60s_CannotBeRedeemed()
    {
        var manager = new DeviceRouteManager();
        var compSocket = new MockWebSocket();
        var compSession = new RelaySession(compSocket, SessionType.Companion);
        manager.RegisterSession(compSession);

        var clientSocket = new MockWebSocket();
        var clientSession = new RelaySession(clientSocket, SessionType.Client);
        manager.RegisterSession(clientSession);

        // Challenge expired in past
        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "111-222",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        var req = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "111-222",
            DeviceLabel = "Expired Phone"
        };

        bool result = await manager.ProcessPairRequestAsync(clientSession, req);
        Assert.False(result);
    }

    [Fact]
    public async Task Challenge_IsSingleUse_CannotBeRedeemedTwice()
    {
        var manager = new DeviceRouteManager();
        var compSocket = new MockWebSocket();
        var compSession = new RelaySession(compSocket, SessionType.Companion);
        manager.RegisterSession(compSession);

        var client1 = new RelaySession(new MockWebSocket(), SessionType.Client);
        var client2 = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(client1);
        manager.RegisterSession(client2);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "333-444",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        var req1 = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "333-444",
            DeviceLabel = "First Phone"
        };
        bool firstOk = await manager.ProcessPairRequestAsync(client1, req1);
        Assert.True(firstOk);

        // Second attempt on same challenge must fail
        var req2 = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "333-444",
            DeviceLabel = "Second Phone"
        };
        bool secondOk = await manager.ProcessPairRequestAsync(client2, req2);
        Assert.False(secondOk);
    }

    [Fact]
    public async Task Challenge_WithIncorrectShortCode_IsRejected()
    {
        var manager = new DeviceRouteManager();
        var compSocket = new MockWebSocket();
        var compSession = new RelaySession(compSocket, SessionType.Companion);
        manager.RegisterSession(compSession);

        var clientSession = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(clientSession);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "555-666",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        var req = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "999-999", // Mismatch
            DeviceLabel = "Hacker Phone"
        };

        bool ok = await manager.ProcessPairRequestAsync(clientSession, req);
        Assert.False(ok);
    }

    [Fact]
    public async Task RevokeDevice_ImmediatelyDropsRoute_AndAbortsClientSocket()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        var clientSocket = new MockWebSocket();
        var clientSession = new RelaySession(clientSocket, SessionType.Client);
        manager.RegisterSession(compSession);
        manager.RegisterSession(clientSession);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "777-888",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        await manager.ProcessPairRequestAsync(clientSession, new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = "777-888",
            DeviceLabel = "Phone"
        });

        var deviceId = Guid.NewGuid();
        await manager.ProcessPairApproveAsync(compSession, new PairApprovePayload
        {
            ChallengeId = challenge.ChallengeId,
            Approved = true,
            DeviceId = deviceId
        });

        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.Command,
            new CommandPayload { Command = "test_echo" },
            deviceId);

        // Before revocation: route works
        Assert.True(await manager.RouteMessageAsync(clientSession, envelope));

        // Revoke device
        manager.RevokeDevice(deviceId);

        // Socket was aborted
        Assert.Equal(WebSocketState.Aborted, clientSocket.State);

        // Subsequent message routing is immediately rejected
        Assert.False(await manager.RouteMessageAsync(clientSession, envelope));
    }

    [Fact]
    public async Task EnforceSingleActiveDevice_PairingSecondDevice_DropsFirstDeviceRoute()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var client1 = new RelaySession(new MockWebSocket(), SessionType.Client);
        var client2 = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(client1);
        manager.RegisterSession(client2);

        // Pair Device 1
        var ch1 = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "111-111",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(ch1, compSession.SessionId);
        await manager.ProcessPairRequestAsync(client1, new PairRequestPayload { ChallengeId = ch1.ChallengeId, ShortCode = "111-111" });
        var dev1 = Guid.NewGuid();
        await manager.ProcessPairApproveAsync(compSession, new PairApprovePayload { ChallengeId = ch1.ChallengeId, Approved = true, DeviceId = dev1 });

        // Dev1 route is active
        var env1 = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "test_echo" }, dev1);
        Assert.True(await manager.RouteMessageAsync(client1, env1));

        // Pair Device 2
        var ch2 = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "222-222",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(ch2, compSession.SessionId);
        await manager.ProcessPairRequestAsync(client2, new PairRequestPayload { ChallengeId = ch2.ChallengeId, ShortCode = "222-222" });
        var dev2 = Guid.NewGuid();
        await manager.ProcessPairApproveAsync(compSession, new PairApprovePayload { ChallengeId = ch2.ChallengeId, Approved = true, DeviceId = dev2 });

        // Dev2 route is active
        var env2 = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "test_echo" }, dev2);
        Assert.True(await manager.RouteMessageAsync(client2, env2));

        // Dev1 route is now cleared because 1 active device limit is enforced
        Assert.False(await manager.RouteMessageAsync(client1, env1));
    }

    [Fact]
    public async Task TryResumeClientSession_PreservesRouteAcrossClientDisconnect_AndResumes()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var client1 = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(client1);

        var ch = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "333-333",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(ch, compSession.SessionId);
        await manager.ProcessPairRequestAsync(client1, new PairRequestPayload { ChallengeId = ch.ChallengeId, ShortCode = "333-333" });

        var devId = Guid.NewGuid();
        var token = "token_resumption_123";
        await manager.ProcessPairApproveAsync(compSession, new PairApprovePayload
        {
            ChallengeId = ch.ChallengeId,
            Approved = true,
            DeviceId = devId,
            SessionToken = token
        });

        // 1. Client 1 sends command successfully
        var env = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "query_state" }, devId);
        Assert.True(await manager.RouteMessageAsync(client1, env));

        // 2. Client 1 disconnects (simulating browser refresh / temporary socket close)
        manager.UnregisterSession(client1.SessionId);

        // 3. New client session connects (tab refreshed)
        var client2 = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(client2);

        // Resume with invalid token fails
        Assert.False(manager.TryResumeClientSession(client2, devId, "wrong_token"));

        // Resume with valid token succeeds
        Assert.True(manager.TryResumeClientSession(client2, devId, token));

        // 4. Client 2 can immediately route messages for the paired device
        Assert.True(await manager.RouteMessageAsync(client2, env));
    }

    [Fact]
    public async Task RegisterCompanionRoute_RestoresRouteAcrossCompanionRestart()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var devId = Guid.NewGuid();
        var token = "token_comp_persisted";

        // Companion registers persisted route
        Assert.True(manager.RegisterCompanionRoute(compSession, devId, token));

        // Client connects and resumes session
        var clientSession = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(clientSession);
        Assert.True(manager.TryResumeClientSession(clientSession, devId, token));

        // Can route command from client to companion
        var env = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "query_state" }, devId);
        Assert.True(await manager.RouteMessageAsync(clientSession, env));
    }

    [Fact]
    public async Task PairRequest_ResolvesByPairingCodeWithoutChallengeId()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var clientSession = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(clientSession);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            PairingCode = "K7M4PX",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        // Client supplies ONLY the 6-character code (ChallengeId is empty)
        var req = new PairRequestPayload
        {
            PairingCode = "k7m4px", // case-insensitive
            DeviceLabel = "Mobile Device"
        };

        var (success, errorCode, reason) = await manager.ProcessPairRequestWithResultAsync(clientSession, req);
        Assert.True(success, reason);
        Assert.Null(errorCode);
    }

    [Fact]
    public async Task PairRequest_WhenInvalidGuess_DoesNotConsumeActiveChallenge()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var attacker = new RelaySession(new MockWebSocket(), SessionType.Client);
        var legitimate = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(attacker);
        manager.RegisterSession(legitimate);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            PairingCode = "K7M4PX",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        // Attacker guesses wrong code
        var wrongReq = new PairRequestPayload
        {
            PairingCode = "WRONGX",
            DeviceLabel = "Attacker"
        };
        var (attackerOk, attackerErr, _) = await manager.ProcessPairRequestWithResultAsync(attacker, wrongReq);
        Assert.False(attackerOk);
        Assert.Equal(ProtocolErrorCodes.InvalidPairingCode, attackerErr);

        // Challenge was NOT consumed; legitimate user still redeems successfully
        var legitimateReq = new PairRequestPayload
        {
            PairingCode = "K7M4PX",
            DeviceLabel = "Legitimate Phone"
        };
        var (legitOk, legitErr, legitReason) = await manager.ProcessPairRequestWithResultAsync(legitimate, legitimateReq);
        Assert.True(legitOk, legitReason);
        Assert.Null(legitErr);
    }

    [Fact]
    public async Task PairRequest_WhenExceedingRateLimit_ReturnsRateLimited()
    {
        var manager = new DeviceRouteManager();
        var compSession = new RelaySession(new MockWebSocket(), SessionType.Companion);
        manager.RegisterSession(compSession);

        var clientSession = new RelaySession(new MockWebSocket(), SessionType.Client);
        manager.RegisterSession(clientSession);

        var challenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            PairingCode = "K7M4PX",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(60)
        };
        manager.RegisterChallenge(challenge, compSession.SessionId);

        // First 3 failed attempts
        for (int i = 0; i < 3; i++)
        {
            var req = new PairRequestPayload
            {
                PairingCode = $"FAIL{i:D2}",
                DeviceLabel = "Brute Forcer"
            };
            var (ok, err, _) = await manager.ProcessPairRequestWithResultAsync(clientSession, req);
            Assert.False(ok);
            Assert.Equal(ProtocolErrorCodes.InvalidPairingCode, err);
        }

        // 4th attempt: must be RATE_LIMITED even if user subsequently supplies the correct code!
        var fourthReq = new PairRequestPayload
        {
            PairingCode = "K7M4PX",
            DeviceLabel = "Brute Forcer"
        };
        var (fourthOk, fourthErr, fourthReason) = await manager.ProcessPairRequestWithResultAsync(clientSession, fourthReq);
        Assert.False(fourthOk);
        Assert.Equal(ProtocolErrorCodes.RateLimited, fourthErr);
        Assert.Contains("rate", fourthReason, StringComparison.OrdinalIgnoreCase);
    }
}
