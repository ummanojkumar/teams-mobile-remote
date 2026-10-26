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
                logAction?.Invoke("[FIREWALL] Detected orphaned firewall rule from a previous session while LAN mode is disabled. Cleaning up...");

                // Attempt non-elevated removal first (in case running elevated)
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
}
