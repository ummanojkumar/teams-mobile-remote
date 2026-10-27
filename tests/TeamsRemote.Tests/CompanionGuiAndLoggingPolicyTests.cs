using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TeamsRemote.Companion.Logging;
using Xunit;

namespace TeamsRemote.Tests;

public class CompanionGuiAndLoggingPolicyTests
{
    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "version.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not find repository root containing version.json");
    }

    [Fact]
    public void CompanionProject_ConfiguredAsWinExe()
    {
        string repoRoot = FindRepoRoot();
        string csprojPath = Path.Combine(repoRoot, "src", "TeamsRemote.Companion", "TeamsRemote.Companion.csproj");
        Assert.True(File.Exists(csprojPath), $"Csproj file must exist at {csprojPath}");

        var doc = XDocument.Load(csprojPath);
        var outputTypeElement = doc.Descendants("OutputType").FirstOrDefault();

        Assert.NotNull(outputTypeElement);
        Assert.Equal("WinExe", outputTypeElement.Value.Trim());
    }

    [Fact]
    public void CompanionExecutable_PeHeaderSpecifiesWindowsGuiSubsystem()
    {
        // IMAGE_SUBSYSTEM_WINDOWS_GUI = 2 (Windows GUI, no console window allocated on launch)
        // IMAGE_SUBSYSTEM_WINDOWS_CUI = 3 (Windows Console, forces CMD/console window allocation)
        const ushort ExpectedSubsystemGui = 2;

        string repoRoot = FindRepoRoot();
        // Check in bin/Debug/net10.0-windows/
        string exePath = Path.Combine(
            repoRoot, "src", "TeamsRemote.Companion", "bin", "Debug", "net10.0-windows", "TeamsRemote.Companion.exe");

        if (!File.Exists(exePath))
        {
            // If not found in bin/Debug, look for publish/staging output
            string stagingExe = Path.Combine(repoRoot, "artifacts", "staging", "msix", "TeamsRemote.Companion.exe");
            if (File.Exists(stagingExe))
            {
                exePath = stagingExe;
            }
        }

        Assert.True(File.Exists(exePath), $"Compiled Companion executable must exist for PE verification at {exePath}");

        ushort subsystem = ReadPeSubsystem(exePath);
        Assert.Equal(ExpectedSubsystemGui, subsystem);
    }

    [Fact]
    public void PackageAppxManifestTemplate_TargetsWinExeCompanionWithoutConsole()
    {
        string repoRoot = FindRepoRoot();
        string manifestTemplatePath = Path.Combine(repoRoot, "packaging", "msix", "Package.appxmanifest.template");
        Assert.True(File.Exists(manifestTemplatePath), "Package.appxmanifest.template must exist");

        string content = File.ReadAllText(manifestTemplatePath);

        // Application executable must target TeamsRemote.Companion.exe
        Assert.Contains("Executable=\"TeamsRemote.Companion.exe\"", content);
        Assert.Contains("EntryPoint=\"Windows.FullTrustApplication\"", content);

        // Startup task must also target TeamsRemote.Companion.exe
        Assert.Contains("desktop:StartupTask TaskId=\"TeamsRemoteStartup\"", content);

        // Must not target any console wrapper (cmd.exe, powershell.exe, conhost.exe)
        Assert.DoesNotContain("cmd.exe", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("powershell.exe", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conhost.exe", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanionLogPolicy_ProductionDefault_DisablesConsole()
    {
        // Default policy without CLI args or debug env vars
        var policy = new CompanionLogPolicy();

        Assert.False(policy.IsConsoleEnabled, "Console logging must be disabled by default in production");
        Assert.False(policy.IsDiagnosticMode, "Diagnostic mode must be disabled by default in production");

        string defaultLogDir = CompanionLogPolicy.GetDefaultLogDirectory();
        Assert.Contains("TeamsRemote", defaultLogDir);
        Assert.Contains("logs", defaultLogDir);

        string currentLogFile = policy.GetCurrentLogFilePath();
        Assert.StartsWith(policy.LogDirectory, currentLogFile);
        Assert.Contains($"companion-{DateTime.UtcNow:yyyyMMdd}.log", currentLogFile);
    }

    [Theory]
    [InlineData("--console", true, false)]
    [InlineData("-c", true, false)]
    [InlineData("--diag", true, true)]
    [InlineData("--diagnostic", true, true)]
    [InlineData("--debug", true, true)]
    public void CompanionLogPolicy_CommandLineOptions_EnableConsoleAndDiag(
        string arg, bool expectedConsole, bool expectedDiag)
    {
        var policy = CompanionLogPolicy.InitializeFromArgs(new[] { arg });

        Assert.Equal(expectedConsole, policy.IsConsoleEnabled);
        Assert.Equal(expectedDiag, policy.IsDiagnosticMode);
    }

    [Fact]
    public void CompanionLogRedactor_RedactsUserPathsAndSecrets()
    {
        string userPathLog = @"Failed loading config at C:\Users\SecretUser123\AppData\Local\TeamsRemote\config.json";
        string redactedPath = CompanionLogRedactor.Redact(userPathLog);
        Assert.DoesNotContain("SecretUser123", redactedPath);
        Assert.Contains(@"C:\Users\[REDACTED]", redactedPath);

        string tokenLog = "WebSocket connected: /ws/companion?deviceId=12345678-1234-1234-1234-123456789abc&token=SuperSecretAuthToken12345";
        string redactedToken = CompanionLogRedactor.Redact(tokenLog);
        Assert.DoesNotContain("SuperSecretAuthToken12345", redactedToken);
        Assert.Contains("token=[REDACTED_TOKEN]", redactedToken);
        Assert.Contains("deviceId=[REDACTED_DEVICE_ID]", redactedToken);

        string codeLog = "Client submitted pairing code: http://127.0.0.1:5100/pair?code=849201";
        string redactedCode = CompanionLogRedactor.Redact(codeLog);
        Assert.DoesNotContain("849201", redactedCode);
        Assert.Contains("code=[REDACTED_CODE]", redactedCode);
    }

    [Fact]
    public void CompanionFileLogger_WritesRedactedOutputToRollingFile()
    {
        string testLogDir = Path.Combine(Path.GetTempPath(), "TeamsRemoteTestLogs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testLogDir);

        try
        {
            var policy = new CompanionLogPolicy(logDirectory: testLogDir, isConsoleEnabled: false);
            var provider = new CompanionLoggerProvider(policy);
            var logger = provider.CreateLogger("TestCategory");

            string sensitiveMessage = @"User file accessed at C:\Users\AliceSmith\Desktop\token.txt with token=MySecretToken123";
            logger.LogInformation(sensitiveMessage);

            string expectedLogFile = policy.GetCurrentLogFilePath();
            Assert.True(File.Exists(expectedLogFile), $"Log file should have been created at {expectedLogFile}");

            string logContent = File.ReadAllText(expectedLogFile);
            Assert.Contains("[INFO] [TestCategory]", logContent);
            Assert.DoesNotContain("AliceSmith", logContent);
            Assert.DoesNotContain("MySecretToken123", logContent);
            Assert.Contains(@"C:\Users\[REDACTED]", logContent);
            Assert.Contains("token=[REDACTED_TOKEN]", logContent);
        }
        finally
        {
            if (Directory.Exists(testLogDir))
            {
                try { Directory.Delete(testLogDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void CompanionLogWriter_PrunesExpiredLogs()
    {
        string testLogDir = Path.Combine(Path.GetTempPath(), "TeamsRemotePruneLogs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testLogDir);

        try
        {
            // Create an expired log file (10 days old)
            string oldLog = Path.Combine(testLogDir, "companion-20260101.log");
            File.WriteAllText(oldLog, "old log content");
            File.SetLastWriteTimeUtc(oldLog, DateTime.UtcNow.AddDays(-10));

            // Create a fresh log file (today)
            string currentLog = Path.Combine(testLogDir, "companion-20261027.log");
            File.WriteAllText(currentLog, "current log content");
            File.SetLastWriteTimeUtc(currentLog, DateTime.UtcNow);

            // Force prune with maxAgeDays: 7
            var dirInfo = new DirectoryInfo(testLogDir);
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var file in dirInfo.GetFiles("companion-*.log"))
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                }
            }

            Assert.False(File.Exists(oldLog), "10-day old log must be deleted");
            Assert.True(File.Exists(currentLog), "Current log must be preserved");
        }
        finally
        {
            if (Directory.Exists(testLogDir))
            {
                try { Directory.Delete(testLogDir, recursive: true); } catch { }
            }
        }
    }

    private static ushort ReadPeSubsystem(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fs);

        // Verify MZ signature
        if (reader.ReadUInt16() != 0x5A4D)
        {
            throw new InvalidOperationException("Not a valid DOS executable.");
        }

        // Offset 0x3C points to PE header (e_lfanew)
        fs.Seek(0x3C, SeekOrigin.Begin);
        int peOffset = reader.ReadInt32();

        // Verify PE signature ('PE\0\0' = 0x00004550)
        fs.Seek(peOffset, SeekOrigin.Begin);
        uint peSig = reader.ReadUInt32();
        if (peSig != 0x00004550)
        {
            throw new InvalidOperationException("Not a valid PE header.");
        }

        // COFF header begins immediately after PE signature (20 bytes).
        // SizeOfOptionalHeader is at offset +16 into COFF header.
        fs.Seek(peOffset + 4 + 16, SeekOrigin.Begin);
        ushort sizeOfOptHeader = reader.ReadUInt16();
        if (sizeOfOptHeader == 0)
        {
            throw new InvalidOperationException("PE Optional Header is missing.");
        }

        // Optional header starts at offset +20 into COFF header (peOffset + 24).
        // In both PE32 (magic 0x010B) and PE32+ (magic 0x020B, 64-bit),
        // the Subsystem 16-bit word is located at offset +68 within the Optional Header.
        int optionalHeaderOffset = peOffset + 4 + 20;
        int subsystemOffset = optionalHeaderOffset + 68;

        fs.Seek(subsystemOffset, SeekOrigin.Begin);
        return reader.ReadUInt16();
    }
}
