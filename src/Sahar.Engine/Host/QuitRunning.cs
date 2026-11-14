using System.IO.Pipes;
using System.Text;

namespace Sahar.Engine.Host;

/// <summary>
/// <c>Sahar.Engine.exe --quit</c>, for the installer and the uninstaller: asks a running Sahar to quit with her own
/// pipe command (she saves and ends as from the tray), then waits for her to be gone. Never kills her.
/// Exit 0: she is not running (any more); 4: she is still running after the wait.
/// <c>--remember &lt;file&gt;</c>: first writes the running Sahar's exe path there (UTF-8 with BOM), so a setup that
/// does not finish can start the same Sahar again.
/// </summary>
internal static class QuitRunning
{
    public const int StillRunning = 4;
    private const string EngineMutex = @"Local\Sahar.Engine";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    public static int Run(string? rememberFile = null)
    {
        if (!Running()) return 0;
        if (rememberFile is not null) Remember(rememberFile);
        var deadline = DateTime.UtcNow + Wait;
        bool asked = false;
        while (!asked && DateTime.UtcNow < deadline && Running())
        {
            asked = Ask();
            if (!asked) Thread.Sleep(300);
        }
        while (Running() && DateTime.UtcNow < deadline) Thread.Sleep(200);
        return Running() ? StillRunning : 0;
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

    private static bool Running()
    {
        try
        {
            if (!Mutex.TryOpenExisting(EngineMutex, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;   // it exists, we just may not open it
        }
    }
}
