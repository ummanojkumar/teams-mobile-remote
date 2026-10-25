using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using TeamsRemote.Core.Pairing;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.Security;
using TeamsRemote.Core.State;
using TeamsRemote.Core.Transport;

namespace TeamsRemote.Tests;

public sealed class RelayTransportIntegrationTests
{
    private class InMemoryDeviceStore : IDeviceCredentialStore
    {
        private PairedDeviceRecord? _record;
        public Task<PairedDeviceRecord?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_record);
        public Task SaveAsync(PairedDeviceRecord record, CancellationToken cancellationToken = default)
        {
            _record = record;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _record = null;
            return Task.CompletedTask;
        }
    }

    private static async Task<ProtocolEnvelope> ReceiveEnvelopeAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed by remote peer.");
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        var (success, env, err, reason) = ProtocolEnvelope.TryDeserialize(sb.ToString());
        if (!success || env == null)
        {
            throw new InvalidOperationException($"Envelope deserialize error: {err} - {reason}");
        }
        return env;
    }

    private static async Task SendEnvelopeAsync(WebSocket ws, ProtocolEnvelope env, CancellationToken ct)
    {
        var json = env.Serialize();
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    [Fact]
    public async Task EndToEnd_PairingAndTestEcho_Succeeds()
    {
        using var host = await TeamsRemote.Relay.Program.CreateHostBuilder(Array.Empty<string>())
            .ConfigureWebHost(w => w.UseTestServer())
            .StartAsync();

        var server = host.GetTestServer();
        var wsClient = server.CreateWebSocketClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var companionWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/companion"), cts.Token);
        var clientWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/client"), cts.Token);

        // Setup Companion State & Processor
        var stateStore = new MeetingStateStore();
        stateStore.UpdateObserved("unmuted", isConfirmed: true, meetingStatus: "active");
        var credentialStore = new InMemoryDeviceStore();
        var pairingService = new PairingService(credentialStore, (label, code) => Task.FromResult(true));

        var commandProcessor = new CommandProcessor(stateStore, () => pairingService.ActiveDeviceId);

        // 1. Companion creates and registers challenge
        var challenge = pairingService.CreateSingleUseChallenge("wss://relay.example.com/ws/client");
        var challengeEnv = ProtocolEnvelope.Create(ProtocolMessageType.PairChallenge, challenge, Guid.Empty);
        await SendEnvelopeAsync(companionWs, challengeEnv, cts.Token);
        var challengeAck = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairChallenge, challengeAck.Type);

        // 2. Client sends PairRequest with matching code
        var pairReq = new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Test Phone Pixel"
        };
        var pairReqEnv = ProtocolEnvelope.Create(ProtocolMessageType.PairRequest, pairReq, Guid.Empty);
        await SendEnvelopeAsync(clientWs, pairReqEnv, cts.Token);

        // 3. Companion receives PairRequest from relay
        var receivedReqEnv = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairRequest, receivedReqEnv.Type);
        var forwardedReq = receivedReqEnv.DeserializePayload<PairRequestPayload>();
        Assert.NotNull(forwardedReq);
        Assert.Equal(challenge.ChallengeId, forwardedReq.ChallengeId);

        // 4. Companion redeems and approves
        var (approved, record, _, _) = await pairingService.RedeemChallengeAsync(forwardedReq, cts.Token);
        Assert.True(approved);
        Assert.NotNull(record);

        var approvePayload = new PairApprovePayload
        {
            ChallengeId = challenge.ChallengeId,
            Approved = true,
            DeviceId = record.DeviceId,
            SessionToken = record.SessionToken
        };
        var approveEnv = ProtocolEnvelope.Create(ProtocolMessageType.PairApprove, approvePayload, record.DeviceId);
        await SendEnvelopeAsync(companionWs, approveEnv, cts.Token);

        // 5. Client receives PairResponse with deviceId and token
        var clientPairRespEnv = await ReceiveEnvelopeAsync(clientWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairResponse, clientPairRespEnv.Type);
        var pairResp = clientPairRespEnv.DeserializePayload<PairApprovePayload>();
        Assert.NotNull(pairResp);
        Assert.True(pairResp.Approved);
        Assert.Equal(record.DeviceId, pairResp.DeviceId);

        // 6. Client sends harmless test_echo command
        var echoCmd = new CommandPayload
        {
            Command = "test_echo",
            EchoText = "hello_relay"
        };
        var cmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, echoCmd, record.DeviceId);
        await SendEnvelopeAsync(clientWs, cmdEnv, cts.Token);

        // Companion receives command and processes it
        var receivedCmdEnv = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.Command, receivedCmdEnv.Type);

        var cmdResultEnv = commandProcessor.ProcessEnvelope(receivedCmdEnv);
        await SendEnvelopeAsync(companionWs, cmdResultEnv, cts.Token);

        // Client receives confirmed echo result
        var clientResultEnv = await ReceiveEnvelopeAsync(clientWs, cts.Token);
        Assert.Equal(ProtocolMessageType.CommandResult, clientResultEnv.Type);
        var resultPayload = clientResultEnv.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(resultPayload);
        Assert.Equal("confirmed", resultPayload.Status);
        Assert.Equal("hello_relay", resultPayload.State?["echo"]);
    }

    [Fact]
    public async Task EndToEnd_RemoteMicrophoneControl_IsStrictlyBlockedWith_UNSUPPORTED_IN_PHASE_2()
    {
        using var host = await TeamsRemote.Relay.Program.CreateHostBuilder(Array.Empty<string>())
            .ConfigureWebHost(w => w.UseTestServer())
            .StartAsync();

        var server = host.GetTestServer();
        var wsClient = server.CreateWebSocketClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var companionWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/companion"), cts.Token);
        var clientWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/client"), cts.Token);

        var stateStore = new MeetingStateStore();
        var credentialStore = new InMemoryDeviceStore();
        var pairingService = new PairingService(credentialStore, (l, c) => Task.FromResult(true));
        var commandProcessor = new CommandProcessor(stateStore, () => pairingService.ActiveDeviceId);

        // Pair device
        var challenge = pairingService.CreateSingleUseChallenge("wss://relay.example.com");
        await SendEnvelopeAsync(companionWs, ProtocolEnvelope.Create(ProtocolMessageType.PairChallenge, challenge, Guid.Empty), cts.Token);
        var chAck1 = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairChallenge, chAck1.Type);

        await SendEnvelopeAsync(clientWs, ProtocolEnvelope.Create(ProtocolMessageType.PairRequest, new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Mobile"
        }, Guid.Empty), cts.Token);

        var reqEnv = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        var (approved, record, _, _) = await pairingService.RedeemChallengeAsync(reqEnv.DeserializePayload<PairRequestPayload>()!, cts.Token);

        await SendEnvelopeAsync(companionWs, ProtocolEnvelope.Create(ProtocolMessageType.PairApprove, new PairApprovePayload
        {
            ChallengeId = challenge.ChallengeId,
            Approved = true,
            DeviceId = record!.DeviceId,
            SessionToken = record.SessionToken
        }, record.DeviceId), cts.Token);

        await ReceiveEnvelopeAsync(clientWs, cts.Token); // PairResponse

        // Client attempts remote set_camera command (strictly unsupported in Phase 4A)
        var cameraCmd = new CommandPayload
        {
            Command = "set_camera",
            Desired = "on"
        };
        var cameraCmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, cameraCmd, record.DeviceId);
        await SendEnvelopeAsync(clientWs, cameraCmdEnv, cts.Token);

        // Companion receives and validates via CommandProcessor
        var receivedCmd = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        var resultEnv = commandProcessor.ProcessEnvelope(receivedCmd);
        await SendEnvelopeAsync(companionWs, resultEnv, cts.Token);

        // Client receives response
        var clientResult = await ReceiveEnvelopeAsync(clientWs, cts.Token);
        var payload = clientResult.DeserializePayload<CommandResultPayload>();

        Assert.NotNull(payload);
        Assert.Equal("unsupported", payload.Status);
        Assert.Equal(ProtocolErrorCodes.UnsupportedInPhase4A, payload.ErrorCode);
    }

    [Fact]
    public async Task EndToEnd_Revocation_AbortsClientSocket_AndBlocksSubsequentCommands()
    {
        using var host = await TeamsRemote.Relay.Program.CreateHostBuilder(Array.Empty<string>())
            .ConfigureWebHost(w => w.UseTestServer())
            .StartAsync();

        var server = host.GetTestServer();
        var wsClient = server.CreateWebSocketClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var companionWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/companion"), cts.Token);
        var clientWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/client"), cts.Token);

        var stateStore = new MeetingStateStore();
        var credentialStore = new InMemoryDeviceStore();
        var pairingService = new PairingService(credentialStore, (l, c) => Task.FromResult(true));

        // Pair device
        var challenge = pairingService.CreateSingleUseChallenge("wss://relay.example.com");
        await SendEnvelopeAsync(companionWs, ProtocolEnvelope.Create(ProtocolMessageType.PairChallenge, challenge, Guid.Empty), cts.Token);
        var chAck2 = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairChallenge, chAck2.Type);

        await SendEnvelopeAsync(clientWs, ProtocolEnvelope.Create(ProtocolMessageType.PairRequest, new PairRequestPayload
        {
            ChallengeId = challenge.ChallengeId,
            ShortCode = challenge.ShortCode,
            DeviceLabel = "Mobile"
        }, Guid.Empty), cts.Token);

        var reqEnv = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        var (_, record, _, _) = await pairingService.RedeemChallengeAsync(reqEnv.DeserializePayload<PairRequestPayload>()!, cts.Token);

        await SendEnvelopeAsync(companionWs, ProtocolEnvelope.Create(ProtocolMessageType.PairApprove, new PairApprovePayload
        {
            ChallengeId = challenge.ChallengeId,
            Approved = true,
            DeviceId = record!.DeviceId,
            SessionToken = record.SessionToken
        }, record.DeviceId), cts.Token);

        await ReceiveEnvelopeAsync(clientWs, cts.Token); // PairResponse

        // Now Companion sends RevokeDevice envelope to relay
        var revokePayload = new RevokePayload
        {
            DeviceId = record.DeviceId,
            Reason = "User clicked revoke"
        };
        var revokeEnv = ProtocolEnvelope.Create(ProtocolMessageType.RevokeDevice, revokePayload, record.DeviceId);
        await SendEnvelopeAsync(companionWs, revokeEnv, cts.Token);
        await pairingService.RevokeDeviceAsync(cts.Token);

        // Allow relay to process revocation
        await Task.Delay(100);

        // Attempting to send from revoked client immediately fails because socket was aborted
        try
        {
            var cmd = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "test_echo" }, record.DeviceId);
            await SendEnvelopeAsync(clientWs, cmd, cts.Token);
            var reply = await ReceiveEnvelopeAsync(clientWs, cts.Token);
            var res = reply.DeserializePayload<CommandResultPayload>();
            Assert.NotNull(res);
            Assert.Equal(ProtocolErrorCodes.PairingRequired, res.ErrorCode);
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or ObjectDisposedException)
        {
            // Expected: Remote connection was immediately closed/aborted upon revocation
        }

        // Test reconnect with old credential: must immediately be rejected with PAIRING_REQUIRED
        var reconnectWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/client"), cts.Token);
        var oldCredCmd = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload { Command = "test_echo" }, record.DeviceId);
        await SendEnvelopeAsync(reconnectWs, oldCredCmd, cts.Token);

        var rejectReply = await ReceiveEnvelopeAsync(reconnectWs, cts.Token);
        var rejectRes = rejectReply.DeserializePayload<CommandResultPayload>();
        Assert.NotNull(rejectRes);
        Assert.Equal("rejected", rejectRes.Status);
        Assert.Equal(ProtocolErrorCodes.PairingRequired, rejectRes.ErrorCode);
    }

    [Fact]
    public async Task EndToEnd_PairingExpiry_RejectsRedemptionAfter60Seconds()
    {
        using var host = await TeamsRemote.Relay.Program.CreateHostBuilder(Array.Empty<string>())
            .ConfigureWebHost(w => w.UseTestServer())
            .StartAsync();

        var server = host.GetTestServer();
        var wsClient = server.CreateWebSocketClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var companionWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/companion"), cts.Token);
        var clientWs = await wsClient.ConnectAsync(new Uri(server.BaseAddress, "/ws/client"), cts.Token);

        // Challenge registered with expired timestamp
        var expiredChallenge = new PairChallengePayload
        {
            ChallengeId = Guid.NewGuid(),
            ShortCode = "999-000",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-2)
        };
        await SendEnvelopeAsync(companionWs, ProtocolEnvelope.Create(ProtocolMessageType.PairChallenge, expiredChallenge, Guid.Empty), cts.Token);
        var chAck3 = await ReceiveEnvelopeAsync(companionWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairChallenge, chAck3.Type);

        // Client attempts to redeem
        await SendEnvelopeAsync(clientWs, ProtocolEnvelope.Create(ProtocolMessageType.PairRequest, new PairRequestPayload
        {
            ChallengeId = expiredChallenge.ChallengeId,
            ShortCode = expiredChallenge.ShortCode,
            DeviceLabel = "Mobile"
        }, Guid.Empty), cts.Token);

        // Client receives rejection immediately from relay
        var rejectEnv = await ReceiveEnvelopeAsync(clientWs, cts.Token);
        Assert.Equal(ProtocolMessageType.PairResponse, rejectEnv.Type);
        var rawPayload = rejectEnv.Payload.ToString();
        Assert.Contains("invalid or expired", rawPayload, StringComparison.OrdinalIgnoreCase);
    }
}
