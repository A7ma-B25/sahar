using System.IO.Pipes;
using System.Text;
using Sahar.Core.Activities;
using Sahar.Core.Protocol;

namespace Sahar.Engine.Activities;

/// <summary>
/// <c>Sahar.Engine.exe hook</c> (spec 0.13 §2 Claude Code, owner ruling 3): run by Claude Code for each hook (async,
/// exec form). Reads stdin, sends one line to her pipe and exits 0; it never prints (Claude's own prompt answers) and
/// never opens a window, a log or the single-instance mutex.
/// </summary>
internal static class HookMode
{
    public static int Run()
    {
        try
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            return HookRun.Run(stdin, TextWriter.Null, Send);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool Send(IReadOnlyDictionary<string, string> args)
    {
        using var pipe = new NamedPipeClientStream(".", Pipe.PipeServer.Name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        pipe.Connect(500);
        var enc = new UTF8Encoding(false);
        var line = enc.GetBytes(ProtocolJson.Write(new PipeRequest("cmd", "claude-hook", args)) + "\n");
        pipe.Write(line);
        pipe.Flush();
        // Wait (briefly) for her answer, so the line is handled before the pipe closes.
        var buffer = new byte[256];
        var read = pipe.ReadAsync(buffer).AsTask();
        read.Wait(1000);
        return true;
    }
}
