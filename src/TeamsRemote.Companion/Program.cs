using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TeamsRemote.Companion.UI;

namespace TeamsRemote.Companion;

public static class Program
{
    private delegate bool ConsoleCtrlHandlerDelegate(int eventType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate? handler, bool add);

    private static ConsoleCtrlHandlerDelegate? _ctrlHandler;

    [STAThread]
    public static void Main()
    {
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
            // Ignore if running without console
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new CompanionMainForm());
    }
}
