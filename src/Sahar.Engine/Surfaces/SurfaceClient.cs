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
/// hidden), keeps one duplex pipe to it, and shows panels and the chat window through it. Each show has a sequence
/// number; its events go to the handler given with it, and a show replaced by another gets "closed". With no host
/// within 8 s a show gets "failed" (the caller says a calm line) — she never waits on it. Logs kinds and ids only.
/// </summary>
internal sealed class SurfaceClient : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(8);
    private readonly DispatcherQueue _ui;
    private readonly bool _testMode;
    private readonly Dictionary<string, (int Seq, Action<SurfaceEvent>? Handler)> _current = [];
    private readonly Dictionary<int, TaskCompletionSource<string?>> _replies = [];
    private readonly List<SurfaceMsg> _waiting = [];
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
    public int Show(string surface, string kind, string mode, JModel model, RectF body, Action<SurfaceEvent> handler, Vector2? anchor = null)
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
        Native.AllowSetForegroundWindow(0xFFFFFFFF);   // the host may take the foreground: the click was his
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
        Send(new SurfaceMsg { T = "hide", Surface = surface }, queueIfDown: false);
    }

    /// <summary>Hides it only if <paramref name="seq"/> is still the one showing (a late answer never closes a newer panel).</summary>
    public void Hide(string surface, int seq)
    {
        if (_current.TryGetValue(surface, out var c) && c.Seq == seq) Hide(surface);
    }

    public void Focus(string surface) => Send(new SurfaceMsg { T = "focus", Surface = surface }, queueIfDown: false);

    public void Theme() => Send(new SurfaceMsg { T = "theme", Dark = Dark() }, queueIfDown: false);

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
        if (_writer is null)
        {
            if (!queueIfDown) return;
            lock (_waiting) { _waiting.RemoveAll(w => w.Surface == m.Surface); _waiting.Add(m); }
            Ensure();
            int seq = m.Seq;
            string surface = m.Surface!;
            _ = Task.Delay(ShowTimeout).ContinueWith(_ => _ui.TryEnqueue(() =>
            {
                bool still;
                lock (_waiting) still = _waiting.RemoveAll(w => w.Seq == seq) > 0;
                if (!still || !_current.TryGetValue(surface, out var c) || c.Seq != seq) return;
                _current.Remove(surface);
                Log.Write($"surface: {surface} failed (no host)");
                if (c.Handler is { } h) Safe(h, new SurfaceEvent("failed", null));
            }));
            return;
        }
        _ = Write(m);
    }

    private async Task Write(SurfaceMsg m)
    {
        var w = _writer;
        if (w is null) return;
        await _write.WaitAsync();
        try { await w.WriteLineAsync(SurfaceJson.Write(m)); }
        catch (Exception e) { Log.Write($"surface: send failed ({e.GetType().Name})"); Drop(); }
        finally { _write.Release(); }
    }

    /// <summary>The host is running and connected, or on its way.</summary>
    private void Ensure()
    {
        lock (_waiting)
        {
            if (_connecting || _writer is not null) return;
            _connecting = true;
        }
        _ = Task.Run(Connect);
    }

    private async Task Connect()
    {
        try
        {
            if (!SettingsLauncher.Available) { Log.Write("surface: host not available"); return; }
            if (!File.Exists(@"\\.\pipe\" + "sahar-surfaces") && (_host is null || _host.HasExited))
            {
                try
                {
                    _host = Process.Start(new ProcessStartInfo(SettingsLauncher.ExePath)
                    { UseShellExecute = false, Arguments = _testMode ? "--surfaces --test-mode" : "--surfaces" });
                    Log.Write("surface: host started");
                }
                catch (Exception e) { Log.Write($"surface: host not started ({e.GetType().Name})"); return; }
            }
            var deadline = DateTime.UtcNow.AddSeconds(90);   // a cold WebView2 runtime can take long on a slow PC
            while (DateTime.UtcNow < deadline && !_stop.IsCancellationRequested)
            {
                var pipe = new NamedPipeClientStream(".", "sahar-surfaces", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(500, _stop.Token);
                    _pipe = pipe;
                    _writer = new StreamWriter(pipe, Utf8) { NewLine = "\n", AutoFlush = true };
                    Log.Write("surface: host connected");
                    _ = Task.Run(() => Read(pipe));
                    List<SurfaceMsg> waiting;
                    lock (_waiting) { waiting = [.. _waiting]; _waiting.Clear(); }
                    foreach (var w in waiting) await Write(w);
                    return;
                }
                catch (Exception) { await pipe.DisposeAsync(); await Task.Delay(300); }
            }
        }
        finally { lock (_waiting) _connecting = false; }
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
        Log.Write("surface: host went away");
        _ui.TryEnqueue(Drop);
    }

    private void Drop()
    {
        _writer = null;
        try { _pipe?.Dispose(); } catch (Exception) { }
        _pipe = null;
        // Whatever was open is gone with it.
        foreach (var (surface, c) in _current.ToList())
        {
            _current.Remove(surface);
            if (c.Handler is { } h) Safe(h, new SurfaceEvent("closed", null));
        }
    }

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
