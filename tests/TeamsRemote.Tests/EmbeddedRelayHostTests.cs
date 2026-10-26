using System.Net;
using System.Reflection;
using TeamsRemote.Companion.AppHost;
using TeamsRemote.Core.Protocol;
using Xunit;

namespace TeamsRemote.Tests;

public class EmbeddedRelayHostTests
{
    [Fact]
    public void Assemblies_InheritCentralVersionFromDirectoryBuildProps()
    {
        var coreAsm = typeof(ProtocolEnvelope).Assembly;
        var version = coreAsm.GetName().Version;

        Assert.NotNull(version);
        Assert.Equal(0, version.Major);
        Assert.Equal(9, version.Minor);
        Assert.Equal(0, version.Build);

        var infoVerAttr = coreAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(infoVerAttr);
        Assert.Contains("0.9.0-beta.1", infoVerAttr.InformationalVersion);
    }

    [Fact]
    public async Task EmbeddedRelayHost_StartsAndReportsHealthyOnLoopback()
    {
        int testPort = 5198;
        string listenUrl = $"http://127.0.0.1:{testPort}";
        var host = new EmbeddedRelayHost(listenUrl);

        try
        {
            await host.StartAsync();
            Assert.True(host.IsRunning);

            bool isHealthy = await host.WaitForHealthAsync(TimeSpan.FromSeconds(5));
            Assert.True(isHealthy, "EmbeddedRelayHost must report healthy via /health endpoint");

            using var client = new HttpClient();
            var response = await client.GetAsync($"{listenUrl}/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Assert.Contains("healthy", content);
        }
        finally
        {
            await host.StopAsync();
            Assert.False(host.IsRunning);
        }
    }

    [Fact]
    public async Task EmbeddedRelayHost_ServesIndexOrFallbackOnRootPath()
    {
        int testPort = 5197;
        string listenUrl = $"http://127.0.0.1:{testPort}";
        var host = new EmbeddedRelayHost(listenUrl);

        try
        {
            await host.StartAsync();
            bool isHealthy = await host.WaitForHealthAsync(TimeSpan.FromSeconds(5));
            Assert.True(isHealthy);

            using var client = new HttpClient();
            var response = await client.GetAsync(listenUrl);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string content = await response.Content.ReadAsStringAsync();
            Assert.True(content.Contains("Teams") || content.Contains("html"));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
