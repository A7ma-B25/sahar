using System.IO.Pipes;
using System.Text;

namespace Sahar.Engine.Host;

/// <summary>
/// <c>Sahar.Engine.exe --quit</c>, for the installer and the uninstaller: asks a running Sahar to quit with her own
/// pipe command (she saves and ends as from the tray), then waits for her to be gone, and for her brain's process
/// (Sahar.Mind.exe follows her out on its own). An open settings window (0.12) is closed as its own close button does
/// (WM_CLOSE: it saves what was just typed, then exits) and waited for. Never kills anything.
/// Exit 0: none of them is running (any more); 4: one is still running after the wait.
/// <c>--remember &lt;file&gt;</c>: first writes the running Sahar's exe path there (UTF-8 with BOM), so a setup that
/// does not finish can start the same Sahar again.
/// </summary>
internal static unsafe class QuitRunning
{
    public const int StillRunning = 4;
    private const string EngineMutex = @"Local\Sahar.Engine";
    private const string SettingsMutex = @"Local\Sahar.Settings";
    private const string SettingsWindowClass = "SaharSettingsWindow";   // Sahar.Settings' HostWindow.ClassName
    private const uint WM_CLOSE = 0x0010;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SettingsWait = TimeSpan.FromSeconds(10);

    public static int Run(string? rememberFile = null)
    {
        bool sahar = QuitSahar(rememberFile);
        bool settings = CloseSettings();
        return sahar && settings ? 0 : StillRunning;
    }

    private static bool QuitSahar(string? rememberFile)
    {
        if (!Running(EngineMutex)) return true;
        if (rememberFile is not null) Remember(rememberFile);
        var deadline = DateTime.UtcNow + Wait;
        bool asked = false;
        while (!asked && DateTime.UtcNow < deadline && Running(EngineMutex))
        {
            asked = Ask();
            if (!asked) Thread.Sleep(300);
        }
        while (Running(EngineMutex) && DateTime.UtcNow < deadline) Thread.Sleep(200);
        if (Running(EngineMutex)) return false;
        // Her brain's process sees her go and ends its runtime (a kill-on-close job): the setup replaces its files next.
        while (MindRunning() && DateTime.UtcNow < deadline) Thread.Sleep(200);
        return !MindRunning();
    }

    /// <summary>The settings window, closed as its own close button closes it (never killed); true when it is gone.</summary>
    private static bool CloseSettings()
    {
        if (!Running(SettingsMutex)) return true;
        var deadline = DateTime.UtcNow + SettingsWait;
        bool posted = false;
        while (Running(SettingsMutex) && DateTime.UtcNow < deadline)
        {
            if (!posted)
                fixed (char* cls = SettingsWindowClass)
                {
                    nint hwnd = Native.FindWindowEx(0, 0, cls, null);
                    posted = hwnd != 0 && Native.PostMessage(hwnd, WM_CLOSE, 0, 0) != 0;
                }
            Thread.Sleep(200);
        }
        return !Running(SettingsMutex);
    }

    private static bool MindRunning()
    {
        var mind = System.Diagnostics.Process.GetProcessesByName("Sahar.Mind");
        foreach (var p in mind) p.Dispose();
        return mind.Length > 0;
    }

    private static void Remember(string file)
    {
        try
        {
            int self = Environment.ProcessId;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Sahar.Engine"))
                using (p)
                {
                    if (p.Id == self) continue;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch (Exception) { }
                    if (path is null) continue;
                    File.WriteAllText(file, path, new UTF8Encoding(true));
                    return;
                }
        }
        catch (Exception) { }
    }

    private static bool Ask()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Pipe.PipeServer.Name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            using var reader = new StreamReader(pipe, utf8, false, leaveOpen: true);
            writer.WriteLine("{\"type\":\"cmd\",\"name\":\"quit\"}");
            // The reply comes before she quits (the quit runs after it is sent); a closed pipe also means "heard".
            var reply = reader.ReadLine();
            return reply is null || Sahar.Core.Protocol.ProtocolJson.ParseReply(reply).Ok;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Running(string name)
    {
        try
        {
            if (!Mutex.TryOpenExisting(name, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;   // it exists, we just may not open it
        }
    }
}
