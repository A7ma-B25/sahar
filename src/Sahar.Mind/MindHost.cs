using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Bench;
using Sahar.Core.Mind.Download;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;

namespace Sahar.Mind;

/// <summary>
/// The Mind's main loop (spec §3.2): one connection on the engine's private pipe, JSON lines both ways. Runs one turn
/// at a time (a chat turn cancels a running initiative/reflect turn, never the other way), downloads, benches and the
/// model list; unloads and exits by <see cref="IdlePolicy"/>; exits with the engine. Logs kinds and timings only.
/// </summary>
internal sealed class MindHost : ITurnIO
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamWriter _out;
    private readonly object _write = new();
    private readonly IMindRuntime _runtime;
    private readonly ModelRegistry _registry = new(MindPaths.Models);
    private readonly ConcurrentDictionary<(string, int), TaskCompletionSource<MindMsg>> _replies = new();
    private readonly CancellationTokenSource _quit = new();
    private MindMsg _hello = new() { T = "hello" };
    private (string Id, string Source, CancellationTokenSource Cts)? _turn;
    private CancellationTokenSource? _download;
    private DateTimeOffset _lastUse = DateTimeOffset.UtcNow;
    private IMindStore? _store;
    private int _busy;   // a bench running: never unloaded under it

    public MindHost(NamedPipeClientStream pipe, string? script)
    {
        _pipe = pipe;
        _out = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _runtime = script is null ? new RuntimeHost(_registry, Settings) : new ScriptRuntime(script);
    }

    private static SaharSettings Settings()
    {
        try { return SettingsStore.Load(SettingsStore.DefaultPath); }
        catch (Exception) { return new SaharSettings(); }
    }

    private IMindStore Store => _store ??= MindParts.Store(MindPaths.Personal, () => DateTimeOffset.Now, () => LearnConsent.From(Settings()), () => Settings().MindEpisodeDays);

    public void Send(MindMsg msg)
    {
        var line = MindWire.Encode(msg);
        lock (_write)
        {
            try { _out.WriteLine(line); }
            catch (IOException) { _quit.Cancel(); }
            catch (ObjectDisposedException) { _quit.Cancel(); }
        }
    }

    public Task<MindMsg> ReplyAsync(string turnId, int call, CancellationToken ct)
    {
        var tcs = _replies.GetOrAdd((turnId, call), _ => new TaskCompletionSource<MindMsg>(TaskCreationOptions.RunContinuationsAsynchronously));
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public async Task<int> RunAsync(int parentPid)
    {
        if (parentPid > 0) _ = WatchParent(parentPid);
        _ = IdleLoop();
        using var reader = new StreamReader(_pipe, Encoding.UTF8);
        MindLog.Write("mind: connected");
        try
        {
            while (!_quit.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_quit.Token);
                if (line is null) break;
                var msg = MindWire.Decode(line);
                if (msg is null) { MindLog.Write("mind: dropped a line"); continue; }
                Dispatch(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        await _runtime.UnloadAsync();
        MindLog.Write("mind: exit");
        return 0;
    }

    private void Dispatch(MindMsg m)
    {
        switch (m.T)
        {
            case "hello": _hello = m; Send(Models()); break;
            case "turn": StartTurn(m); break;
            case "tool-result" or "answer" when m.Id is { } id && m.Call is { } call:
                _replies.GetOrAdd((id, call), _ => new TaskCompletionSource<MindMsg>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(m);
                break;
            case "cancel" when _turn is { } t && (m.Id is null || m.Id == t.Id): t.Cts.Cancel(); break;
            case "warm": _ = Warm(); break;
            case "unload": _ = Unload(); break;
            case "quit": _quit.Cancel(); break;
            case "store-changed": _store = null; break;
            case "download": Download(m); break;
            case "bench": _ = Bench(m.Model == "quick"); break;
            case "model": Model(m); break;
        }
    }

    private void StartTurn(MindMsg turn)
    {
        var id = turn.Id ?? Guid.NewGuid().ToString("n");
        string source = turn.Source ?? "chat";
        if (_turn is { } running)
        {
            if (source is "chat" or "voice" or "draft" && running.Source is not ("chat" or "voice" or "draft")) running.Cts.Cancel();
            else { Send(new MindMsg { T = "error", Id = id, Error = "busy" }); return; }
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
        cts.CancelAfter(source == "reflect" ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(25));   // the runner's own budget is 20 s
        _turn = (id, source, cts);
        _lastUse = DateTimeOffset.UtcNow;
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!_runtime.Loaded)
                {
                    Send(new MindMsg { T = "state", Phase = "loading" });
                    var ready = await _runtime.WarmAsync(cts.Token);
                    Send(new MindMsg { T = "ready", Ready = ready });
                }
                Send(new MindMsg { T = "state", Phase = "thinking" });
                if (source == "draft")
                {
                    var draft = await MindParts.Drafter(new LazyLlm(_runtime), Store).DraftAsync(new Sahar.Core.Mind.Drafts.DraftRequest(turn.Text ?? ""), cts.Token);
                    Send(draft is null ? new MindMsg { T = "error", Id = id, Error = "bad-output" } : new MindMsg { T = "draft", Id = id, Text = draft.Text, Kind = draft.Label });
                }
                else
                {
                    var runner = MindParts.TurnRunner(new LazyLlm(_runtime), MindParts.PromptParts(Store), Store);
                    await runner.RunAsync(turn with { Id = id }, _hello, this, cts.Token);
                }
            }
            catch (OperationCanceledException) { Send(new MindMsg { T = "error", Id = id, Error = _quit.IsCancellationRequested || cts.IsCancellationRequested && sw.Elapsed < TimeSpan.FromSeconds(24) ? "cancelled" : "timeout" }); }
            catch (MindRuntimeException e) { Send(new MindMsg { T = "error", Id = id, Error = e.Code }); }
            catch (Exception e) { MindLog.Write($"turn: failed ({e.GetType().Name})"); Send(new MindMsg { T = "error", Id = id, Error = "bad-output" }); }
            finally
            {
                MindLog.Write($"turn: {source} {sw.ElapsedMilliseconds}ms");
                if (_turn?.Id == id) _turn = null;
                _lastUse = DateTimeOffset.UtcNow;
                Send(new MindMsg { T = "state", Phase = "idle" });
            }
        });
    }

    private async Task Warm()
    {
        try
        {
            _lastUse = DateTimeOffset.UtcNow;
            if (_runtime.Loaded) return;
            Send(new MindMsg { T = "state", Phase = "loading" });
            Send(new MindMsg { T = "ready", Ready = await _runtime.WarmAsync(_quit.Token) });
            Send(new MindMsg { T = "state", Phase = "idle" });
        }
        catch (MindRuntimeException e) { Send(new MindMsg { T = "error", Error = e.Code }); }
        catch (OperationCanceledException) { }
    }

    private async Task Unload()
    {
        await _runtime.UnloadAsync();
        Send(new MindMsg { T = "state", Phase = "unloaded" });
    }

    private MindMsg Models() => new() { T = "models", Models = _registry.List(), Model = _registry.Active?.Id };

    private void Model(MindMsg m)
    {
        switch (m.Op)
        {
            case "use" when m.Model is { } id:
                if (_registry.Use(id)) _ = Unload();   // the next warm loads it; memory is untouched
                break;
            case "remove" when m.Model is { } id && _registry.Find(id) is { } e:
                if (e.Active) _ = Unload();
                if (e.Origin != ModelOrigin.Local && e.LocalPath is { } path) { try { File.Delete(path); } catch (IOException) { } }
                if (e.Origin == ModelOrigin.Recommended) _registry.MarkRemoved(id); else _registry.Remove(id);
                break;
        }
        Send(Models());
    }

    /// <summary>Downloads (spec §6). The engine sends "start" only after his yes (the page's «نزّليه» or the bubble's «إي»).</summary>
    private void Download(MindMsg m)
    {
        if (m.Model == "runtime-cuda") { CudaPack(m.Op); return; }
        var entry = m.Model is { } id ? _registry.Find(id) : _registry.Find(ModelCatalog.DefaultId);
        if (entry is null || entry.Sha256 is null || entry.Repo is null || entry.Commit is null || entry.File is null)
        {
            Send(new MindMsg { T = "progress", Progress = new DownloadProgress(m.Model ?? "", "", 0, 0, 0, "failed", null, "unknown-model") });
            return;
        }
        var file = Path.Combine(MindPaths.Models, entry.File);
        var url = new Uri($"https://huggingface.co/{entry.Repo}/resolve/{entry.Commit}/{Uri.EscapeDataString(entry.File)}");
        var dl = new Downloader(new SocketsHttpHandler { AllowAutoRedirect = false }, () => DateTimeOffset.Now, FreeBytes);
        switch (m.Op)
        {
            case "check":
                _ = Task.Run(async () =>
                {
                    Send(new MindMsg { T = "progress", Progress = new DownloadProgress(entry.Id, entry.File, 0, entry.Bytes, 0, "checking", "hf") });
                    var reach = await dl.CheckAsync(url, _quit.Token);
                    MindLog.Write($"download: reach {(reach.Ok ? "ok" : reach.Why)}");
                    long done = Downloader.ReadState(file)?.Offset ?? 0;
                    Send(new MindMsg { T = "progress", Progress = new DownloadProgress(entry.Id, entry.File, done, entry.Bytes, 0, reach.Ok ? "reachable" : "blocked", "hf", reach.Why) });
                });
                break;
            case "start" or "resume":
                if (_download is not null) return;
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
                _download = cts;
                _ = Task.Run(async () =>
                {
                    var reach = await dl.CheckAsync(url, cts.Token);
                    if (!reach.Ok)
                    {
                        Send(new MindMsg { T = "progress", Progress = new DownloadProgress(entry.Id, entry.File, 0, entry.Bytes, 0, "blocked", "hf", reach.Why) });
                        _download = null;
                        return;
                    }
                    var plan = new DownloadPlan(entry.Id, url, file, entry.Bytes, entry.Sha256, DateTimeOffset.Now);
                    var progress = new Reporter(p => { _lastUse = DateTimeOffset.UtcNow; Send(new MindMsg { T = "progress", Progress = p }); });
                    var sw = Stopwatch.StartNew();
                    var r = await dl.RunAsync(plan, progress, cts.Token);
                    MindLog.Write($"download: {r.State} after {sw.Elapsed.TotalSeconds:0}s ({r.Error ?? "-"})");
                    if (r.State == "done")
                    {
                        _registry.MarkInstalled(entry.Id, file);
                        if (_registry.Active is null) _registry.Use(entry.Id);
                        Send(Models());
                    }
                    else Send(new MindMsg { T = "progress", Progress = new DownloadProgress(entry.Id, entry.File, r.Done, entry.Bytes, 0, r.State, "hf", r.Error ?? (r.Missing > 0 ? r.Missing.ToString(System.Globalization.CultureInfo.InvariantCulture) : null)) });
                    _download = null;
                });
                break;
            case "pause": _download?.Cancel(); break;
            case "cancel":
                _download?.Cancel();
                Downloader.Discard(file);
                Send(new MindMsg { T = "progress", Progress = new DownloadProgress(entry.Id, entry.File, 0, entry.Bytes, 0, "cancelled", "hf") });
                break;
        }
    }

    /// <summary>The optional CUDA pack (spec §5: ≈ 680 MB, only on his yes): the official llama.cpp CUDA 12.4 build and
    /// its cudart DLLs, each verified against tools/mind/pins.json, unpacked into %LocalAppData%\Sahar\runtime.</summary>
    private void CudaPack(string? op)
    {
        if (op is "pause" or "cancel") { _download?.Cancel(); return; }
        if (op is not ("start" or "resume") || _download is not null) return;
        var pins = RuntimePins.Load();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
        _download = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                var dl = new Downloader(new SocketsHttpHandler { AllowAutoRedirect = false }, () => DateTimeOffset.Now, FreeBytes);
                foreach (var variant in new[] { "cuda", "cudart" })
                {
                    var a = pins.Asset(variant)!;
                    if (a.Sha256 is null) { Send(new MindMsg { T = "progress", Progress = new DownloadProgress("runtime-cuda", a.Name, 0, a.Bytes, 0, "failed", "github", "not-pinned") }); return; }
                    var zip = Path.Combine(MindPaths.Runtime, a.Name);
                    var plan = new DownloadPlan("runtime-cuda", RuntimePins.Url(pins, a), zip, a.Bytes, a.Sha256, DateTimeOffset.Now);
                    var r = File.Exists(zip) ? new DownloadResult("done", a.Bytes) : await dl.RunAsync(plan, new Reporter(p => Send(new MindMsg { T = "progress", Progress = p })), cts.Token);
                    MindLog.Write($"download: cuda {variant} {r.State}");
                    if (r.State != "done") { Send(new MindMsg { T = "progress", Progress = new DownloadProgress("runtime-cuda", a.Name, r.Done, a.Bytes, 0, r.State, "github", r.Error) }); return; }
                    var dir = Path.Combine(MindPaths.Runtime, variant);
                    var tmp = dir + ".unzip";
                    if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                    System.IO.Compression.ZipFile.ExtractToDirectory(zip, tmp);
                    Directory.CreateDirectory(dir);
                    foreach (var f in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
                    {
                        var n = Path.GetFileName(f);
                        if (n == "llama-server.exe" || n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || n.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase))
                            File.Copy(f, Path.Combine(dir, n), overwrite: true);
                    }
                    Directory.Delete(tmp, true);
                    File.Delete(zip);
                }
                Send(new MindMsg { T = "progress", Progress = new DownloadProgress("runtime-cuda", "", 1, 1, 0, "done", "github") });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MindLog.Write($"download: cuda unpack failed ({e.GetType().Name})");
                Send(new MindMsg { T = "progress", Progress = new DownloadProgress("runtime-cuda", "", 0, 0, 0, "failed", "github", "io") });
            }
            finally { _download = null; }
        });
    }

    private async Task Bench(bool quick)
    {
        if (_registry.Active is not { } model && _runtime is not ScriptRuntime) { Send(new MindMsg { T = "error", Error = "model-missing" }); return; }
        var id = _registry.Active?.Id ?? "script";
        Interlocked.Increment(ref _busy);
        try
        {
            Send(new MindMsg { T = "state", Phase = "loading" });
            await _runtime.UnloadAsync();   // a cold load is part of the measure
            var schema = MindParts.ToolRegistry(BenchSuite.Tools).DecisionSchema();
            var r = await BenchRunner.RunAsync(new LazyLlm(_runtime), id, schema, () => _runtime.WarmAsync(_quit.Token),
                () => _runtime is RuntimeHost h ? h.Memory() : (0, 0), golden: !quick, null, () => DateTimeOffset.Now, _quit.Token);
            if (_registry.Find(id) is not null) _registry.SetBench(id, r);
            MindLog.Write(FormattableString.Invariant($"bench: load={r.LoadMs}ms first={r.FirstTokenMs}ms tps={r.TokPerSec} dialect={r.DialectPass}/{r.DialectTotal} tools={r.ToolPass}/{r.ToolTotal}"));
            Send(new MindMsg { T = "bench-result", Bench = r });
            Send(Models());
        }
        catch (MindRuntimeException e) { MindLog.Write($"bench: failed ({e.Code})"); Send(new MindMsg { T = "error", Error = e.Code }); }
        catch (OperationCanceledException) { MindLog.Write("bench: cancelled"); }
        catch (Exception e) { MindLog.Write($"bench: failed ({e.GetType().Name})"); Send(new MindMsg { T = "error", Error = "bad-output" }); }
        finally { Interlocked.Decrement(ref _busy); _lastUse = DateTimeOffset.UtcNow; Send(new MindMsg { T = "state", Phase = "idle" }); }
    }

    private async Task IdleLoop()
    {
        while (!_quit.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("SAHAR_MIND_FAST_IDLE") == "1" ? 2 : 15), _quit.Token); }
            catch (OperationCanceledException) { return; }
            if (_turn is not null || _download is not null || Volatile.Read(ref _busy) > 0) continue;
            var s = Settings();
            bool battery = Native.OnBattery();
            var step = IdlePolicy.Decide(DateTimeOffset.UtcNow, _lastUse, _runtime.Loaded, battery, s.MindUnloadMinutes);
            if (step == IdleStep.Unload) { await Unload(); _lastUse = DateTimeOffset.UtcNow; }
            else if (step == IdleStep.Exit) { MindLog.Write("mind: idle, exiting"); _quit.Cancel(); }
        }
    }

    private async Task WatchParent(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            await p.WaitForExitAsync(_quit.Token);
            MindLog.Write("mind: the engine went");
            _quit.Cancel();
        }
        catch (ArgumentException) { _quit.Cancel(); }
        catch (OperationCanceledException) { }
    }

    private static long FreeBytes(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace; }
        catch (Exception) { return long.MaxValue; }
    }

    private sealed class Reporter(Action<DownloadProgress> on) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => on(value);
    }

    /// <summary>The runtime's LLM, looked up per call (the server's port changes after an unload).</summary>
    private sealed class LazyLlm(IMindRuntime runtime) : ILlm
    {
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct) => runtime.Llm.CompleteAsync(request, partial, ct);
    }
}
