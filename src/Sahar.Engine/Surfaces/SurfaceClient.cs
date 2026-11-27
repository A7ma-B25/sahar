using System.Diagnostics;
using System.IO.Pipes;
using System.Numerics;
using System.Text;
using Sahar.Core.Geometry;
using Sahar.Core.Surfaces;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Surfaces;

/// <summary>What a surface told her: pick, submit, secondary, action, back, input, seg, send, …, and closed / failed.</summary>
internal readonly record struct SurfaceEvent(string Kind, string? Value);

/// <summary>
/// Her end of the surface host (0.16): starts <c>Sahar.Settings.exe --surfaces</c> a moment after she wakes (warm and
/// hidden), keeps one duplex pipe to it (per session, current user only), and shows panels and the chat window through
/// it. Each show has a sequence number; its events go to the handler given with it, and a show replaced by another gets
/// "closed". Review C2: a show waits while the host warms up (<see cref="HostLink"/>); a host that never comes or dies
/// twice fails shows at once with "failed" and <see cref="Unavailable"/> (her one calm line; the tray falls back to its
/// native menu while <see cref="Connected"/> is false); a host that died once is started again and warmed. Logs kinds
/// and ids only.
/// </summary>
internal sealed class SurfaceClient : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly DispatcherQueue _ui;
    private readonly bool _testMode;
    private readonly Dictionary<string, (int Seq, Action<SurfaceEvent>? Handler)> _current = [];
    private readonly Dictionary<int, TaskCompletionSource<string?>> _replies = [];
    private readonly List<SurfaceMsg> _waiting = [];
    private readonly HostLink _link = new(() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private Process? _host;
    private int _seq;
    private bool _connecting;

    public Func<bool> Capturable { get; set; } = () => false;
    public Func<bool> Dark { get; set; } = () => false;
    public Func<bool> Still { get; set; } = () => false;
    public bool Connected => _writer is not null;
    /// <summary>A show could not open (no host): its kind (tray, surah, chat, …). App says one calm line.</summary>
    public event Action<string>? Unavailable;

    public SurfaceClient(DispatcherQueue ui, bool testMode)
    {
        _ui = ui;
        _testMode = testMode;
    }

    /// <summary>Warm the host up after <paramref name="delay"/> (her own start stays light).</summary>
    public void Start(TimeSpan delay) => _ = Task.Run(async () =>
    {
        try { await Task.Delay(delay, _stop.Token); } catch (OperationCanceledException) { return; }
        Ensure();
    });

    public bool IsOpen(string surface) => _current.ContainsKey(surface);

    /// <summary>Shows a surface beside / above her body (or at an anchor point for the tray menu); returns its seq.</summary>
    public int Show(string surface, string kind, string mode, JModel model, RectF body, Action<SurfaceEvent> handler, Vector2? anchor = null, bool activate = true)
    {
        var centre = anchor ?? new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        var mon = Monitors.At(centre);
        int seq = ++_seq;
        if (_current.TryGetValue(surface, out var old) && old.Handler is { } h) Safe(h, new SurfaceEvent("closed", null));
        _current[surface] = (seq, handler);
        var work = mon.WorkF;
        var m = new SurfaceMsg
        {
            T = "show", Surface = surface, Kind = kind, Seq = seq, Model = model.ToString(), Mode = mode,
            X = anchor?.X ?? body.X, Y = anchor?.Y ?? body.Y, W = body.Width, H = body.Height,
            Wx = work.X, Wy = work.Y, Ww = work.Width, Wh = work.Height, Dpi = mon.DpiScale,
            Dark = Dark(), Still = Still(), Capturable = Capturable(),
        };
        if (activate) Native.AllowSetForegroundWindow(0xFFFFFFFF);   // the host may take the foreground: the click was his
        m.Quiet = !activate;
        Log.Write($"surface: {surface} {kind} open");
        Send(m, queueIfDown: true);
        return seq;
    }

    public void Update(string surface, int seq, JModel model)
    {
        if (!_current.TryGetValue(surface, out var c) || c.Seq != seq) return;
        Send(new SurfaceMsg { T = "update", Surface = surface, Seq = seq, Model = model.ToString(), Dark = Dark(), Still = Still() }, queueIfDown: false);
    }

    public void Hide(string surface)
    {
        if (!_current.Remove(surface)) return;
        lock (_waiting) _waiting.RemoveAll(w => w.Surface == surface);   // a show still waiting for the host never opens later
        Send(new SurfaceMsg { T = "hide", Surface = surface }, queueIfDown: false);
    }

    /// <summary>Hides it only if <paramref name="seq"/> is still the one showing (a late answer never closes a newer panel).</summary>
    public void Hide(string surface, int seq)
    {
        if (_current.TryGetValue(surface, out var c) && c.Seq == seq) Hide(surface);
    }

    public void Focus(string surface) => Send(new SurfaceMsg { T = "focus", Surface = surface }, queueIfDown: false);

    public void Theme() => Send(new SurfaceMsg { T = "theme", Dark = Dark() }, queueIfDown: false);

    /// <summary>0.15 «اسمح بتصويرها» changed: every surface window, open or warm, follows now.</summary>
    public void Capture() => Send(new SurfaceMsg { T = "capture", Capturable = Capturable() }, queueIfDown: false);

    /// <summary>Test mode: the host's state (JSON), or a script's result in a surface.</summary>
    public Task<string?> Ask(string t, string? surface = null, string? script = null)
    {
        int seq = ++_seq;
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_replies) _replies[seq] = tcs;
        Send(new SurfaceMsg { T = t, Seq = seq, Surface = surface, Value = script }, queueIfDown: false);
        _ = Task.Delay(4000).ContinueWith(_ => { lock (_replies) _replies.Remove(seq); tcs.TrySetResult(null); });
        return tcs.Task;
    }

    private void Send(SurfaceMsg m, bool queueIfDown)
    {
        if (_writer is not null) { _ = Write(m); return; }
        if (!queueIfDown) return;
        HostAction a;
        lock (_waiting)
        {
            a = _link.Show();
            if (a is HostAction.Queue or HostAction.QueueAndStart) { _waiting.RemoveAll(w => w.Surface == m.Surface); _waiting.Add(m); }
        }
        if (a == HostAction.Fail) { string surface = m.Surface!, kind = m.Kind ?? ""; int seq = m.Seq; _ui.TryEnqueue(() => Fail(surface, seq, kind)); }   // after Show returned its seq
        else Ensure();
    }

    /// <summary>The show <paramref name="seq"/> of <paramref name="surface"/> cannot open: "failed", and her calm line.</summary>
    private void Fail(string surface, int seq, string kind)
    {
        if (!_current.TryGetValue(surface, out var c) || c.Seq != seq) return;
        _current.Remove(surface);
        Log.Write($"surface: {surface} failed (no host)");
        if (c.Handler is { } h) Safe(h, new SurfaceEvent("failed", null));
        try { Unavailable?.Invoke(kind); } catch (Exception x) { Log.Write($"surface: unavailable handler error {x.GetType().Name}"); }
    }

    private void FailWaiting()
    {
        List<SurfaceMsg> waiting;
        lock (_waiting) { _link.StartFailed(); waiting = [.. _waiting]; _waiting.Clear(); }
        Log.Write("surface: host failed");
        _ui.TryEnqueue(() => { foreach (var w in waiting) Fail(w.Surface!, w.Seq, w.Kind ?? ""); });
    }

    private async Task Write(SurfaceMsg m)
    {
        var w = _writer;
        if (w is null) return;
        await _write.WaitAsync();
        try { await w.WriteLineAsync(SurfaceJson.Write(m)); }
        catch (Exception e) { Log.Write($"surface: send failed ({e.GetType().Name})"); var p = _pipe; _ui.TryEnqueue(() => Drop(p)); }
        finally { _write.Release(); }
    }

    /// <summary>The host is running and connected, or on its way.</summary>
    private void Ensure()
    {
        lock (_waiting)
        {
            if (_connecting || _writer is not null || _link.State == HostState.Failed) return;
            _connecting = true;
            _link.Starting();
        }
        _ = Task.Run(Connect);
    }

    private async Task Connect()
    {
        bool connected = false;
        try
        {
            if (!SettingsLauncher.Available) { Log.Write("surface: host not available"); return; }
            if (_host is null || _host.HasExited)
            {
                try
                {
                    // Started every time it is not ours and running: a host left from her last run says "already
                    // running" (exit 0) and that one answers the pipe.
                    _host = Process.Start(new ProcessStartInfo(SettingsLauncher.ExePath)
                    { UseShellExecute = false, Arguments = _testMode ? "--surfaces --test-mode" : "--surfaces" });
                    Log.Write("surface: host started");
                }
                catch (Exception e) { Log.Write($"surface: host not started ({e.GetType().Name})"); return; }
            }
            string name = SurfacePipeName.For(Process.GetCurrentProcess().SessionId);
            while (!_stop.IsCancellationRequested)
            {
                lock (_waiting) if (_link.Overdue) { Log.Write("surface: host never answered"); return; }
                if (_host is { HasExited: true } h && h.ExitCode != 0) { Log.Write($"surface: host exited ({h.ExitCode})"); return; }   // no WebView2: at once
                var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await pipe.ConnectAsync(500, _stop.Token);
                }
                catch (Exception) { await pipe.DisposeAsync(); await Task.Delay(300); continue; }
                _pipe = pipe;
                _writer = new StreamWriter(pipe, Utf8) { NewLine = "\n", AutoFlush = true };
                connected = true;
                Log.Write("surface: host connected");
                _ = Task.Run(() => Read(pipe));
                List<SurfaceMsg> waiting;
                lock (_waiting) { _link.Connected(); waiting = [.. _waiting]; _waiting.Clear(); }
                foreach (var w in waiting) await Write(w);
                return;
            }
        }
        finally
        {
            lock (_waiting) _connecting = false;
            if (!connected && !_stop.IsCancellationRequested) FailWaiting();
        }
    }

    private async Task Read(NamedPipeClientStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe, Utf8, false);
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (SurfaceJson.Read(line) is not { } m) continue;
                if (m.T == "reply")
                {
                    TaskCompletionSource<string?>? tcs;
                    lock (_replies) { _replies.Remove(m.Seq, out tcs); }
                    tcs?.TrySetResult(m.Model);
                    continue;
                }
                if (m.T == "event") _ui.TryEnqueue(() => Dispatch(m));
            }
        }
        catch (Exception) { }
        if (_stop.IsCancellationRequested) return;
        Log.Write("surface: host went away");
        _ui.TryEnqueue(() => Drop(pipe));
    }

    /// <summary>The pipe <paramref name="pipe"/> is gone (a late call for an older pipe does nothing).</summary>
    private void Drop(NamedPipeClientStream? pipe)
    {
        if (pipe is null || pipe != _pipe) return;
        _writer = null;
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null;
        // Whatever was open is gone with it.
        foreach (var (surface, c) in _current.ToList())
        {
            _current.Remove(surface);
            if (c.Handler is { } h) Safe(h, new SurfaceEvent("closed", null));
        }
        HostAction a;
        lock (_waiting) a = _link.Lost();
        if (a == HostAction.Respawn) { Log.Write("surface: host started again"); Ensure(); }
        else if (a == HostAction.FailQueued) Log.Write("surface: host gone for this session");
    }

    /// <summary>Test mode: the link's state and the host's process id (the stage stops it to see her fall back).</summary>
    public string Describe() =>
        $"state={_link.State.ToString().ToLowerInvariant()} respawns={_link.Respawns} connected={(Connected ? "true" : "false")} " +
        $"pid={(_host is { HasExited: false } h ? h.Id : 0)} waiting={_waiting.Count}";

    private void Dispatch(SurfaceMsg m)
    {
        if (m.Surface is not { } surface || !_current.TryGetValue(surface, out var c) || c.Seq != m.Seq) return;
        if (m.Kind == "closed") _current.Remove(surface);
        Log.Write($"surface: {surface} {m.Kind}");   // the kind only; never what was typed or picked
        if (c.Handler is { } h) Safe(h, new SurfaceEvent(m.Kind ?? "", m.Value));
    }

    private static void Safe(Action<SurfaceEvent> h, SurfaceEvent e)
    {
        try { h(e); }
        catch (Exception x) { Log.Write($"surface: handler error {x.GetType().Name}"); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _pipe?.Dispose(); } catch (Exception) { }
        // The host quits by itself 15 s after the pipe closes (it never outlives her).
    }
}
