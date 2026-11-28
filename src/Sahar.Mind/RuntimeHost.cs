using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Settings;

namespace Sahar.Mind;

/// <summary>
/// llama-server as a child (spec §3.1): started for the active model with the tier's arguments, a random port and key,
/// below-normal priority, inside the kill-on-close job; healthy within 120 s or it is a load failure (a failed GPU load
/// tries once more on the CPU). Unload = kill: RAM and VRAM go back at once. Its output is read only for the GPU
/// buffer sizes and an out-of-memory sign; nothing of it is logged.
/// </summary>
internal sealed partial class RuntimeHost(ModelRegistry registry, Func<SaharSettings> settings) : IMindRuntime
{
    private Process? _server;
    private OpenAiClient? _client;
    private RuntimeReady? _ready;
    private int _vramMB;
    private int _bufferMB;   // llama.cpp's "buffer size" lines (hidden at b12207's default verbosity)
    private bool _gpu;
    private volatile bool _oom;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Loaded => _server is { HasExited: false } && _ready is not null;
    public ILlm Llm => _client ?? throw new MindRuntimeException("model-missing");
    public RuntimeReady? Ready => Loaded ? _ready : null;
    public int ServerPid => _server is { HasExited: false } s ? s.Id : 0;

    [GeneratedRegex(@"(Vulkan\d+|CUDA\d+)[^=\n]*buffer size\s*=\s*([\d.]+)\s*MiB")]
    private static partial Regex GpuBuffer();

    public async Task<RuntimeReady> WarmAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Loaded) return _ready!;
            var model = registry.Active ?? throw new MindRuntimeException("model-missing");
            if (model.LocalPath is not { } path || !File.Exists(path)) throw new MindRuntimeException("model-missing");
            var s = settings();
            var probe = Probe.Read();
            long bytes = new FileInfo(path).Length;
            // 0.18: a model bigger than his RAM is never started; the big brain hands over to the small one.
            if (Tiers.TooBig(probe, bytes)) { MindLog.Write("runtime: model too big for this RAM"); FallBack(model); throw new MindRuntimeException("too-big"); }
            var plan = Tiers.ForModel(Tiers.Plan(probe, s.MindUseGpu, s.MindBackend), probe, bytes);   // 0.18: bigger than the GPU → llama.cpp fits it
            if (plan.Tier == Tier.None) plan = plan with { Context = 4096 };
            try
            {
                try { return await StartAsync(model, path, plan, Tiers.Best(probe), bytes, ct); }
                catch (MindRuntimeException e) when (plan.Backend != Backend.Cpu && e.Code is "load-failed" or "oom")
                {
                    MindLog.Write($"runtime: {plan.Backend} load failed ({e.Code}), trying the CPU");
                    return await StartAsync(model, path, plan with { Backend = Backend.Cpu, Offload = Offload.None }, null, bytes, ct);
                }
            }
            catch (MindRuntimeException e) when (model.Id == ModelCatalog.BigId && e.Code is "load-failed" or "oom" or "timeout")
            {
                MindLog.Write($"runtime: big brain failed ({e.Code})");
                if (FallBack(model)) throw new MindRuntimeException("big-failed");
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>0.18: the big brain did not run — the default (or any other installed model) becomes active again.</summary>
    private bool FallBack(ModelEntry failed)
    {
        if (failed.Id != ModelCatalog.BigId) return false;
        var back = registry.List().Where(m => m.Installed && m.Id != failed.Id).OrderBy(m => m.Id == ModelCatalog.DefaultId ? 0 : 1).FirstOrDefault();
        if (back is null || !registry.Use(back.Id)) return false;
        MindLog.Write("runtime: back to the small brain");
        return true;
    }

    private readonly Dictionary<string, IReadOnlyList<LlamaDevice>> _devices = [];

    private async Task<RuntimeReady> StartAsync(ModelEntry model, string path, TierPlan plan, Gpu? gpu, long bytes, CancellationToken ct)
    {
        var appDir = AppContext.BaseDirectory;
        var dir = ServerArgs.RuntimeDir(appDir, MindPaths.Runtime, plan.Backend);
        if (!File.Exists(Path.Combine(dir, "llama-server.exe")))
        {
            dir = ServerArgs.RuntimeDir(appDir, MindPaths.Runtime, Backend.Vulkan);
            if (!File.Exists(Path.Combine(dir, "llama-server.exe"))) { dir = ServerArgs.RuntimeDir(appDir, MindPaths.Runtime, Backend.Cpu); plan = plan with { Backend = Backend.Cpu, Offload = Offload.None }; }
            else if (plan.Backend == Backend.Cuda) plan = plan with { Backend = Backend.Vulkan };
        }
        var exe = Path.Combine(dir, "llama-server.exe");
        if (!File.Exists(exe)) throw new MindRuntimeException("load-failed");
        // 0.14.1 fix 7: one named GPU — llama.cpp's Vulkan default also takes an Optimus laptop's Intel iGPU.
        IReadOnlyList<LlamaDevice> before = [];
        LlamaDevice? device = null;
        if (plan.Backend != Backend.Cpu && plan.Offload != Offload.None)
        {
            // 0.17.2: listed once per Mind process (each listing started Vulkan on both his GPUs).
            if (!_devices.TryGetValue(exe, out var listed) || listed.Count == 0) _devices[exe] = listed = await ListDevicesAsync(exe, dir, plan.Backend, ct);
            before = listed;
            device = Devices.Pick(before, gpu);
            MindLog.Write($"runtime: devices {(before.Count == 0 ? "none" : string.Join(", ", before.Select(d => $"{d.Id} {d.Name} {d.TotalMB}MB")))}; using {device?.Id ?? "llama.cpp's choice"}");
        }
        int port = FreePort();
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        var args = ServerArgs.For(plan, path, port, key, ServerArgs.Threads(Environment.ProcessorCount, 0), device?.Id);
        foreach (var a in args) psi.ArgumentList.Add(a);
        // 0.17.2: the CPU side as launched (numbers only), for «ابعت سجلّي».
        string Flag(string f) => args.ToList().IndexOf(f) is var i && i >= 0 && i + 1 < args.Count ? args[i + 1] : "-";
        MindLog.Write($"runtime: server -t {Flag("-t")} -tb {Flag("-tb")} --poll {Flag("--poll")} --prio {Flag("--prio")} (cpus {Environment.ProcessorCount})");
        if (plan.Backend == Backend.Cuda) psi.Environment["PATH"] = CudaPath();
        _vramMB = 0;
        _bufferMB = 0;
        _gpu = plan.Backend != Backend.Cpu && plan.Offload != Offload.None;
        _oom = false;
        var sw = Stopwatch.StartNew();
        var p = Process.Start(psi) ?? throw new MindRuntimeException("load-failed");
        Native.KillWithMe(p.Handle);
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => { try { if (ReferenceEquals(_server, p)) MindLog.Write($"runtime: server exited by itself (code {p.ExitCode})"); } catch (Exception) { } };
        p.ErrorDataReceived += (_, e) => Read(e.Data);
        p.OutputDataReceived += (_, e) => Read(e.Data);
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        _server = p;
        var client = new OpenAiClient(new SocketsHttpHandler { UseProxy = false }, port, key);
        var deadline = ServerArgs.LoadDeadline(bytes);   // 0.18: 120 s, more for a big file (20.5 GB: 265 s)
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (p.HasExited) { _server = null; throw new MindRuntimeException(_oom ? "oom" : "load-failed"); }
            if (await client.HealthyAsync(ct)) break;
            if (sw.Elapsed > deadline) { Kill(); throw new MindRuntimeException("timeout"); }
            await Task.Delay(250, ct);
        }
        _client = client;
        int ram = 0;
        try { p.Refresh(); ram = (int)(p.WorkingSet64 / (1024 * 1024)); } catch (Exception) { }
        long loadMs = sw.ElapsedMilliseconds;
        // 0.15.0 T9: what llama-server holds in video memory, from Windows' own counter (0.14.x logged vram=0MB on his
        // RTX 4050: b12207's default verbosity hides the "buffer size" lines, and the device's free memory before and
        // after often does not move); those two stay as fallbacks, in that order.
        int pid = p.Id;
        // Review I3: Windows' counter is asked off this thread and waited for 2 s at most (stuck or failing → 0, fallback).
        int pdh = _gpu ? await Sahar.Core.Mind.Runtime.Probe.WithinAsync(() => GpuPdh.ProcessMB(pid), TimeSpan.FromSeconds(2)) : 0, buffers = Volatile.Read(ref _bufferMB), delta = 0;
        if (pdh == 0 && buffers == 0 && device is not null) delta = Devices.UsedMB(before, await ListDevicesAsync(exe, dir, plan.Backend, ct), device.Id);
        var (vram, source) = GpuMemory.Best(pdh, buffers, delta);
        _vramMB = vram;
        _ready = new RuntimeReady(model.Name, model.Quant, plan.Tier, plan.Backend, (int)loadMs, _vramMB, ram);
        MindLog.Write(FormattableString.Invariant($"runtime: ready model={model.Name} {model.Quant} offload={plan.Offload} backend={plan.Backend} device={device?.Name ?? (plan.Backend == Backend.Cpu ? "cpu" : "default")} tier={plan.Tier} load={loadMs}ms vram={_vramMB}MB ({source}) ram={ram}MB"));
        return _ready;
    }

    private static string CudaPath() => Path.Combine(MindPaths.Runtime, "cudart") + ";" + Environment.GetEnvironmentVariable("PATH");

    /// <summary><c>llama-server --list-devices</c> (≈ 1 s; 15 s at most): the GPUs this runtime sees, names and memory.</summary>
    private static async Task<IReadOnlyList<LlamaDevice>> ListDevicesAsync(string exe, string dir, Backend backend, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--list-devices");
            if (backend == Backend.Cuda) psi.Environment["PATH"] = CudaPath();
            using var p = Process.Start(psi);
            if (p is null) return [];
            try
            {
                var err = p.StandardError.ReadToEndAsync(ct);
                var outText = await p.StandardOutput.ReadToEndAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
                await err.WaitAsync(TimeSpan.FromSeconds(2), ct);
                return Devices.Parse(outText);
            }
            finally
            {
                try { if (!p.WaitForExit(2000)) p.Kill(); } catch (Exception) { }   // a stuck driver never leaves it behind
            }
        }
        catch (Exception e) when (e is TimeoutException or System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            MindLog.Write($"runtime: list-devices failed ({e.GetType().Name})");
            return [];
        }
    }

    /// <summary>The server's GPU buffers (model, KV, compute) and a memory failure; the line itself is dropped.</summary>
    private void Read(string? line)
    {
        if (line is null) return;
        foreach (Match m in GpuBuffer().Matches(line))
            Interlocked.Add(ref _bufferMB, (int)Math.Round(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        if (line.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || line.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase)) _oom = true;
    }

    /// <summary>Now (the bench asks after its rows): Windows' counter again when she runs on a GPU, else the number
    /// from the load.</summary>
    public (int VramMB, int RamMB) Memory()
    {
        try
        {
            if (_server is not { HasExited: false } s) return (_vramMB, 0);
            s.Refresh();
            int ram = (int)(s.WorkingSet64 / (1024 * 1024));
            int id = s.Id;
            if (_gpu && Sahar.Core.Mind.Runtime.Probe.Within(() => GpuPdh.ProcessMB(id), TimeSpan.FromSeconds(1)) is > 0 and var now) _vramMB = now;   // review I3
            return (_vramMB, ram);
        }
        catch (Exception) { return (_vramMB, 0); }
    }

    public Task UnloadAsync()
    {
        Kill();
        return Task.CompletedTask;
    }

    private void Kill()
    {
        var p = _server;
        _server = null;
        _client = null;
        _ready = null;
        if (p is null) return;
        try
        {
            if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(3000); }
        }
        catch (Exception) { }
        p.Dispose();
        MindLog.Write("runtime: unloaded");
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

/// <summary>The hardware probe (spec §4.3): RAM, DXGI adapters, AVX2, free disk under the models folder.</summary>
internal static class Probe
{
    public static HardwareProbe Read()
    {
        var gpus = Native.Adapters().Select(a => new Gpu(a.Vendor, a.DedicatedMB, a.Software, a.Name)).ToList();
        long free = 0;
        try { Directory.CreateDirectory(MindPaths.Models); free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(MindPaths.Models))!).AvailableFreeSpace / (1024 * 1024); }
        catch (Exception) { }
        return new HardwareProbe(Native.TotalRamMB(), gpus, Native.Avx2(), free);
    }
}

/// <summary>CI's fake model (<c>--script file</c>): every request gets the next scripted line, streamed in chunks.
/// No llama-server runs; the protocol, the tool calls and the unload are tested around it.</summary>
internal sealed class ScriptRuntime(string file) : IMindRuntime, ILlm
{
    private readonly string[] _lines = File.ReadAllLines(file).Where(l => l.Trim().Length > 0).ToArray();
    private int _next;
    private bool _loaded;

    public bool Loaded => _loaded;
    public ILlm Llm => this;

    public async Task<RuntimeReady> WarmAsync(CancellationToken ct)
    {
        await Task.Delay(200, ct);
        _loaded = true;
        return new RuntimeReady("script", "", Tier.None, Backend.Cpu, 200, 0, 0);
    }

    public Task UnloadAsync() { _loaded = false; MindLog.Write("runtime: unloaded (script)"); return Task.CompletedTask; }

    public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
    {
        if (!_loaded) await WarmAsync(ct);
        var text = _lines.Length == 0 ? "{\"say\":\"\"}" : _lines[Math.Min(_next++, _lines.Length - 1)];
        if (text.StartsWith("#sleep ", StringComparison.Ordinal))
        {
            await Task.Delay(int.Parse(text[7..], CultureInfo.InvariantCulture), ct);
            text = _lines.Length > _next ? _lines[_next++] : "{\"say\":\"\"}";
        }
        int third = Math.Max(1, text.Length / 3);
        for (int i = 0; i < text.Length; i += third)
        {
            await Task.Delay(40, ct);
            partial?.Invoke(text.Substring(i, Math.Min(third, text.Length - i)));
        }
        return new LlmReply(text, 50, 20, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(160));
    }
}
