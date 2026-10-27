using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TeamsRemote.Companion;

internal static class ConsoleManager
{
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    public static bool IsConsoleAttached { get; private set; }

    public static void AttachOrAllocConsole()
    {
        try
        {
            // First try attaching to the parent console (e.g. caller in PowerShell or CMD)
            if (AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole())
            {
                IsConsoleAttached = true;
                // Re-bind standard output and standard error so Console.WriteLine works
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), Console.OutputEncoding) { AutoFlush = true });
            }
        }
        catch
        {
            IsConsoleAttached = false;
        }
    }

    public static void SuppressConsole()
    {
        IsConsoleAttached = false;
        try
        {
            // Nullify console output so background execution does not interact with non-existent console
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }
        catch
        {
            // Suppress redirection failures
        }
    }
}
