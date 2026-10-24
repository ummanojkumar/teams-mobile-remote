using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TeamsRemote.Core.Abstractions;
using TeamsRemote.Core.Adapters;
using TeamsRemote.Core.Protocol;
using TeamsRemote.Diagnostics.Formatting;

namespace TeamsRemote.Diagnostics;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool isTestPhoneMode = args.Any(a => string.Equals(a, "test-phone", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "test-client", StringComparison.OrdinalIgnoreCase));
        if (isTestPhoneMode)
        {
            return await RunTestPhoneClientAsync(args);
        }

        bool outputJson = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-j", StringComparison.OrdinalIgnoreCase));
        bool isObserveMode = args.Any(a => string.Equals(a, "observe", StringComparison.OrdinalIgnoreCase));
        bool showHelp = args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase));

        if (showHelp)
        {
            PrintHelp();
            return 0;
        }

        using ITeamsController controller = new UiaTeamsController();

        if (isObserveMode)
        {
            int durationSeconds = 30;
            int durIndex = Array.FindIndex(args, a => string.Equals(a, "--duration", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "-d", StringComparison.OrdinalIgnoreCase));
            if (durIndex >= 0 && durIndex + 1 < args.Length && int.TryParse(args[durIndex + 1], out int parsedDuration))
            {
                durationSeconds = Math.Clamp(parsedDuration, 5, 300);
            }

            Console.WriteLine();
            Console.WriteLine($"Starting passive state-change observation for {durationSeconds} seconds...");
            Console.WriteLine("Press Ctrl+C to cancel at any time. No actions or clicks will be sent to Teams.");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            int eventsCount = 0;
            await controller.ObserveEventsAsync(
                TimeSpan.FromSeconds(durationSeconds),
                evt =>
                {
                    Interlocked.Increment(ref eventsCount);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{evt.ObservedAt:HH:mm:ss.fff}] ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(evt.RedactedSummary);
                    Console.ResetColor();
                },
                cts.Token);

            Console.WriteLine();
            Console.WriteLine($"Observation finished. Total state-change events observed: {eventsCount}.");
            return 0;
        }

        // Default: Run inspection report
        var report = await controller.InspectAsync();

        if (outputJson)
        {
            ReportConsoleFormatter.PrintJson(report);
        }
        else
        {
            ReportConsoleFormatter.PrintReport(report);
        }

        return 0;
    }

    private static async Task<int> RunTestPhoneClientAsync(string[] args)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==========================================================");
        Console.WriteLine("  Teams Mobile Remote — Phone Simulator (Phase 2 Transport) ");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();

        Guid challengeId = Guid.Empty;
        string shortCode = "";
        string relayUrl = "ws://127.0.0.1:5100/ws/client";

        int cmdIdx = Array.FindIndex(args, a => a.Equals("test-phone", StringComparison.OrdinalIgnoreCase) || a.Equals("test-client", StringComparison.OrdinalIgnoreCase));
        string[] remainingArgs = cmdIdx >= 0 ? args.Skip(cmdIdx + 1).ToArray() : Array.Empty<string>();

        if (remainingArgs.Length > 0 && remainingArgs[0].Trim().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(remainingArgs[0]);
                if (doc.RootElement.TryGetProperty("challengeId", out var cId)) challengeId = cId.GetGuid();
                if (doc.RootElement.TryGetProperty("shortCode", out var sc)) shortCode = sc.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("relayUrl", out var ru)) relayUrl = ru.GetString() ?? relayUrl;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse JSON argument: {ex.Message}");
            }
        }
        else if (remainingArgs.Length >= 2 && Guid.TryParse(remainingArgs[0], out var parsedGuid))
        {
            challengeId = parsedGuid;
            shortCode = remainingArgs[1].Trim();
        }

        if (challengeId == Guid.Empty || string.IsNullOrWhiteSpace(shortCode))
        {
            Console.WriteLine("Paste the pairing JSON payload from the Companion (or enter Challenge ID GUID):");
            Console.Write("> ");
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Error: Input cannot be empty.");
                return 1;
            }

            if (input.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(input);
                    if (doc.RootElement.TryGetProperty("challengeId", out var cId)) challengeId = cId.GetGuid();
                    if (doc.RootElement.TryGetProperty("shortCode", out var sc)) shortCode = sc.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("relayUrl", out var ru)) relayUrl = ru.GetString() ?? relayUrl;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON: {ex.Message}");
                    return 1;
                }
            }
            else if (Guid.TryParse(input, out var parsedGuid))
            {
                challengeId = parsedGuid;
                Console.Write("Enter matching confirmation code (e.g. 123-456): ");
                shortCode = Console.ReadLine()?.Trim() ?? "";
            }
            else
            {
                Console.WriteLine("Error: Unrecognized input format. Expected GUID or JSON.");
                return 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[1/5] Connecting to relay at {relayUrl}...");
        using var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            await ws.ConnectAsync(new Uri(relayUrl), cts.Token);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("       Connected outbound to relay successfully.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"       Failed to connect to relay: {ex.Message}");
            Console.WriteLine("       Make sure the relay is running: dotnet run --project src/TeamsRemote.Relay");
            Console.ResetColor();
            return 1;
        }

        // Send PairRequest
        Console.WriteLine($"[2/5] Sending pairing request for code [{shortCode}]...");
        var pairReq = new PairRequestPayload
        {
            ChallengeId = challengeId,
            ShortCode = shortCode,
            DeviceLabel = $"Manual Test Device ({Environment.MachineName})"
        };
        var pairReqEnv = ProtocolEnvelope.Create(ProtocolMessageType.PairRequest, pairReq, Guid.Empty);
        await SendAsync(ws, pairReqEnv, cts.Token);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("       --> Switch to the Companion window and click 'Approve' on the prompt!");
        Console.ResetColor();

        // Await PairResponse
        var pairRespEnv = await ReceiveAsync(ws, cts.Token);
        if (pairRespEnv == null || pairRespEnv.Type != ProtocolMessageType.PairResponse)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"       Pairing failed or timed out. Received: {pairRespEnv?.Type}");
            Console.ResetColor();
            return 1;
        }

        var pairApprove = pairRespEnv.DeserializePayload<PairApprovePayload>();
        if (pairApprove == null || !pairApprove.Approved)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("       Pairing was declined, expired, or rejected.");
            Console.ResetColor();
            return 1;
        }

        Guid deviceId = pairApprove.DeviceId;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[3/5] Pairing CONFIRMED! Issued Device ID: {deviceId}");
        Console.ResetColor();

        // 4. Test Echo
        Console.WriteLine("[4/5] Testing transport round-trip: sending 'test_echo' command...");
        var echoEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
        {
            Command = "test_echo",
            EchoText = "Hello from phone simulator!"
        }, deviceId);
        await SendAsync(ws, echoEnv, cts.Token);

        var echoRespEnv = await ReceiveAsync(ws, cts.Token);
        var echoResult = echoRespEnv?.DeserializePayload<CommandResultPayload>();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"       Echo Result: Status='{echoResult?.Status}', Echo='{echoResult?.State?["echo"]}'");
        Console.ResetColor();

        // Query State
        Console.WriteLine("       Sending 'query_state' command...");
        var queryEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
        {
            Command = "query_state"
        }, deviceId);
        await SendAsync(ws, queryEnv, cts.Token);

        var queryRespEnv = await ReceiveAsync(ws, cts.Token);
        var queryResult = queryRespEnv?.DeserializePayload<CommandResultPayload>();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"       State Snapshot: Meeting='{queryResult?.State?["meeting"]}', Mic='{queryResult?.State?["microphone"]}', Conf='{queryResult?.State?["confidence"]}'");
        Console.ResetColor();

        // 5. Test Remote Control Safety Barrier
        Console.WriteLine("[5/5] Testing Phase 2 Safety Barrier: sending remote 'set_microphone' command...");
        var micCmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
        {
            Command = "set_microphone",
            Desired = "muted"
        }, deviceId);
        await SendAsync(ws, micCmdEnv, cts.Token);

        var micRespEnv = await ReceiveAsync(ws, cts.Token);
        var micResult = micRespEnv?.DeserializePayload<CommandResultPayload>();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"       Result: Status='{micResult?.Status}', ErrorCode='{micResult?.ErrorCode}', Reason='{micResult?.Reason}'");
        Console.ResetColor();

        Console.WriteLine();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("==========================================================");
        Console.WriteLine("  Interactive Phone Simulator Ready!");
        Console.WriteLine("==========================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("You can now send commands manually across the transport:");
        Console.WriteLine("  echo <text>         -> Send 'test_echo' with your custom message");
        Console.WriteLine("  query               -> Send 'query_state' to fetch Teams meeting status");
        Console.WriteLine("  mic <muted|unmuted> -> Send 'set_microphone' (verify UNSUPPORTED_IN_PHASE_2)");
        Console.WriteLine("  exit                -> Disconnect and exit");
        Console.WriteLine();
        Console.WriteLine("Tip: You can also click 'Revoke Phone' in the Companion UI at any time.");
        Console.WriteLine();

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                Console.Write("Phone> ");
                string? line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (line.StartsWith("echo ", StringComparison.OrdinalIgnoreCase) || line.Equals("echo", StringComparison.OrdinalIgnoreCase))
                {
                    string echoText = line.Length > 5 ? line.Substring(5).Trim() : "ping";
                    var cmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
                    {
                        Command = "test_echo",
                        EchoText = echoText
                    }, deviceId);
                    await SendAsync(ws, cmdEnv, CancellationToken.None);

                    var reply = await ReceiveAsync(ws, CancellationToken.None);
                    var res = reply?.DeserializePayload<CommandResultPayload>();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[test_echo reply] Status='{res?.Status}', Echo='{res?.State?["echo"]}' (StateRev: {res?.StateRevision})");
                    Console.ResetColor();
                }
                else if (line.Equals("query", StringComparison.OrdinalIgnoreCase))
                {
                    var cmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
                    {
                        Command = "query_state"
                    }, deviceId);
                    await SendAsync(ws, cmdEnv, CancellationToken.None);

                    var reply = await ReceiveAsync(ws, CancellationToken.None);
                    var res = reply?.DeserializePayload<CommandResultPayload>();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[query_state reply] Meeting='{res?.State?["meeting"]}', Mic='{res?.State?["microphone"]}', Confidence='{res?.State?["confidence"]}' (StateRev: {res?.StateRevision})");
                    Console.ResetColor();
                }
                else if (line.StartsWith("mic", StringComparison.OrdinalIgnoreCase))
                {
                    string desired = line.Contains("unmute", StringComparison.OrdinalIgnoreCase) ? "unmuted" : "muted";
                    var cmdEnv = ProtocolEnvelope.Create(ProtocolMessageType.Command, new CommandPayload
                    {
                        Command = "set_microphone",
                        Desired = desired
                    }, deviceId);
                    await SendAsync(ws, cmdEnv, CancellationToken.None);

                    var reply = await ReceiveAsync(ws, CancellationToken.None);
                    var res = reply?.DeserializePayload<CommandResultPayload>();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[set_microphone reply] Status='{res?.Status}', ErrorCode='{res?.ErrorCode}' - {res?.Reason}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("Commands: echo <text>, query, mic [muted|unmuted], exit");
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[Transport Notification] Connection terminated by relay/companion: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine("Test phone client finished.");
        return 0;
    }

    private static async Task SendAsync(WebSocket ws, ProtocolEnvelope env, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(env.Serialize());
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task<ProtocolEnvelope?> ReceiveAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        var (success, env, _, _) = ProtocolEnvelope.TryDeserialize(sb.ToString());
        return success ? env : null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Teams Mobile Remote — Diagnostic Tool");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  report (default)     Inspects Teams desktop and prints capability matrix");
        Console.WriteLine("  observe              Passively listens for state-change events without clicking");
        Console.WriteLine("  test-phone           Simulates a mobile phone pairing and transport connection");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --json, -j           Output diagnostic report as formatted JSON");
        Console.WriteLine("  --duration, -d <sec> Duration in seconds for observation mode (default: 30)");
        Console.WriteLine("  --help, -h           Show this help information");
        Console.WriteLine();
    }
}
