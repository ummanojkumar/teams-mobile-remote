using System.Net.WebSockets;
using System.Text;

namespace TeamsRemote.Relay.Routing;

public enum SessionType
{
    Companion,
    Client
}

public sealed class RelaySession
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public SessionType Type { get; }
    public WebSocket Socket { get; }
    public Guid? PairedDeviceId { get; set; }
    public string? DeviceLabel { get; set; }
    public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public RelaySession(WebSocket socket, SessionType type)
    {
        Socket = socket;
        Type = type;
    }

    public async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (Socket.State != WebSocketState.Open) return;

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
