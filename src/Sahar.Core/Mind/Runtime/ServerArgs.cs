using Sahar.Core.Mind.Models;

namespace Sahar.Core.Mind.Runtime;

/// <summary>llama-server's command line (spec §3.1, §3.5): loopback only, a fresh key per launch, no web UI, the
/// model's own chat template (--jinja), q8_0 KV cache (q4_0 hurts tool calls), one slot, no built-in sleep (the
/// process is killed instead).</summary>
public static class ServerArgs
{
    public static IReadOnlyList<string> For(TierPlan plan, string modelPath, int port, string apiKey, int threads)
    {
        var a = new List<string>
        {
            "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--api-key", apiKey, "--no-webui", "--jinja", "-c", plan.Context.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ngl", plan.Offload == Offload.None ? "0" : "999",
            "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
            "-t", threads.ToString(System.Globalization.CultureInfo.InvariantCulture), "--parallel", "1",
        };
        if (plan.Offload == Offload.Fit) a.AddRange(["--fit", "on"]);
        return a;
    }

    /// <summary>Physical cores − 2, at least 2 (without a physical count: half the logical ones).</summary>
    public static int Threads(int logical, int physical) => Math.Max(2, (physical > 0 ? physical : logical / 2) - 2);

    /// <summary>Where llama-server lives: Vulkan and CPU ship next to Sahar.Mind.exe (runtime\vulkan, runtime\cpu);
    /// CUDA is downloaded on his yes into %LocalAppData%\Sahar\runtime\cuda (with cudart's DLLs beside it).</summary>
    public static string RuntimeDir(string appDir, string localRuntime, Backend backend) => backend switch
    {
        Backend.Cuda => Path.Combine(localRuntime, "cuda"),
        Backend.Cpu => Path.Combine(appDir, "runtime", "cpu"),
        _ => Path.Combine(appDir, "runtime", "vulkan"),
    };
}

public enum IdleStep { Keep, Unload, Exit }

/// <summary>Spec §3.1: unload after MindUnloadMinutes (1 min on battery); with nothing loaded for 10 min the Mind exits.</summary>
public static class IdlePolicy
{
    public static readonly TimeSpan ExitAfter = TimeSpan.FromMinutes(10);

    public static IdleStep Decide(DateTimeOffset now, DateTimeOffset lastUse, bool loaded, bool onBattery, int unloadMinutes)
    {
        var idle = now - lastUse;
        if (loaded) return idle >= TimeSpan.FromMinutes(onBattery ? 1 : unloadMinutes) ? IdleStep.Unload : IdleStep.Keep;
        return idle >= ExitAfter ? IdleStep.Exit : IdleStep.Keep;
    }
}
