using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TxtConverter.Cli;

/// <summary>
/// Gives the WinExe process a usable console when it was started from cmd/PowerShell.
/// The GUI build of the app is a WinExe, so the standard handles are only inherited from the
/// parent console when it has one. When they are missing we attach to the parent console and
/// rebind the managed streams to the real handles.
/// </summary>
internal static class ConsoleHost {
    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint FILE_TYPE_UNKNOWN = 0x0000;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr handle);

    /// <summary>True when the process owns a console window (interactive TTY).</summary>
    public static bool IsInteractive { get; private set; }

    public static void Initialize() {
        if (!HasUsableStdHandle()) {
            // Fails harmlessly when there is no parent console (e.g. double click).
            AttachConsole(ATTACH_PARENT_PROCESS);
            RebindStreams();
        }

        try {
            // UTF-8 without BOM so redirected output stays byte clean for piping.
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch {
            // Console encoding can be locked down by the host - not fatal.
        }

        IsInteractive = !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    private static bool HasUsableStdHandle() {
        IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (handle == IntPtr.Zero || handle == INVALID_HANDLE_VALUE) return false;
        return GetFileType(handle) != FILE_TYPE_UNKNOWN;
    }

    private static void RebindStreams() {
        try {
            Stream stdout = Console.OpenStandardOutput();
            if (stdout.CanWrite)
                Console.SetOut(new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch { }

        try {
            Stream stderr = Console.OpenStandardError();
            if (stderr.CanWrite)
                Console.SetError(new StreamWriter(stderr, new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch { }

        try {
            Stream stdin = Console.OpenStandardInput();
            if (stdin.CanRead)
                Console.SetIn(new StreamReader(stdin, Encoding.UTF8));
        }
        catch { }
    }

    /// <summary>
    /// Keeps the console open after a command finished so the result stays readable instead of
    /// the window closing on the last line. Only an interactive console is held: redirected or piped
    /// runs, --json, --quiet and an explicit --no-pause return straight away, so scripts and CI never block.
    /// </summary>
    public static void WaitForExit(ConsoleLog log, bool enabled) {
        if (!enabled || !IsInteractive || log.JsonMode || log.Quiet) return;

        try {
            Console.Error.WriteLine();
            Console.Error.Write("Press any key to return to the command prompt...");
            Console.Error.Flush();
            Console.ReadKey(intercept: true);
            Console.Error.WriteLine();
        }
        catch {
            // No real console or closed input - never block the exit path.
        }
    }

    /// <summary>Asks the user for confirmation. Returns true automatically when there is no TTY and assumeYes is set.</summary>
    public static bool Confirm(string question, bool assumeYes) {
        if (assumeYes) return true;
        if (!IsInteractive) {
            throw CliException.Usage($"{question} (no interactive console: pass --yes to confirm)");
        }

        Console.Write($"{question} [y/N]: ");
        string? answer = Console.ReadLine();
        return !string.IsNullOrWhiteSpace(answer)
               && (answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) || answer.Trim().StartsWith("д", StringComparison.OrdinalIgnoreCase));
    }
}