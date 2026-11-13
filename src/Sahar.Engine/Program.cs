using Sahar.Core;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine;

internal static class Program
{
    public const int ExitAlreadyRunning = 3;
    public const int ExitFatal = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        // Claude Code's hooks (0.13): one line to her pipe, then exit — no mutex, no log, no window, nothing printed.
        if (args.Length > 0 && args[0] == "hook") return Activities.HookMode.Run();
        if (args.Contains("--version"))
        {
            // WinExe has no console; attach to the parent's so the output is visible in a terminal.
            if (!Console.IsOutputRedirected) Native.AttachToParentConsole();
            Console.WriteLine($"Sahar {CoreInfo.Version}");
            return 0;
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\Sahar.Engine", out bool first);
        if (!first) return ExitAlreadyRunning;
        // The clean version: old test builds' data goes once, before the log opens.
        bool cleaned = Sahar.Core.Settings.CleanStart.Run(Sahar.Core.Settings.CleanStart.RoamingFolder, Log.Folder);
        Log.Start();
        string? last = Log.LastLine();
        Log.Write($"start {CoreInfo.Version} args [{string.Join(' ', args)}] on {Environment.OSVersion}");
        // A run that never wrote "exit" was killed, crashed, or lost power: say so (its time only, never the line).
        if (!Sahar.Core.Diagnostics.LogRules.EndedCleanly(last))
            Log.Write($"previous run ended without exit (last line {Sahar.Core.Diagnostics.LogRules.Timestamp(last!) ?? "?"})");
        if (cleaned) Log.Write("clean start: old data removed");
        try
        {
            int code = new App(EngineOptions.Parse(args)).Run();
            if (App.EraseRequested)
                Log.Write($"erase: {Sahar.Core.Settings.DataEraser.EraseAll(Sahar.Core.Settings.CleanStart.RoamingFolder, Sahar.Core.Settings.CleanStart.MarkerName)} items removed");
            Log.Write($"exit {code}");
            return code;
        }
        catch (Exception e)
        {
            Log.Write($"fatal: {e}");
            return ExitFatal;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
