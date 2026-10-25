using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using TeamsRemote.Core.Pairing;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Core.State;

namespace TeamsRemote.Core.Transport;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

public sealed class ConnectionClient : IAsyncDisposable
{
    private readonly Uri _relayUri;
    private readonly CommandProcessor _commandProcessor;
    private readonly MeetingStateStore _stateStore;
    private readonly PairingService? _pairingService;
    private readonly CancellationTokenSource _disposeCts = new();

    private ClientWebSocket? _webSocket;
    private Task? _heartbeatLoopTask;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? LogMessage;

    public ConnectionClient(
        Uri relayUri,
        CommandProcessor commandProcessor,
        MeetingStateStore stateStore,
        PairingService? pairingService = null)
    {
        _relayUri = relayUri;
        _commandProcessor = commandProcessor;
        _stateStore = stateStore;
        _pairingService = pairingService;
    }

    public async Task StartAsync()
    {
        await ConnectWithBackoffAsync(_disposeCts.Token);
    }

    private async Task ConnectWithBackoffAsync(CancellationToken cancellationToken)
    {
        int attempt = 0;
        int maxDelayMs = 30000;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetStatus(attempt == 0 ? ConnectionStatus.Connecting : ConnectionStatus.Reconnecting);

                _webSocket?.Dispose();
                _webSocket = new ClientWebSocket();

                var targetUri = _relayUri;
                if (_pairingService?.ActiveDevice is { } active)
                {
                    var uriBuilder = new UriBuilder(_relayUri);
                    var query = $"deviceId={Uri.EscapeDataString(active.DeviceId.ToString())}&token={Uri.EscapeDataString(active.SessionToken)}";
                    uriBuilder.Query = string.IsNullOrEmpty(uriBuilder.Query) || uriBuilder.Query == "?"
                        ? query
                        : $"{uriBuilder.Query.TrimStart('?')}&{query}";
                    targetUri = uriBuilder.Uri;
                }

                LogMessage?.Invoke($"Connecting outbound to relay at {targetUri}...");
                await _webSocket.ConnectAsync(targetUri, cancellationToken);

                SetStatus(ConnectionStatus.Connected);
                LogMessage?.Invoke("Successfully connected outbound to relay.");
                attempt = 0;

                // Sync fresh state snapshot on connect/reconnect
                await SendStateSnapshotAsync(cancellationToken);

                // Start loops
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _heartbeatLoopTask = Task.Run(() => HeartbeatLoopAsync(linkedCts.Token), linkedCts.Token);
                await ReceiveLoopAsync(linkedCts.Token);

                // Cancel heartbeat when receive loop terminates
                linkedCts.Cancel();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(ConnectionStatus.Reconnecting);
                LogMessage?.Invoke($"Relay connection dropped or failed: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested) break;

            // Exponential backoff with jitter
            attempt++;
            int baseDelayMs = Math.Min(1000 * (1 << Math.Min(attempt, 5)), maxDelayMs);
            int jitterMs = RandomNumberGenerator.GetInt32(-200, 200);
            int delayMs = Math.Max(500, baseDelayMs + jitterMs);

            LogMessage?.Invoke($"Reconnecting in {delayMs}ms (attempt #{attempt})...");
            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetStatus(ConnectionStatus.Disconnected);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            messageBuilder.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                    return;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            string rawJson = messageBuilder.ToString();
            await HandleIncomingMessageAsync(rawJson, cancellationToken);
        }
    }

    private async Task HandleIncomingMessageAsync(string rawJson, CancellationToken cancellationToken)
    {
        var (success, envelope, errorCode, errorReason) = ProtocolEnvelope.TryDeserialize(rawJson);

        if (!success || envelope == null)
        {
            LogMessage?.Invoke($"Rejected invalid envelope: {errorCode} - {errorReason}");
            return;
        }

        if (envelope.Type == ProtocolMessageType.Ping)
        {
            var pong = ProtocolEnvelope.Create(ProtocolMessageType.Pong, new { }, envelope.DeviceId, envelope.CorrelationId);
            await SendEnvelopeAsync(pong, cancellationToken);
            return;
        }

        if (envelope.Type == ProtocolMessageType.Pong)
        {
            // Heartbeat confirmed
            return;
        }

        if (envelope.Type == ProtocolMessageType.PairRequest && _pairingService != null)
        {
            var req = envelope.DeserializePayload<PairRequestPayload>();
            if (req != null)
            {
                var (approved, record, _, _) = await _pairingService.RedeemChallengeAsync(req, cancellationToken);
                var approvePayload = new PairApprovePayload
                {
                    ChallengeId = req.ChallengeId,
                    Approved = approved,
                    DeviceId = record?.DeviceId ?? Guid.Empty,
                    SessionToken = record?.SessionToken
                };
                var approveEnvelope = ProtocolEnvelope.Create(
                    ProtocolMessageType.PairApprove,
                    approvePayload,
                    record?.DeviceId ?? Guid.Empty,
                    envelope.CorrelationId);
                await SendEnvelopeAsync(approveEnvelope, cancellationToken);
            }
            return;
        }

        if (envelope.Type == ProtocolMessageType.RevokeDevice && _pairingService != null)
        {
            await _pairingService.RevokeDeviceAsync(cancellationToken);
            return;
        }

        if (envelope.Type == ProtocolMessageType.Command)
        {
            // Process command through CommandProcessor (authorization, dedupe, rate-limits, safety)
            var responseEnvelope = _commandProcessor.ProcessEnvelope(envelope);
            await SendEnvelopeAsync(responseEnvelope, cancellationToken);

            var cmdPayload = envelope.DeserializePayload<CommandPayload>();
            if (cmdPayload?.Command == "query_state")
            {
                await SendStateSnapshotAsync(cancellationToken);
            }
        }
    }

    public async Task SendEnvelopeAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        string json = envelope.Serialize();
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    public async Task SendStateSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var (rev, micState, conf, meeting) = _stateStore.GetSnapshot();

        var snapshot = new StateSnapshotPayload
        {
            StateRevision = rev,
            Meeting = meeting,
            Controls = new Dictionary<string, ControlSnapshotItem>
            {
                ["microphone"] = new ControlSnapshotItem
                {
                    Value = micState,
                    Capability = "verified",
                    Confidence = conf.ToString().ToLowerInvariant()
                }
            },
            Connection = "connected",
            ObservedAt = DateTimeOffset.UtcNow
        };

        var deviceId = _pairingService?.ActiveDeviceId ?? Guid.Empty;
        var envelope = ProtocolEnvelope.Create(
            ProtocolMessageType.StateSnapshot,
            snapshot,
            deviceId);

        await SendEnvelopeAsync(envelope, cancellationToken);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);

                if (_webSocket.State == WebSocketState.Open)
                {
                    var ping = ProtocolEnvelope.Create(ProtocolMessageType.Ping, new { }, Guid.Empty);
                    await SendEnvelopeAsync(ping, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private void SetStatus(ConnectionStatus status)
    {
        if (Status != status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposing", CancellationToken.None);
                }
            }
            catch
            {
                // Ignore close error during teardown
            }
            finally
            {
                _webSocket.Dispose();
            }
        }

        _disposeCts.Dispose();
    }
}
