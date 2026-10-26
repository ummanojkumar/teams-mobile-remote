using System.Net;
using TeamsRemote.Companion.AppHost;
using TeamsRemote.Companion.Network;
using TeamsRemote.Companion.Security;
using TeamsRemote.Companion.Settings;
using Xunit;

namespace TeamsRemote.Tests;

public sealed class NetworkAndFirewallTests : IDisposable
{
    private readonly string _tempSettingsDir;
    private readonly string _tempSettingsFile;

    public NetworkAndFirewallTests()
    {
        _tempSettingsDir = Path.Combine(Path.GetTempPath(), "TeamsRemoteTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempSettingsDir);
        _tempSettingsFile = Path.Combine(_tempSettingsDir, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempSettingsDir))
            {
                Directory.Delete(_tempSettingsDir, recursive: true);
            }
        }
        catch
        {
            // Suppress cleanup errors
        }
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.254.10.50")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.20.10.5")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.150")]
    [InlineData("192.168.255.255")]
    public void IsPrivateRfc1918_ValidPrivateAddresses_ReturnsTrue(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        bool isPrivate = NetworkInterfaceScanner.IsPrivateRfc1918(ip);
        Assert.True(isPrivate, $"Expected {ipString} to be recognized as private RFC 1918.");
    }

    [Theory]
    [InlineData("127.0.0.1")]       // Loopback
    [InlineData("127.255.255.255")] // Loopback
    [InlineData("169.254.1.1")]     // APIPA / Link-local
    [InlineData("169.254.255.255")] // APIPA
    [InlineData("8.8.8.8")]         // Public Google DNS
    [InlineData("1.1.1.1")]         // Public Cloudflare
    [InlineData("172.15.255.255")]   // Class B just below 172.16
    [InlineData("172.32.0.1")]       // Class B just above 172.31
    [InlineData("192.167.1.1")]     // Class C just below 192.168
    [InlineData("192.169.1.1")]     // Class C just above 192.168
    [InlineData("224.0.0.1")]       // Multicast
    [InlineData("255.255.255.255")] // Broadcast
    public void IsPrivateRfc1918_InvalidOrPublicAddresses_ReturnsFalse(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        bool isPrivate = NetworkInterfaceScanner.IsPrivateRfc1918(ip);
        Assert.False(isPrivate, $"Expected {ipString} to NOT be recognized as private RFC 1918.");
    }

    [Fact]
    public void IsPrivateRfc1918_IPv6Address_ReturnsFalse()
    {
        var ipv6Loopback = IPAddress.IPv6Loopback;
        Assert.False(NetworkInterfaceScanner.IsPrivateRfc1918(ipv6Loopback));
    }

    [Fact]
    public void BuildCreateRuleScript_EnforcesAllSecurityRestrictions()
    {
        string mockExePath = @"C:\Program Files\TeamsRemote\TeamsRemote.Companion.exe";
        string script = WindowsFirewallService.BuildCreateRuleScript(mockExePath, 5100);

        // 1. Rule Name check
        Assert.Contains($"-DisplayName \"{WindowsFirewallService.RuleName}\"", script);

        // 2. Executable scoping (Never port-only)
        Assert.Contains($"-Program \"{mockExePath}\"", script);

        // 3. Port & Protocol
        Assert.Contains("-LocalPort 5100", script);
        Assert.Contains("-Protocol TCP", script);
        Assert.Contains("-Direction Inbound", script);

        // 4. Scoped to Private Profile ONLY
        Assert.Contains("-Profile Private", script);
        Assert.DoesNotContain("Public", script);
        Assert.DoesNotContain("Domain", script);

        // 5. Restrict to LocalSubnet
        Assert.Contains("-RemoteAddress LocalSubnet", script);

        // 6. Action Allow
        Assert.Contains("-Action Allow", script);
    }

    [Fact]
    public void BuildRemoveRuleScript_TargetsAppOwnedRuleOnly()
    {
        string script = WindowsFirewallService.BuildRemoveRuleScript();

        Assert.Contains($"-DisplayName \"{WindowsFirewallService.RuleName}\"", script);
        Assert.Contains("-ErrorAction SilentlyContinue", script);
        Assert.StartsWith("Remove-NetFirewallRule", script);
    }

    [Fact]
    public void EmbeddedRelayHost_ListenEndpoints_NeverIncludeWildcard()
    {
        var host = new EmbeddedRelayHost("http://127.0.0.1:5100");

        // Loopback initial state
        Assert.Equal("http://127.0.0.1:5100", host.CurrentListenUrls);
        Assert.Equal(new Uri("http://127.0.0.1:5100"), host.ListenUri);
        Assert.Null(host.LanListenUri);

        // Ensure wildcard is never generated
        Assert.DoesNotContain("0.0.0.0", host.CurrentListenUrls);
    }

    [Fact]
    public async Task CompanionSettingsManager_RoundtripsSuccessfully()
    {
        var manager = new CompanionSettingsManager(_tempSettingsFile);

        // 1. Default state when file doesn't exist
        var initial = await manager.LoadAsync();
        Assert.False(initial.EnablePhoneAccess);
        Assert.Null(initial.SelectedAdapterId);
        Assert.Null(initial.SelectedIpAddress);

        // 2. Save new preferences
        var updated = new CompanionSettings
        {
            EnablePhoneAccess = true,
            SelectedAdapterId = "adapter-guid-1234",
            SelectedIpAddress = "192.168.1.150"
        };
        await manager.SaveAsync(updated);

        // 3. Reload and verify
        var reloaded = await manager.LoadAsync();
        Assert.True(reloaded.EnablePhoneAccess);
        Assert.Equal("adapter-guid-1234", reloaded.SelectedAdapterId);
        Assert.Equal("192.168.1.150", reloaded.SelectedIpAddress);

        // 4. Disable and verify clean teardown save
        reloaded.EnablePhoneAccess = false;
        await manager.SaveAsync(reloaded);

        var finalCheck = await manager.LoadAsync();
        Assert.False(finalCheck.EnablePhoneAccess);
        Assert.Equal("192.168.1.150", finalCheck.SelectedIpAddress);
    }
}
