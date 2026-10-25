using System.Net.WebSockets;
using System.Text;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Relay.Routing;

namespace TeamsRemote.Relay;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                bool devLan = string.Equals(
                    Environment.GetEnvironmentVariable("TEAMS_REMOTE_DEV_LAN"),
                    "true",
                    StringComparison.OrdinalIgnoreCase);

                if (devLan)
                {
                    webBuilder.UseUrls("http://0.0.0.0:5100;http://127.0.0.1:5100");
                }
                else
                {
                    webBuilder.UseUrls("http://127.0.0.1:5100");
                }

                string? devPwaOrigin = Environment.GetEnvironmentVariable("TEAMS_REMOTE_DEV_PWA_ORIGIN");

                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<DeviceRouteManager>();
                    services.AddCors(options =>
                    {
                        options.AddDefaultPolicy(policy =>
                        {
                            var origins = new List<string>
                            {
                                "http://localhost:5173",
                                "http://127.0.0.1:5173"
                            };

                            if (!string.IsNullOrWhiteSpace(devPwaOrigin) &&
                                Uri.TryCreate(devPwaOrigin.Trim(), UriKind.Absolute, out var uri))
                            {
                                origins.Add(uri.GetLeftPart(UriPartial.Authority));
                            }

                            policy.WithOrigins(origins.ToArray())
                                  .AllowAnyHeader()
                                  .AllowAnyMethod();
                        });
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseCors();

                    var routeManager = app.ApplicationServices.GetRequiredService<DeviceRouteManager>();

                    app.UseWebSockets(new WebSocketOptions
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(15)
                    });

                    app.Use(async (context, next) =>
                    {
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            string path = context.Request.Path.ToString().ToLowerInvariant();

                            if (path == "/ws/companion")
                            {
                                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                var session = new RelaySession(webSocket, SessionType.Companion);
                                routeManager.RegisterSession(session);

                                string? companionDeviceIdStr = context.Request.Query["deviceId"];
                                string? companionToken = context.Request.Query["token"];
                                if (Guid.TryParse(companionDeviceIdStr, out var compDevId) && !string.IsNullOrEmpty(companionToken))
                                {
                                    routeManager.RegisterCompanionRoute(session, compDevId, companionToken);
                                }

                                try
                                {
                                    await HandleSessionLoopAsync(session, routeManager, context.RequestAborted);
                                }
                                finally
                                {
                                    routeManager.UnregisterSession(session.SessionId);
                                }
                                return;
                            }
                            else if (path == "/ws/client")
                            {
                                using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                var session = new RelaySession(webSocket, SessionType.Client);
                                routeManager.RegisterSession(session);

                                string? deviceIdStr = context.Request.Query["deviceId"];
                                string? token = context.Request.Query["token"];
                                if (Guid.TryParse(deviceIdStr, out var devId) && !string.IsNullOrEmpty(token))
                                {
                                    routeManager.TryResumeClientSession(session, devId, token);
                                }

                                try
                                {
                                    await HandleSessionLoopAsync(session, routeManager, context.RequestAborted);
                                }
                                finally
                                {
                                    routeManager.UnregisterSession(session.SessionId);
                                }
                                return;
                            }
                        }

                        await next();
                    });

                    app.Run(async context =>
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("Teams Mobile Remote Authenticated Relay Service (Phase 2)");
                    });
                });
            });

    private static async Task HandleSessionLoopAsync(
        RelaySession session,
        DeviceRouteManager routeManager,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
        {
            sb.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await session.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await session.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                    return;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            string rawJson = sb.ToString();

            // Validate message size and envelope
            var (success, envelope, errorCode, errorReason) = ProtocolEnvelope.TryDeserialize(rawJson);
            if (!success || envelope == null)
            {
                var errorEnvelope = ProtocolEnvelope.Create(
                    ProtocolMessageType.CommandResult,
                    new CommandResultPayload
                    {
                        Status = "rejected",
                        ErrorCode = errorCode ?? "INVALID_ENVELOPE",
                        Reason = errorReason ?? "Message failed validation."
                    },
                    Guid.Empty);

                await session.SendMessageAsync(errorEnvelope.Serialize(), cancellationToken);
                continue;
            }

            // Handle pairing control messages
            if (envelope.Type == ProtocolMessageType.PairChallenge && session.Type == SessionType.Companion)
            {
                var challenge = envelope.DeserializePayload<PairChallengePayload>();
                if (challenge != null)
                {
                    routeManager.RegisterChallenge(challenge, session.SessionId);
                    var ackEnvelope = ProtocolEnvelope.Create(
                        ProtocolMessageType.PairChallenge,
                        new { registered = true, challengeId = challenge.ChallengeId },
                        Guid.Empty,
                        correlationId: envelope.MessageId);
                    await session.SendMessageAsync(ackEnvelope.Serialize(), cancellationToken);
                }
                continue;
            }

            if (envelope.Type == ProtocolMessageType.PairRequest && session.Type == SessionType.Client)
            {
                var request = envelope.DeserializePayload<PairRequestPayload>();
                if (request != null)
                {
                    bool forwarded = await routeManager.ProcessPairRequestAsync(session, request);
                    if (!forwarded)
                    {
                        var rejectEnvelope = ProtocolEnvelope.Create(
                            ProtocolMessageType.PairResponse,
                            new { approved = false, reason = "Pairing challenge invalid or expired." },
                            Guid.Empty);
                        await session.SendMessageAsync(rejectEnvelope.Serialize(), cancellationToken);
                    }
                }
                continue;
            }

            if (envelope.Type == ProtocolMessageType.PairApprove && session.Type == SessionType.Companion)
            {
                var approve = envelope.DeserializePayload<PairApprovePayload>();
                if (approve != null)
                {
                    await routeManager.ProcessPairApproveAsync(session, approve);
                }
                continue;
            }

            if (envelope.Type == ProtocolMessageType.RevokeDevice && session.Type == SessionType.Companion)
            {
                var revoke = envelope.DeserializePayload<RevokePayload>();
                if (revoke != null)
                {
                    routeManager.RevokeDevice(revoke.DeviceId);
                }
                continue;
            }

            // Normal device-scoped message routing
            bool routed = await routeManager.RouteMessageAsync(session, envelope);
            if (!routed && session.Type == SessionType.Client)
            {
                // Reject unauthorized or cross-device attempt
                var rejectEnvelope = ProtocolEnvelope.Create(
                    ProtocolMessageType.CommandResult,
                    new CommandResultPayload
                    {
                        Status = "rejected",
                        ErrorCode = ProtocolErrorCodes.PairingRequired,
                        Reason = "Device route not found or session revoked."
                    },
                    envelope.DeviceId,
                    envelope.CorrelationId);

                await session.SendMessageAsync(rejectEnvelope.Serialize(), cancellationToken);
            }
        }
    }
}
