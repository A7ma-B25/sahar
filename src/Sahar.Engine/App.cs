using System.Globalization;
using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Protocol;
using Sahar.Engine.Diagnostics;
using Sahar.Core.Brain;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Input;
using Sahar.Engine.Motion;
using Sahar.Engine.Pipe;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;

namespace Sahar.Engine;

/// <summary>Wires the windows, the compositor and the pipe together and runs the message loop.</summary>
internal sealed unsafe class App
{
    private const float BodyDip = 80f;   // Medium; Task 12 reads it from settings

    private readonly EngineOptions _options;
    private readonly StateReporter _state = new();

    public App(EngineOptions options) => _options = options;

    public int Run()
    {
        var ui = CompositionHost.EnsureDispatcherQueue();
        var overlay = new OverlayWindow(_options.Capturable);
        using var host = new CompositionHost(overlay.Hwnd);
        var (_, work, dpi) = Native.MonitorAt(new POINT());
        var sahar = new SaharVisual(host, Rig.Load(), BodyDip, dpi / 96f)
        {
            Origin = new Vector2(overlay.Bounds.Left, overlay.Bounds.Top),
        };
        sahar.Position = StartPosition(sahar, work);
        IdleAnimations.Start(sahar);
        var player = new ExpressionPlayer(sahar);
        sahar.Rescaled += () =>
        {
            IdleAnimations.Start(sahar);
            player.Reapply();
        };
        host.DeviceLost += () =>
        {
            sahar.Redraw();
            player.Reapply();
        };

        var input = new InputWindow(_options.Capturable, sahar.HitRect);
        _state.Bounds = () =>
        {
            var b = sahar.BodyRect;
            return (b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
        };

        void MoveTo(Vector2 position)
        {
            sahar.Position = position;
            var body = sahar.BodyRect;
            var (_, _, d) = Native.MonitorAt(new POINT { X = (body.Left + body.Right) / 2, Y = (body.Top + body.Bottom) / 2 });
            sahar.SetSize(sahar.BodyDip, d / 96f);
            input.MoveTo(sahar.HitRect);
        }

        using var ticker = new Ticker();
        void Express(string key)
        {
            player.Play(ExpressionCatalog.Get(key));
            _state.Expression = key;
        }
        var motion = new MotionController(sahar, player, ticker, MoveTo, Express);
        _state.MotionName = () => motion.Current.ToString();

        // A click pokes her; she goes back to what she was showing after 1.6 s.
        var pokeTimer = ui.CreateTimer();
        pokeTimer.Interval = TimeSpan.FromMilliseconds(1600);
        pokeTimer.IsRepeating = false;
        string beforePoke = "idle";
        pokeTimer.Tick += (_, _) => { if (_state.Expression == "poked") Express(beforePoke); };
        _ = new InputController(input, motion, () =>
        {
            if (_state.Expression != "poked") beforePoke = _state.Expression;
            Express("poked");
            pokeTimer.Stop();
            pokeTimer.Start();
        });
        // Senses → brain → what she shows.
        void SetHidden(bool hidden)
        {
            _state.Hidden = hidden;
            input.SetVisible(!hidden);
            motion.Play(hidden ? MotionKind.FullscreenHide : MotionKind.Wake);
        }
        var director = new Director(ui, Express, SetHidden);
        using var tray = new Tray();
        tray.HideHour += () => director.HideFor(TimeSpan.FromHours(1));
        tray.Quit += () => Native.PostQuitMessage(0);
        var power = new Power(director.Post);
        power.Check();
        tray.PowerChanged += power.Check;
        var idle = new Idle(director.Post);
        tray.SessionLocked += locked => idle.SetAway(locked);
        var idleTimer = ui.CreateTimer();
        idleTimer.Interval = TimeSpan.FromSeconds(5);
        idleTimer.Tick += (_, _) => idle.Check();
        // Test machines have no human at the keyboard: away/back come only from the pipe there.
        if (!_options.TestMode) idleTimer.Start();
        _ = new Network(ui, director.Post);
        Foreground.Start();

        overlay.DisplayChanged += () =>
        {
            sahar.Origin = new Vector2(overlay.Bounds.Left, overlay.Bounds.Top);
            MoveTo(sahar.Position);
        };

        using var pipe = new PipeServer(ui);
        pipe.Register("state", _ => new PipeReply(true, State: _state.Snapshot()));
        pipe.Register("quit", _ => new PipeReply(true), afterReply: () => Native.PostQuitMessage(0));
        pipe.Register("pose", r =>
        {
            motion.Play(MotionKind.Hover);
            MoveTo(new Vector2(Arg(r, "x"), Arg(r, "y")));
            return new PipeReply(true, State: _state.Snapshot());
        });
        pipe.Register("expression", r =>
        {
            if (r.Args is null || !r.Args.TryGetValue("key", out var key)) return new PipeReply(false, "expression needs 'key'");
            var preset = ExpressionCatalog.All.FirstOrDefault(e => e.Key == key);
            if (preset is null) return new PipeReply(false, "unknown expression");
            player.Play(preset);
            _state.Expression = preset.Key;
            return new PipeReply(true, State: _state.Snapshot());
        });
        pipe.OnEvent(r =>
        {
            EventKind kind;
            try { kind = EventNames.Parse(r.Name); }
            catch (FormatException) { return new PipeReply(false, "unknown event"); }
            director.Post(kind);
            return new PipeReply(true, State: _state.Snapshot());
        });
        pipe.Register("motion", r =>
        {
            if (r.Args is null || !r.Args.TryGetValue("kind", out var name)) return new PipeReply(false, "motion needs 'kind'");
            if (!Enum.TryParse<MotionKind>(name, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
                return new PipeReply(false, "unknown motion");
            Vector2? velocity = r.Args.ContainsKey("vx") ? new Vector2(Arg(r, "vx"), Arg(r, "vy")) : null;
            if (!motion.Play(kind, velocity)) return new PipeReply(false, $"motion '{kind}' is not available yet");
            return new PipeReply(true, State: _state.Snapshot());
        });
        if (_options.TestMode)
        {
            pipe.Register("debug-foreground", _ => new PipeReply(false, FullscreenWatcher.Describe()));
            pipe.Register("debug-tray", _ => tray.IconRect() is { } r
                ? new PipeReply(true, State: _state.Snapshot() with { X = r.Left, Y = r.Top, Width = r.Right - r.Left, Height = r.Bottom - r.Top })
                : new PipeReply(false, "tray icon unknown to Explorer"));
            pipe.Register("debug-device-lost", _ =>
            {
                host.RecreateDevice();
                return new PipeReply(true);
            });
        }
        pipe.Start();
        Log.Write($"engine running (test mode {_options.TestMode}, capturable {_options.Capturable})");

        int code = Loop(ticker);
        Log.Write("message loop ended");
        Foreground.Stop();
        Native.DestroyWindow(input.Hwnd);
        Native.DestroyWindow(overlay.Hwnd);
        return code;
    }

    /// <summary>Pumps messages and, while something is moving, the motion timer. Sleeps when idle.</summary>
    private static int Loop(Ticker ticker)
    {
        nint timer = ticker.Handle;
        MSG msg;
        while (true)
        {
            uint r = Native.MsgWaitForMultipleObjectsEx(1, &timer, Native.INFINITE, Native.QS_ALLINPUT, Native.MWMO_INPUTAVAILABLE);
            if (r == Native.WAIT_OBJECT_0) ticker.Fire();
            while (Native.PeekMessage(&msg, 0, 0, 0, Native.PM_REMOVE) != 0)
            {
                if (msg.Message == Native.WM_QUIT) return (int)msg.WParam;
                Native.TranslateMessage(&msg);
                Native.DispatchMessage(&msg);
            }
        }
    }

    /// <summary>Her canvas position so her body sits at the bottom-right of the work area.</summary>
    private static Vector2 StartPosition(SaharVisual v, RECT work)
    {
        var body = SaharVisual.BodyUnits;
        float margin = 24 * v.DpiScale;
        return new Vector2(
            work.Right - margin - (body.X + body.Width) * v.Scale,
            work.Bottom - (body.Y + body.Height) * v.Scale);
    }

    /// <summary>A numeric argument of a pipe command (screen pixels).</summary>
    private static float Arg(PipeRequest r, string name)
    {
        if (r.Args is null || !r.Args.TryGetValue(name, out var s) ||
            !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{r.Name}' needs a numeric '{name}' argument");
        return v;
    }
}
