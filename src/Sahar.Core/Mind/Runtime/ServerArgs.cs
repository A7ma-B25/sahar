using Sahar.Core.Mind.Models;

namespace Sahar.Core.Mind.Runtime;

/// <summary>llama-server's command line (spec §3.1, §3.5): loopback only, a fresh key per launch, no web UI, the
/// model's own chat template (--jinja), q8_0 KV cache (q4_0 hurts tool calls), one slot, no built-in sleep (the
/// process is killed instead). 0.14.1: no thinking (<c>--reasoning off</c>): Gemma 4's template thinks by default and the
/// thought took every token of her reply.</summary>
public static class ServerArgs
{
    /// <param name="device">0.14.1: the one GPU to offload to (<c>--device Vulkan1</c>, from <see cref="Devices.Pick"/>);
    /// null = llama.cpp's own choice. Never for the CPU backend.</param>
    public static IReadOnlyList<string> For(TierPlan plan, string modelPath, int port, string apiKey, int threads, string? device = null)
    {
        var a = new List<string>
        {
            "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--api-key", apiKey, "--no-webui", "--jinja", "-c", plan.Context.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--cache-type-k", "q8_0", "--cache-type-v", "q8_0",
            "-t", Cpu(plan, threads), "-tb", Cpu(plan, threads), "--parallel", "1",
            // 0.17.2 (other apps lagged): no busy-wait between jobs and low priority (b12207 common/arg.cpp: --poll 0 = no
            // polling, default 50; --prio -1 = low).
            "--poll", "0", "--prio", "-1",
            "--reasoning", "off", "--reasoning-budget", "0",   // 0.14.1: Gemma 4 still opened a thought now and then
        };
        // 0.18 Auto: no -ngl at all — llama.cpp fits the layers and the MoE experts to the GPU's free memory itself.
        if (plan.Offload != Offload.Auto) a.InsertRange(a.IndexOf("--cache-type-k"), ["-ngl", plan.Offload == Offload.None ? "0" : "999"]);
        if (plan.Offload == Offload.Fit) a.AddRange(["--fit", "on"]);
        if (plan.Offload == Offload.Auto) a.AddRange(["--fit", "on", "--fit-target", "1024"]);
        if (device is { Length: > 0 } && plan.Backend != Backend.Cpu && plan.Offload != Offload.None) a.AddRange(["--device", device]);
        return a;
    }

    /// <summary>0.17.2: with every layer on the GPU the CPU only feeds it: 4 threads at most (his 24 logical CPUs gave
    /// 10, spinning beside Explorer). On the CPU (or a partial fit) the computed count stays.</summary>
    public const int GpuThreads = 4;

    private static string Cpu(TierPlan plan, int threads) =>
        (plan.Offload == Offload.All && plan.Backend != Backend.Cpu ? Math.Min(threads, GpuThreads) : threads).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>0.18: healthy within 120 s, plus 10 s per GB above 6 GB (a 20.5 GB file: 265 s), at most 300 s.</summary>
    public static TimeSpan LoadDeadline(long modelBytes)
    {
        double gb = modelBytes / 1e9;
        return TimeSpan.FromSeconds(Math.Clamp(120 + (int)Math.Round(Math.Max(0, gb - 6) * 10), 120, 300));
    }

    /// <summary>0.18: her model time per chat turn — 20 s, or 45 s for a model over 10 GB (experts run on the CPU).</summary>
    public static TimeSpan ChatBudget(long modelBytes) => TimeSpan.FromSeconds(modelBytes > 10_000_000_000 ? 45 : 20);

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
