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
            var plan = Tiers.Plan(probe, s.MindUseGpu, s.MindBackend);
            if (plan.Tier == Tier.None) plan = plan with { Context = 4096 };
            try { return await StartAsync(model, path, plan, ct); }
            catch (MindRuntimeException e) when (plan.Backend != Backend.Cpu && e.Code is "load-failed" or "oom")
            {
                MindLog.Write($"runtime: {plan.Backend} load failed ({e.Code}), trying the CPU");
                return await StartAsync(model, path, plan with { Backend = Backend.Cpu, Offload = Offload.None }, ct);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<RuntimeReady> StartAsync(ModelEntry model, string path, TierPlan plan, CancellationToken ct)
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
        int port = FreePort();
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in ServerArgs.For(plan, path, port, key, ServerArgs.Threads(Environment.ProcessorCount, 0))) psi.ArgumentList.Add(a);
        if (plan.Backend == Backend.Cuda) psi.Environment["PATH"] = Path.Combine(MindPaths.Runtime, "cudart") + ";" + Environment.GetEnvironmentVariable("PATH");
        _vramMB = 0;
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
        var deadline = TimeSpan.FromSeconds(120);
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
        _ready = new RuntimeReady(model.Name, model.Quant, plan.Tier, plan.Backend, (int)sw.ElapsedMilliseconds, _vramMB, ram);
        MindLog.Write(FormattableString.Invariant($"runtime: ready backend={plan.Backend} tier={plan.Tier} load={sw.ElapsedMilliseconds}ms vram={_vramMB}MB ram={ram}MB"));
        return _ready;
    }

    /// <summary>The server's GPU buffers (model, KV, compute) and a memory failure; the line itself is dropped.</summary>
    private void Read(string? line)
    {
        if (line is null) return;
        foreach (Match m in GpuBuffer().Matches(line))
            Interlocked.Add(ref _vramMB, (int)Math.Round(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        if (line.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || line.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase)) _oom = true;
    }

    public (int VramMB, int RamMB) Memory()
    {
        try { _server?.Refresh(); return (_vramMB, _server is { HasExited: false } s ? (int)(s.WorkingSet64 / (1024 * 1024)) : 0); }
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
