using System.IO.Pipes;
using System.Text.Json;

namespace Sahar.Mind;

/// <summary>
/// Sahar.Mind.exe (spec 0.14 §3): <c>--pipe &lt;name&gt; --parent &lt;pid&gt; [--script &lt;file&gt;]</c>. Connects to the
/// engine's private pipe (current user only), runs the turn loop on the model runtime, exits when idle or when the
/// engine goes. <c>--version</c>; <c>--probe</c> prints the hardware tier (numbers only).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine($"Sahar.Mind {Sahar.Core.CoreInfo.Version}"); return 0; }
        if (args.Contains("--probe"))
        {
            var p = Probe.Read();
            var plan = Sahar.Core.Mind.Models.Tiers.Plan(p);
            Console.WriteLine($"ramMB={p.RamMB} avx2={p.Avx2} freeDiskMB={p.FreeDiskMB} gpus={string.Join(';', p.Gpus.Select(g => $"{g.Vendor:x4}:{g.DedicatedMB}:{(g.Software ? "sw" : "hw")}"))} tier={plan.Tier} model={plan.ModelId} backend={plan.Backend}");
            return 0;
        }
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var pipeName = Arg("--pipe");
        if (pipeName is null) return 2;
        int parent = int.TryParse(Arg("--parent"), out var pid) ? pid : 0;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { pipe.Connect(5000); }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { MindLog.Write($"mind: no engine pipe ({e.GetType().Name})"); return 3; }
        MindLog.Write($"mind: start {(Arg("--script") is null ? "model" : "script")}");
        return new MindHost(pipe, Arg("--script")).RunAsync(parent).GetAwaiter().GetResult();
    }
}
