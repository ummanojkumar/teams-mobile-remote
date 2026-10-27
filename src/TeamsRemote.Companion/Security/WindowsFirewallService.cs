using System.ComponentModel;
using System.Diagnostics;

namespace TeamsRemote.Companion.Security;

public sealed class WindowsFirewallService
{
    public const string RuleName = "TeamsMobileRemote-Companion-LAN";
    public const int DefaultPort = 5100;

    public string GetExecutablePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
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

    public async Task<string?> GetExistingRuleProgramPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue | Get-NetFirewallApplicationFilter | Select-Object -ExpandProperty Program\"",
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
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetFirewallRule -DisplayName '{RuleName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DisplayName\"",
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

        string script = BuildCreateRuleScript(exePath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                Verb = "runas", // Triggers Windows UAC elevation
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to launch elevated firewall configuration.");
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return (false, $"Firewall rule creation exited with code {process.ExitCode}.");
            }

            return (true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (User clicked 'No' on UAC)
        {
            return (false, "Administrator elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return (false, $"Firewall rule creation failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ReplaceRuleElevatedAsync(CancellationToken cancellationToken = default)
    {
        string exePath = GetExecutablePath();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (false, "Cannot resolve the Companion executable path.");
        }

        string script = BuildReplaceRuleScript(exePath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to launch elevated firewall rule replacement.");
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return (false, $"Firewall rule replacement exited with code {process.ExitCode}.");
            }

            return (true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Administrator elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return (false, $"Firewall rule replacement failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> RemoveRuleElevatedAsync(CancellationToken cancellationToken = default)
    {
        string script = BuildRemoveRuleScript();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to launch elevated firewall removal.");
            }

            await process.WaitForExitAsync(cancellationToken);
            return (true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Administrator elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return (false, $"Firewall rule removal failed: {ex.Message}");
        }
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
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{BuildRemoveRuleScript()}\"",
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
                // If Private Wi-Fi access is disabled, remove any old app-owned firewall rule as normal
                bool ruleExists = await CheckRuleExistsAsync(cancellationToken);
                if (ruleExists)
                {
                    logAction?.Invoke("[FIREWALL] Private Wi-Fi access is disabled. Cleaning up old app-owned firewall rule...");
                    await StartupOrphanCleanupAsync(false, logAction, cancellationToken);
                    return FirewallReconciliationResult.CleanedStaleRule;
                }

                return FirewallReconciliationResult.NoActionRequired;
            }

            // If Private Wi-Fi access is enabled, inspect the existing app-owned rule
            string? existingProgram = await GetExistingRuleProgramPathAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(existingProgram))
            {
                // No rule currently exists
                return FirewallReconciliationResult.NoActionRequired;
            }

            // Check if existing Program path matches the current Environment.ProcessPath
            if (string.Equals(existingProgram.Trim(), currentExePath.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // Existing rule already matches current process path
                return FirewallReconciliationResult.NoActionRequired;
            }

            // Path mismatch detected (e.g. MSIX update relocated executable directory)
            logAction?.Invoke($"[FIREWALL] MSIX update detected! Existing rule program ({existingProgram}) does not match current executable ({currentExePath}).");

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

            // Only after explicit user confirmation and UAC approval, replace the old app-owned rule
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

public enum FirewallReconciliationResult
{
    NoActionRequired,
    CleanedStaleRule,
    Renewed,
    Declined,
    Error
}

