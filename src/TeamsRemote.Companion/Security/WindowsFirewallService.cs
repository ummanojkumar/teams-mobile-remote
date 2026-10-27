using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TeamsRemote.Companion.Security;

public sealed class WindowsFirewallService
{
    public const string RuleName = "TeamsMobileRemote-Companion-LAN";
    public const int DefaultPort = 5100;

    public string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
    }

    public static string GetTempDirectoryPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TeamsRemote", "temp");
    }

    public static string GetLogFilePath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TeamsRemote", "logs", "firewall-diagnostics.log");
    }

    public static string BuildCreateRuleScript(string executablePath, int port = DefaultPort)
    {
        // Enforce: Program scoping, Port 5100, TCP, Inbound, Profile Private, RemoteAddress LocalSubnet
        return $"New-NetFirewallRule -DisplayName \"{RuleName}\" -Program \"{executablePath}\" -LocalPort {port} -Protocol TCP -Direction Inbound -Profile Private -RemoteAddress LocalSubnet -Action Allow";
    }

    public static string BuildRemoveRuleScript()
    {
        return $"Remove-NetFirewallRule -DisplayName \"{RuleName}\" -ErrorAction SilentlyContinue";
    }

    public static string BuildReplaceRuleScript(string executablePath, int port = DefaultPort)
    {
        return $"Remove-NetFirewallRule -DisplayName \"{RuleName}\" -ErrorAction SilentlyContinue; " +
               $"New-NetFirewallRule -DisplayName \"{RuleName}\" -Program \"{executablePath}\" -LocalPort {port} -Protocol TCP -Direction Inbound -Profile Private -RemoteAddress LocalSubnet -Action Allow";
    }

    public static string BuildEncodedPowerShellCommand(string script)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(script);
        return Convert.ToBase64String(bytes);
    }

    public static string RedactDiagnostic(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        // Redact Windows user profile paths e.g. C:\Users\alice\... -> C:\Users\<REDACTED>\...
        return Regex.Replace(input, @"([A-Za-z]:\\Users\\)[^\\]+(\\)", "$1<REDACTED>$2", RegexOptions.IgnoreCase);
    }

    public static string BuildElevatedScript(string operation, string executablePath, int port, string resultFilePath)
    {
        string escapedExe = executablePath.Replace("'", "''");
        string escapedResultPath = resultFilePath.Replace("'", "''");

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$result = [ordered]@{");
        sb.AppendLine("    Success = $false");
        sb.AppendLine("    ExitCode = 0");
        sb.AppendLine("    StdOut = ''");
        sb.AppendLine("    StdErr = ''");
        sb.AppendLine("    ErrorMessage = ''");
        sb.AppendLine("    FailingCommand = ''");
        sb.AppendLine("    CategoryInfo = ''");
        sb.AppendLine("    FullyQualifiedErrorId = ''");
        sb.AppendLine($"    ExecutablePath = '{escapedExe}'");
        sb.AppendLine("}");
        sb.AppendLine("try {");

        if (operation.Equals("Create", StringComparison.OrdinalIgnoreCase) || operation.Equals("Replace", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    Remove-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue");
            sb.AppendLine($"    $rule = New-NetFirewallRule -DisplayName '{RuleName}' -Program '{escapedExe}' -LocalPort {port} -Protocol TCP -Direction Inbound -Profile Private -RemoteAddress LocalSubnet -Action Allow -ErrorAction Stop");
            sb.AppendLine("    $result.Success = $true");
            sb.AppendLine($"    $result.StdOut = \"Configured firewall rule for '{RuleName}'\"");
        }
        else if (operation.Equals("Remove", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    Remove-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction Stop");
            sb.AppendLine("    $result.Success = $true");
            sb.AppendLine($"    $result.StdOut = \"Removed firewall rule '{RuleName}'\"");
        }

        sb.AppendLine("} catch {");
        sb.AppendLine("    $result.Success = $false");
        sb.AppendLine("    $result.ExitCode = 1");
        sb.AppendLine("    $result.ErrorMessage = $_.Exception.Message");
        sb.AppendLine("    $result.FailingCommand = if ($_.InvocationInfo -and $_.InvocationInfo.MyCommand) { $_.InvocationInfo.MyCommand.Name } else { '' }");
        sb.AppendLine("    $result.CategoryInfo = \"$($_.CategoryInfo.Category): $($_.CategoryInfo.Reason)\"");
        sb.AppendLine("    $result.FullyQualifiedErrorId = \"$($_.FullyQualifiedErrorId)\"");
        sb.AppendLine("    $result.StdErr = \"$($_ | Out-String)\".Trim()");
        sb.AppendLine("} finally {");
        sb.AppendLine($"    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath '{escapedResultPath}' -Encoding UTF8 -Force");
        sb.AppendLine("}");
        sb.AppendLine("if (-not $result.Success) { exit 1 }");

        return sb.ToString();
    }

    public static string CreateSecureTempResultFile()
    {
        string tempDir = GetTempDirectoryPath();
        Directory.CreateDirectory(tempDir);

        var dirInfo = new DirectoryInfo(tempDir);
        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("TeamsRemote temporary folder is a reparse point or symlink. Aborting for security.");
        }

        string fileName = $"fw-res-{Guid.NewGuid():N}.json";
        string filePath = Path.Combine(tempDir, fileName);

        string fullTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullFilePath = Path.GetFullPath(filePath);

        if (!fullFilePath.StartsWith(fullTempDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Result file path escapes the temporary folder.");
        }

        // Pre-create the file empty
        File.WriteAllText(fullFilePath, "{}");

        var fileInfo = new FileInfo(fullFilePath);
        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            File.Delete(fullFilePath);
            throw new InvalidOperationException("Pre-created result file is a reparse point.");
        }

        // Restrict ACL to Current User, Administrators, SYSTEM
        var fileSecurity = fileInfo.GetAccessControl();
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
        }

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            adminSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        fileSecurity.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        fileInfo.SetAccessControl(fileSecurity);
        return fullFilePath;
    }

    public static FirewallExecutionResult? ReadAndDeleteTempResultFile(string tempFilePath)
    {
        try
        {
            string tempDir = GetTempDirectoryPath();
            string fullTempDir = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullFilePath = Path.GetFullPath(tempFilePath);

            if (!fullFilePath.StartsWith(fullTempDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Result file is outside the temporary folder.");
            }

            var fileInfo = new FileInfo(fullFilePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Result file is a reparse point.");
            }

            string json = File.ReadAllText(fullFilePath);
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
            {
                return null;
            }

            return JsonSerializer.Deserialize<FirewallExecutionResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Suppress deletion error
            }
        }
    }

    public static string ClassifyErrorMessage(FirewallExecutionResult? result, Exception? launchException, int exitCode)
    {
        if (launchException is Win32Exception win32 && win32.NativeErrorCode == 1223)
        {
            return "Administrator permission was cancelled by the user. Windows requires administrator permission to open port 5100 on Private networks.";
        }

        if (launchException != null)
        {
            return $"Failed to launch elevated process: {launchException.Message}";
        }

        if (result != null)
        {
            string err = result.ErrorMessage ?? string.Empty;
            string fqid = result.FullyQualifiedErrorId ?? string.Empty;
            string cat = result.CategoryInfo ?? string.Empty;
            string cmd = !string.IsNullOrWhiteSpace(result.FailingCommand) ? result.FailingCommand : "New-NetFirewallRule";

            // Permission denied / not elevated
            if (fqid.Contains("Windows System Error 5", StringComparison.OrdinalIgnoreCase) ||
                cat.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return "Administrator access is required to configure Windows Firewall. Please ensure your Windows account has administrator privileges.";
            }

            // Path rejected / positional parameter / invalid argument
            if (fqid.Contains("InvalidArgument", StringComparison.OrdinalIgnoreCase) ||
                fqid.Contains("PositionalParameterNotFound", StringComparison.OrdinalIgnoreCase) ||
                cat.Contains("InvalidArgument", StringComparison.OrdinalIgnoreCase))
            {
                return $"The Companion executable path was rejected by Windows Firewall: {err}";
            }

            // Replacement / Removal failure
            if (cmd.Equals("Remove-NetFirewallRule", StringComparison.OrdinalIgnoreCase))
            {
                return $"Existing app-owned firewall rule could not be replaced: {err}";
            }

            if (!string.IsNullOrWhiteSpace(err))
            {
                return $"Firewall configuration failed ({cmd}): {err}";
            }
        }

        return $"Firewall configuration exited with code {exitCode}. Check %LOCALAPPDATA%\\TeamsRemote\\logs\\firewall-diagnostics.log for details.";
    }

    public static void WriteDiagnosticLog(
        string operation,
        string executablePath,
        int exitCode,
        FirewallExecutionResult? result,
        string? userFacingError,
        Exception? ex)
    {
        try
        {
            string logPath = GetLogFilePath();
            string logDir = Path.GetDirectoryName(logPath)!;
            Directory.CreateDirectory(logDir);

            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] OPERATION={operation}");
            sb.AppendLine($"Executable: {RedactDiagnostic(executablePath)}");
            sb.AppendLine($"ProcessExitCode: {exitCode}");
            sb.AppendLine($"Success: {result?.Success ?? false}");

            if (!string.IsNullOrWhiteSpace(userFacingError))
            {
                sb.AppendLine($"UserFacingError: {RedactDiagnostic(userFacingError)}");
            }
            if (!string.IsNullOrWhiteSpace(result?.FailingCommand))
            {
                sb.AppendLine($"FailingCommand: {result.FailingCommand}");
            }
            if (!string.IsNullOrWhiteSpace(result?.ErrorMessage))
            {
                sb.AppendLine($"ErrorMessage: {RedactDiagnostic(result.ErrorMessage)}");
            }
            if (!string.IsNullOrWhiteSpace(result?.CategoryInfo))
            {
                sb.AppendLine($"CategoryInfo: {result.CategoryInfo}");
            }
            if (!string.IsNullOrWhiteSpace(result?.FullyQualifiedErrorId))
            {
                sb.AppendLine($"FullyQualifiedErrorId: {result.FullyQualifiedErrorId}");
            }
            if (!string.IsNullOrWhiteSpace(result?.StdOut))
            {
                sb.AppendLine($"StdOut: {RedactDiagnostic(result.StdOut)}");
            }
            if (!string.IsNullOrWhiteSpace(result?.StdErr))
            {
                sb.AppendLine($"StdErr: {RedactDiagnostic(result.StdErr)}");
            }
            if (ex != null)
            {
                sb.AppendLine($"Exception: {RedactDiagnostic(ex.GetType().FullName)}: {RedactDiagnostic(ex.Message)}");
            }
            sb.AppendLine(new string('-', 60));

            File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging should never throw
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> ExecuteElevatedScriptAsync(
        string operation,
        string executablePath,
        int port = DefaultPort,
        CancellationToken cancellationToken = default)
    {
        string tempFilePath;
        try
        {
            tempFilePath = CreateSecureTempResultFile();
        }
        catch (Exception ex)
        {
            string err = $"Failed to prepare secure temporary result file: {ex.Message}";
            WriteDiagnosticLog(operation, executablePath, -1, null, err, ex);
            return (false, err);
        }

        string script = BuildElevatedScript(operation, executablePath, port, tempFilePath);
        string encodedCommand = BuildEncodedPowerShellCommand(script);

        int exitCode = -1;
        FirewallExecutionResult? result = null;
        Exception? launchEx = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                Verb = "runas", // Triggers Windows UAC elevation
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                launchEx = new InvalidOperationException("Failed to launch elevated PowerShell process.");
                string err = ClassifyErrorMessage(null, launchEx, -1);
                WriteDiagnosticLog(operation, executablePath, -1, null, err, launchEx);
                return (false, err);
            }

            await process.WaitForExitAsync(cancellationToken);
            exitCode = process.ExitCode;

            result = ReadAndDeleteTempResultFile(tempFilePath);

            if (exitCode == 0 && (result?.Success ?? true))
            {
                WriteDiagnosticLog(operation, executablePath, exitCode, result, null, null);
                return (true, null);
            }

            string errorMsg = ClassifyErrorMessage(result, null, exitCode);
            WriteDiagnosticLog(operation, executablePath, exitCode, result, errorMsg, null);
            return (false, errorMsg);
        }
        catch (Exception ex)
        {
            launchEx = ex;
            string errorMsg = ClassifyErrorMessage(result, launchEx, exitCode);
            WriteDiagnosticLog(operation, executablePath, exitCode, result, errorMsg, launchEx);
            return (false, errorMsg);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Suppress cleanup error
            }
        }
    }

    public async Task<string?> GetExistingRuleProgramPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string queryScript = $"Get-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter | Select-Object -ExpandProperty Program";
            string encoded = BuildEncodedPowerShellCommand(queryScript);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string trimmed = output.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CheckRuleExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string queryScript = $"Get-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DisplayName";
            string encoded = BuildEncodedPowerShellCommand(queryScript);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0 && output.Contains(RuleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> CreateRuleElevatedAsync(CancellationToken cancellationToken = default)
    {
        string exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (false, "Cannot resolve the Companion executable path.");
        }

        return await ExecuteElevatedScriptAsync("Create", exePath, DefaultPort, cancellationToken);
    }

    public async Task<(bool Success, string? ErrorMessage)> ReplaceRuleElevatedAsync(CancellationToken cancellationToken = default)
    {
        string exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (false, "Cannot resolve the Companion executable path.");
        }

        return await ExecuteElevatedScriptAsync("Replace", exePath, DefaultPort, cancellationToken);
    }

    public async Task<(bool Success, string? ErrorMessage)> RemoveRuleElevatedAsync(CancellationToken cancellationToken = default)
    {
        string exePath = GetExecutablePath();
        return await ExecuteElevatedScriptAsync("Remove", exePath, DefaultPort, cancellationToken);
    }

    public async Task StartupOrphanCleanupAsync(bool isLanEnabled, Action<string>? logAction = null, CancellationToken cancellationToken = default)
    {
        if (isLanEnabled) return;

        try
        {
            bool ruleExists = await CheckRuleExistsAsync(cancellationToken);
            if (ruleExists)
            {
                logAction?.Invoke("[FIREWALL] Detected orphaned firewall rule while LAN mode is disabled. Cleaning up...");

                // Attempt non-elevated removal first
                string removeScript = BuildRemoveRuleScript();
                string encoded = BuildEncodedPowerShellCommand(removeScript);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode == 0)
                    {
                        logAction?.Invoke("[FIREWALL] Cleaned up orphaned firewall rule successfully.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logAction?.Invoke($"[FIREWALL] Startup orphan rule check: {ex.Message}");
        }
    }

    public async Task<FirewallReconciliationResult> ReconcileFirewallRuleOnStartupAsync(
        bool isLanEnabled,
        Func<string, string, Task<bool>> confirmPromptFunc,
        Action<string>? logAction = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string currentExePath = GetExecutablePath();

            if (!isLanEnabled)
            {
                bool ruleExists = await CheckRuleExistsAsync(cancellationToken);
                if (ruleExists)
                {
                    logAction?.Invoke("[FIREWALL] Private Wi-Fi access is disabled. Cleaning up old app-owned firewall rule...");
                    await StartupOrphanCleanupAsync(false, logAction, cancellationToken);
                    return FirewallReconciliationResult.CleanedStaleRule;
                }

                return FirewallReconciliationResult.NoActionRequired;
            }

            string? existingProgram = await GetExistingRuleProgramPathAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(existingProgram))
            {
                return FirewallReconciliationResult.NoActionRequired;
            }

            if (string.Equals(existingProgram.Trim(), currentExePath.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return FirewallReconciliationResult.NoActionRequired;
            }

            logAction?.Invoke($"[FIREWALL] MSIX update detected! Existing rule program ({RedactDiagnostic(existingProgram)}) does not match current executable ({RedactDiagnostic(currentExePath)}).");

            string title = "Renew Private Wi‑Fi firewall permission after update";
            string message = "Teams Remote Companion was updated to a new version.\n\n" +
                             "Because the Windows Firewall rule is scoped to the application executable, it must be renewed for the new executable path:\n\n" +
                             $"New path:\n{currentExePath}\n\n" +
                             $"Previous path:\n{existingProgram}\n\n" +
                             "Would you like to renew Private Wi‑Fi firewall permission now?\n\n" +
                             "(If declined, LAN phone access will be disabled and Teams Remote will run in loopback-only mode).";

            bool confirmed = await confirmPromptFunc(title, message);
            if (!confirmed)
            {
                logAction?.Invoke("[FIREWALL] User declined firewall permission renewal after update. Keeping LAN access disabled and loopback working.");
                return FirewallReconciliationResult.Declined;
            }

            var (success, error) = await ReplaceRuleElevatedAsync(cancellationToken);
            if (success)
            {
                logAction?.Invoke("[FIREWALL] Firewall rule renewed successfully for the updated executable path.");
                return FirewallReconciliationResult.Renewed;
            }

            logAction?.Invoke($"[FIREWALL] Firewall renewal failed or was cancelled by user ({error}). Keeping LAN access disabled.");
            return FirewallReconciliationResult.Declined;
        }
        catch (Exception ex)
        {
            logAction?.Invoke($"[FIREWALL] Error reconciling firewall rule on startup: {ex.Message}");
            return FirewallReconciliationResult.Error;
        }
    }
}

public sealed class FirewallExecutionResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailingCommand { get; set; }
    public string? CategoryInfo { get; set; }
    public string? FullyQualifiedErrorId { get; set; }
    public string? ExecutablePath { get; set; }
}

public enum FirewallReconciliationResult
{
    NoActionRequired,
    CleanedStaleRule,
    Renewed,
    Declined,
    Error
}
