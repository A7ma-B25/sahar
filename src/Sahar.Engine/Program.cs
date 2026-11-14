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
        // An erase that could not finish (a file was in use) finishes now, before anything reads her data.
        var resumed = Sahar.Core.Settings.DataEraser.ResumeIfPending(Sahar.Core.Settings.CleanStart.RoamingFolder, [Sahar.Core.Settings.CleanStart.MarkerName]);
        // The clean version: old test builds' data goes once, before the log opens.
        bool cleaned = Sahar.Core.Settings.CleanStart.Run(Sahar.Core.Settings.CleanStart.RoamingFolder, Log.Folder);
        Log.Start();
        string? last = Log.LastLine();
        Log.Write($"start {CoreInfo.Version} args [{string.Join(' ', args)}] on {Environment.OSVersion}");
        // A run that never wrote "exit" was killed, crashed, or lost power: say so (its time only, never the line).
        if (!Sahar.Core.Diagnostics.LogRules.EndedCleanly(last))
            Log.Write($"previous run ended without exit (last line {Sahar.Core.Diagnostics.LogRules.Timestamp(last!) ?? "?"})");
        if (cleaned) Log.Write("clean start: old data removed");
        if (resumed is not null) Log.Write($"erase: finished at start ({resumed.Removed} removed, {resumed.Left.Count} still in use)");
        try
        {
            int code = new App(EngineOptions.Parse(args)).Run();
            if (App.EraseRequested)
            {
                // A second pass once everything is closed; what is still in use stays marked for her next start.
                var r = Sahar.Core.Settings.DataEraser.Erase(Sahar.Core.Settings.CleanStart.RoamingFolder, [Sahar.Core.Settings.CleanStart.MarkerName]);
                Log.Write($"erase: {r.Removed} more removed, {r.Left.Count} left for the next start");
                Log.Write($"erase: {Sahar.Core.Settings.DataEraser.EraseActivityData(Log.Folder)} activity entries removed");   // screen time, shelf, lyrics (0.13)
                Sahar.Core.Mind.MindParts.Shred(Sahar.Core.Mind.MindPaths.Personal);   // 0.14: her memory of you, wherever it lives
                Log.Write("erase: mind data shredded");
            }
            Log.Write($"exit {code}");
            return code;
        }
        catch (Exception e)
        {
            Log.Write($"fatal: {Sahar.Core.Diagnostics.LogRules.Describe(e)}");
            return ExitFatal;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
