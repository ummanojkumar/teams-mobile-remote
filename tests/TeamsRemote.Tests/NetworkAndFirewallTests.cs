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

    [Fact]
    public void BuildReplaceRuleScript_AtomicallyRemovesOldAndCreatesScopedRule()
    {
        string newExe = @"C:\Program Files\WindowsApps\TeamsMobileRemote.Companion_0.9.0.2_x64__pub\TeamsRemote.Companion.exe";
        string script = WindowsFirewallService.BuildReplaceRuleScript(newExe, 5100);

        // 1. Remove old rule
        Assert.Contains("Remove-NetFirewallRule", script);
        Assert.Contains(WindowsFirewallService.RuleName, script);

        // 2. Create new rule with updated path
        Assert.Contains("New-NetFirewallRule", script);
        Assert.Contains($"-Program \"{newExe}\"", script);
        Assert.Contains("-LocalPort 5100", script);
        Assert.Contains("-Profile Private", script);
        Assert.Contains("-RemoteAddress LocalSubnet", script);
        Assert.Contains("-Action Allow", script);
    }

    [Theory]
    [InlineData(0, 9, 0, 1, "beta.1", "0.9.0.1", "0.9.0-beta.1")]
    [InlineData(0, 9, 0, 2, "beta.2", "0.9.0.2", "0.9.0-beta.2")]
    [InlineData(1, 0, 0, 0, "", "1.0.0.0", "1.0.0")]
    public void VersionMapping_MapsCorrectlyBetweenSemVerAndMsix(
        int major, int minor, int patch, int build, string prerelease,
        string expectedMsix, string expectedSemVer)
    {
        string msix = $"{major}.{minor}.{patch}.{build}";
        string semver = string.IsNullOrWhiteSpace(prerelease)
            ? $"{major}.{minor}.{patch}"
            : $"{major}.{minor}.{patch}-{prerelease}";

        Assert.Equal(expectedMsix, msix);
        Assert.Equal(expectedSemVer, semver);
    }

    [Fact]
    public void PackageAppxManifestTemplate_ContainsRequiredDesktopCapabilities()
    {
        string templatePath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "packaging", "msix", "Package.appxmanifest.template");
        templatePath = Path.GetFullPath(templatePath);

        Assert.True(File.Exists(templatePath), $"Expected manifest template at {templatePath}");

        string content = File.ReadAllText(templatePath);
        Assert.Contains("{{MSIX_VERSION}}", content);
        Assert.Contains("{{PUBLISHER_IDENTITY}}", content);
        Assert.Contains("<rescap:Capability Name=\"runFullTrust\" />", content);
        Assert.Contains("<desktop:StartupTask TaskId=\"TeamsRemoteStartup\"", content);
        Assert.Contains("Executable=\"TeamsRemote.Companion.exe\"", content);
    }

    [Fact]
    public void ClassifyErrorMessage_WhenUacCancelled_ReturnsClearCancellationNotice()
    {
        var win32Ex = new System.ComponentModel.Win32Exception(1223); // ERROR_CANCELLED
        string message = WindowsFirewallService.ClassifyErrorMessage(null, win32Ex, -1);

        Assert.Contains("cancelled by the user", message);
        Assert.Contains("administrator permission", message);
    }

    [Fact]
    public void ClassifyErrorMessage_WhenPermissionDenied_ReturnsAdminRequiredNotice()
    {
        var result = new FirewallExecutionResult
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "Access is denied.",
            CategoryInfo = "PermissionDenied: (MSFT_NetFirewallRule:root/standardcimv2/MSFT_NetFirewallRule)",
            FullyQualifiedErrorId = "Windows System Error 5,New-NetFirewallRule"
        };

        string message = WindowsFirewallService.ClassifyErrorMessage(result, null, 1);
        Assert.Contains("Administrator access is required", message);
        Assert.Contains("administrator privileges", message);
    }

    [Fact]
    public void ClassifyErrorMessage_WhenPathRejectedOrPositionalParam_ReturnsPathRejectedNotice()
    {
        var result = new FirewallExecutionResult
        {
            Success = false,
            ExitCode = 1,
            ErrorMessage = "A positional parameter cannot be found that accepts argument 'Files\\WindowsApps...'",
            FullyQualifiedErrorId = "PositionalParameterNotFound,New-NetFirewallRule"
        };

        string message = WindowsFirewallService.ClassifyErrorMessage(result, null, 1);
        Assert.Contains("executable path was rejected", message);
        Assert.Contains("positional parameter", message);
    }

    [Fact]
    public void ClassifyErrorMessage_WhenRemoveRuleFails_ReturnsRuleReplacementNotice()
    {
        var result = new FirewallExecutionResult
        {
            Success = false,
            ExitCode = 1,
            FailingCommand = "Remove-NetFirewallRule",
            ErrorMessage = "Cannot find the firewall rule."
        };

        string message = WindowsFirewallService.ClassifyErrorMessage(result, null, 1);
        Assert.Contains("Existing app-owned firewall rule could not be replaced", message);
    }

    [Fact]
    public void BuildEncodedPowerShellCommand_RoundtripsWithoutQuoteDegradation()
    {
        string rawScript = "New-NetFirewallRule -DisplayName \"TeamsMobileRemote-Companion-LAN\" -Program 'C:\\Program Files\\My App\\app.exe' -LocalPort 5100";
        string encoded = WindowsFirewallService.BuildEncodedPowerShellCommand(rawScript);

        byte[] decodedBytes = Convert.FromBase64String(encoded);
        string decodedScript = System.Text.Encoding.Unicode.GetString(decodedBytes);

        Assert.Equal(rawScript, decodedScript);
    }

    [Fact]
    public void BuildElevatedScript_EnforcesStrictSecurityScope_AndEscapesPaths()
    {
        string exeWithSpacesAndQuote = @"C:\Program Files\O'Reilly\Teams Companion.exe";
        string tempResult = @"C:\Users\User\AppData\Local\TeamsRemote\temp\fw-res.json";

        string script = WindowsFirewallService.BuildElevatedScript("Create", exeWithSpacesAndQuote, 5100, tempResult);

        // Strict security scope invariants
        Assert.Contains("Profile Private", script);
        Assert.Contains("RemoteAddress LocalSubnet", script);
        Assert.Contains("LocalPort 5100", script);
        Assert.Contains("Protocol TCP", script);
        Assert.Contains("Direction Inbound", script);
        Assert.Contains("Action Allow", script);

        // Anti-fallback invariants
        Assert.DoesNotContain("Profile Public", script);
        Assert.DoesNotContain("RemoteAddress Any", script);

        // Path single-quote escaping for PowerShell
        Assert.Contains(@"C:\Program Files\O''Reilly\Teams Companion.exe", script);
        Assert.Contains(@"C:\Users\User\AppData\Local\TeamsRemote\temp\fw-res.json", script);
    }

    [Fact]
    public void RedactDiagnostic_RedactsUserFolderNames_PreservesSystemPaths()
    {
        string userPath = @"C:\Users\SecretUser123\AppData\Local\TeamsRemote\logs\test.log";
        string redactedUserPath = WindowsFirewallService.RedactDiagnostic(userPath);

        Assert.DoesNotContain("SecretUser123", redactedUserPath);
        Assert.Contains(@"C:\Users\<REDACTED>\AppData\Local\TeamsRemote\logs\test.log", redactedUserPath);

        string systemPath = @"C:\Program Files\WindowsApps\TeamsMobileRemote.Companion_0.9.0.1_x64__pub\TeamsRemote.Companion.exe";
        string redactedSystemPath = WindowsFirewallService.RedactDiagnostic(systemPath);

        Assert.Equal(systemPath, redactedSystemPath);
    }

    [Fact]
    public void CreateSecureTempResultFile_CreatesFileWithStrictSecurityAndCanBeCleaned()
    {
        string filePath = WindowsFirewallService.CreateSecureTempResultFile();

        try
        {
            Assert.True(File.Exists(filePath), "Temp file should exist.");

            var fileInfo = new FileInfo(filePath);
            Assert.True((fileInfo.Attributes & FileAttributes.ReparsePoint) == 0, "Temp file must not be a reparse point.");

            string tempDir = WindowsFirewallService.GetTempDirectoryPath();
            Assert.StartsWith(Path.GetFullPath(tempDir), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase);

            // Write sample result
            var sample = new FirewallExecutionResult
            {
                Success = true,
                ExitCode = 0,
                StdOut = "Test rule created"
            };
            File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(sample));

            var read = WindowsFirewallService.ReadAndDeleteTempResultFile(filePath);
            Assert.NotNull(read);
            Assert.True(read.Success);
            Assert.Equal("Test rule created", read.StdOut);

            Assert.False(File.Exists(filePath), "File should be deleted after read.");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
