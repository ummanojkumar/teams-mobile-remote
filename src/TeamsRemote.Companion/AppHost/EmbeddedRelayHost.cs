using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Hosting;

namespace TeamsRemote.Companion.AppHost;

public sealed class EmbeddedRelayHost : IAsyncDisposable, IDisposable
{
    private string _listenUrls;
    private IHost? _host;
    private bool _isDisposed;
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public Uri ListenUri => new(_listenUrls.Split(';')[0]);
    public Uri? LanListenUri { get; private set; }
    public string CurrentListenUrls => _listenUrls;
    public bool IsRunning => _host != null;

    public EmbeddedRelayHost(string listenUrl = "http://127.0.0.1:5100")
    {
        _listenUrls = listenUrl;
        var parts = listenUrl.Split(';');
        if (parts.Length > 1 && Uri.TryCreate(parts[1], UriKind.Absolute, out var lanUri))
        {
            LanListenUri = lanUri;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null) return;

        // Check if an existing relay service is already running on loopback
        if (await IsHealthyAsync(cancellationToken))
        {
            Trace.WriteLine($"EmbeddedRelayHost: Existing healthy Relay detected on {_listenUrls}.");
            return;
        }

        try
        {
            var hostBuilder = Relay.Program.CreateHostBuilder(Array.Empty<string>(), _listenUrls);
            _host = hostBuilder.Build();
            await _host.StartAsync(cancellationToken);
            Trace.WriteLine($"EmbeddedRelayHost: In-process Kestrel started successfully on {_listenUrls}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"EmbeddedRelayHost: Failed to start in-process Kestrel: {ex.Message}");
            throw;
        }
    }

    public async Task RebindAsync(string? lanIpAddress = null, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(lanIpAddress))
        {
            _listenUrls = "http://127.0.0.1:5100";
            LanListenUri = null;
        }
        else
        {
            // Strictly bind loopback and chosen RFC 1918 LAN IP (never 0.0.0.0)
            _listenUrls = $"http://127.0.0.1:5100;http://{lanIpAddress}:5100";
            LanListenUri = new Uri($"http://{lanIpAddress}:5100");
        }

        await StartAsync(cancellationToken);
        await WaitForHealthAsync(TimeSpan.FromSeconds(3), cancellationToken);
    }

    public async Task<bool> WaitForHealthAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (await IsHealthyAsync(cts.Token))
            {
                return true;
            }

            try
            {
                await Task.Delay(100, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var healthUrl = new Uri(ListenUri, "/health");
            using var response = await HealthClient.GetAsync(healthUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null)
        {
            try
            {
                await _host.StopAsync(cancellationToken);
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_host != null)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            catch
            {
                // Suppress shutdown exceptions during sync dispose
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_host != null)
        {
            await StopAsync();
        }
    }
}
