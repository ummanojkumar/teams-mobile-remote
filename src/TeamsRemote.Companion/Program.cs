using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TeamsRemote.Companion.Logging;
using TeamsRemote.Companion.UI;

namespace TeamsRemote.Companion;

public static class Program
{
    private delegate bool ConsoleCtrlHandlerDelegate(int eventType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate? handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    private static ConsoleCtrlHandlerDelegate? _ctrlHandler;

    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Initialize central logging policy from command line and environment
        var policy = CompanionLogPolicy.InitializeFromArgs(args);

        // 2. Configure console attachment or complete suppression
        if (policy.IsConsoleEnabled)
        {
            ConsoleManager.AttachOrAllocConsole();
            try
            {
                _ctrlHandler = eventType =>
                {
                    // Handles CTRL_C_EVENT (0), CTRL_BREAK_EVENT (1), CTRL_CLOSE_EVENT (2)
                    Environment.Exit(0);
                    return true;
                };
                SetConsoleCtrlHandler(_ctrlHandler, true);

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Environment.Exit(0);
                };
            }
            catch
            {
                // Ignore console control errors
            }
        }
        else
        {
            ConsoleManager.SuppressConsole();
        }

        // 3. Register global unhandled exception logging to the rolling log file
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (sender, e) =>
        {
            CompanionLog.Error("Unhandled Windows Forms UI thread exception", e.Exception);
            MessageBox.Show(
                $"An unexpected error occurred in Teams Remote Companion.\nDetails have been logged to:\n{policy.GetCurrentLogFilePath()}",
                "Teams Remote Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CompanionLog.Error("Unhandled AppDomain exception", ex);
            }
            else
            {
                CompanionLog.Error($"Unhandled non-exception object: {e.ExceptionObject}");
            }
        };

        CompanionLog.Info($"Teams Remote Companion starting (ConsoleEnabled={policy.IsConsoleEnabled}, DiagMode={policy.IsDiagnosticMode})");

        // 4. Run Windows Forms GUI application
        ApplicationConfiguration.Initialize();
        Application.Run(new CompanionMainForm());
    }
}
