using System.Globalization;
using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Protocol;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Core.Brain;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Input;
using Sahar.Engine.Life;
using Sahar.Core.Life;
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

        // DPI follows the monitor holding most (≥ 60 %) of her body; a rescale keeps her body centre in place.
        var monitors = Monitors.All();
        int monitorIndex = -1;
        void MoveTo(Vector2 position)
        {
            sahar.Position = position;
            var b = sahar.BodyRect;
            var body = new RectF(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
            monitorIndex = Placement.ChooseMonitor(body, monitors.Select(m => new RectF(m.Bounds.Left, m.Bounds.Top,
                m.Bounds.Right - m.Bounds.Left, m.Bounds.Bottom - m.Bounds.Top)).ToList(), monitorIndex);
            float dpi = monitors[Math.Clamp(monitorIndex, 0, monitors.Count - 1)].DpiScale;
            if (dpi != sahar.DpiScale)
            {
                var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
                sahar.SetSize(sahar.BodyDip, dpi);
                var units = SaharVisual.BodyUnits;
                sahar.Position = centre - new Vector2(units.X + units.Width / 2, units.Y + units.Height / 2) * sahar.Scale;
            }
            input.MoveTo(sahar.HitRect);
        }

        using var ticker = new Ticker();
        Director? director = null;
        void Show(string key)
        {
            if (key == _state.Expression) return;
            player.Play(ExpressionCatalog.Get(key));
            _state.Expression = key;
        }
        // Expressions from motions and the mouse are transient: after a moment she returns to the brain's face.
        var revert = ui.CreateTimer();
        revert.IsRepeating = false;
        revert.Tick += (_, _) => Safe(() => Show(director?.CurrentKey ?? "idle"));
        void Express(string key, bool transient)
        {
            revert.Stop();
            if (key.Length == 0) { Show(director?.CurrentKey ?? "idle"); return; }
            Show(key);
            if (transient)
            {
                revert.Interval = TimeSpan.FromMilliseconds(key == "poked" ? 1600 : 2000);
                revert.Start();
            }
        }
        // A passing face (a moment, a curious look): back to her expression's own face afterwards.
        var faceTimer = ui.CreateTimer();
        faceTimer.IsRepeating = false;
        faceTimer.Tick += (_, _) => Safe(() => sahar.SetFace(player.Current.Eyes, player.Current.Mouth, ExpressionPlayer.Fade));
        // Only over her own face (idle, or music's headphones): never over an event's face (permission, error…).
        void Face(Eyes eyes, Mouth mouth, float seconds)
        {
            if (_state.Expression is not ("idle" or "music")) return;
            sahar.SetFace(eyes, mouth, ExpressionPlayer.Fade);
            faceTimer.Stop();
            faceTimer.Interval = TimeSpan.FromSeconds(seconds);
            faceTimer.Start();
        }
        var motion = new MotionController(sahar, player, ticker, MoveTo, Express, Face);
        _state.MotionName = () => motion.Current.ToString();
        LifeDirector? life = null;
        _ = new InputController(input, motion, () => { Express("poked", true); life?.OnPoked(); });
        // Senses → brain → what she shows. Hidden means gone: she shrinks away, then the overlay is hidden
        // (no dot over a fullscreen app); she comes back where she was.
        void SetHidden(bool hidden)
        {
            _state.Hidden = hidden;
            input.SetVisible(!hidden);
            if (hidden) motion.Play(MotionKind.FullscreenHide);
            else
            {
                Native.ShowWindow(overlay.Hwnd, Native.SW_SHOWNOACTIVATE);
                motion.Play(MotionKind.Wake);
            }
        }
        motion.HiddenAway += () => { if (_state.Hidden) Native.ShowWindow(overlay.Hwnd, Native.SW_HIDE); };
        director = new Director(ui, key => Express(key, false), SetHidden);
        // Settings: a bad or locked file never stops her (defaults).
        var settings = new SaharSettings();
        try { settings = SettingsStore.Load(SettingsStore.DefaultPath); }
        catch (Exception e) { Log.Write($"settings not loaded: {e.Message}"); }
        using var tray = new Tray { Activity = settings.Activity };
        void SetActivity(ActivityLevel level)
        {
            Log.Write($"activity: {level}");
            settings = settings with { Activity = level };
            tray.Activity = level;
            if (life != null) life.Level = level;
            try { SettingsStore.Save(SettingsStore.DefaultPath, settings); }
            catch (Exception e) { Log.Write($"settings not saved: {e.Message}"); }
        }
        tray.ActivityChosen += level => Safe(() => SetActivity(level));
        tray.HideHour += () => director.HideFor(TimeSpan.FromHours(1));
        tray.ShowNow += () => director.HideFor(TimeSpan.Zero);
        tray.Quit += () => Native.PostQuitMessage(0);
        var power = new Power(director.Post);
        power.Check();
        tray.PowerChanged += () => Safe(() => { power.Check(); host.CheckDevice(); });
        var idle = new Idle(director.Post);
        tray.SessionLocked += locked => Safe(() => { idle.SetAway(locked); if (!locked) host.CheckDevice(); });
        // Nothing draws while she rests, so ask the GPU every second whether it is still there.
        var deviceTimer = ui.CreateTimer();
        deviceTimer.Interval = TimeSpan.FromSeconds(1);
        deviceTimer.Tick += (_, _) => Safe(() => host.CheckDevice());
        deviceTimer.Start();
        var idleTimer = ui.CreateTimer();
        idleTimer.Interval = TimeSpan.FromSeconds(5);
        idleTimer.Tick += (_, _) => Safe(idle.Check);
        // Test machines have no human at the keyboard: away/back come only from the pipe there.
        if (!_options.TestMode) idleTimer.Start();
        _ = new Network(ui, director.Post);
        var media = new Media(ui, director.Post);
        // Her daily life. In test mode it waits for the pipe's "life" command, so it never disturbs other tests.
        using var lifeDirector = new LifeDirector(ui, motion, director, media, settings.Activity, LifeStore.DefaultPath, () => tray.SinceKeyboard,
            Face, () => _state.Expression, () => motion.Squish())
        {
            Enabled = !_options.TestMode,
        };
        life = lifeDirector;
        tray.EndSession += () => Safe(lifeDirector.Save);   // shutdown, logoff, update restart
        Foreground.Start();

        void OnDisplayChanged()
        {
            monitors = Monitors.All();
            monitorIndex = -1;
            sahar.Origin = new Vector2(overlay.Bounds.Left, overlay.Bounds.Top);
            MoveTo(sahar.Position);
            motion.DisplayChanged();
            host.CheckDevice();
        }
        overlay.DisplayChanged += () => Safe(OnDisplayChanged);

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
            revert.Stop();
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
            if (kind == MotionKind.Wander && r.Args.TryGetValue("flight", out var f))
            {
                if (!Enum.TryParse<FlightPattern>(f, out var pattern) || !Enum.IsDefined(pattern)) return new PipeReply(false, "unknown flight");
                motion.PlayFlight(pattern, force: true);
                return new PipeReply(true, State: _state.Snapshot());
            }
            Vector2? velocity = r.Args.ContainsKey("vx") ? new Vector2(Arg(r, "vx"), Arg(r, "vy")) : null;
            if (!motion.Play(kind, velocity)) return new PipeReply(false, $"motion '{kind}' is not available yet");
            return new PipeReply(true, State: _state.Snapshot());
        });
        if (_options.TestMode)
        {
            pipe.Register("debug-info", _ => new PipeReply(true,
                $"generation={host.Generation} overlayVisible={(Native.IsWindowVisible(overlay.Hwnd) != 0 ? "true" : "false")} " +
                FormattableString.Invariant($"faceOffsetX={sahar.Face.Container.Offset.X:0.##} canvasX={sahar.Position.X:0} canvasY={sahar.Position.Y:0}") +
                $" face={sahar.FaceShown.Eyes}/{sahar.FaceShown.Mouth}"));
            pipe.Register("debug-simulate-device-removed", _ => { host.SimulateRemoved(); return new PipeReply(true); });
            pipe.Register("debug-display-changed", _ => { OnDisplayChanged(); return new PipeReply(true, State: _state.Snapshot()); });
            pipe.Register("debug-throw-in-tick", _ => { motion.ArmThrow(); return new PipeReply(true); });
            pipe.Register("hide", r => { director.HideFor(TimeSpan.FromMinutes(Arg(r, "minutes"))); return new PipeReply(true, State: _state.Snapshot()); });
            pipe.Register("show", _ => { director.HideFor(TimeSpan.Zero); return new PipeReply(true, State: _state.Snapshot()); });
            pipe.Register("debug-media", r =>
            {
                bool On(string key) => r.Args?.TryGetValue(key, out var v) == true && v == "true";
                media.Simulate(On("playing"), On("video"), On("browser"), On("front"));
                return new PipeReply(true, lifeDirector.Describe());
            });
            pipe.Register("debug-log-fill", r =>
            {
                var filler = new string('x', 90);
                for (int i = 0, n = (int)(Arg(r, "kb") * 1024 / 120); i < n; i++) Log.Write($"fill {i} {filler}");
                return new PipeReply(true);
            });
            pipe.Register("debug-life", _ => new PipeReply(true, lifeDirector.Describe()));
            pipe.Register("life", r =>
            {
                lifeDirector.Enabled = true;
                if (r.Args?.TryGetValue("gap", out _) == true) lifeDirector.GapOverride = TimeSpan.FromSeconds(Arg(r, "gap"));
                if (r.Args?.TryGetValue("now", out var a) == true)
                    lifeDirector.ActNow(Enum.TryParse<LifeAction>(a, out var act) && Enum.IsDefined(act) ? act : null);
                return new PipeReply(true, lifeDirector.Describe());
            });
            pipe.Register("set-activity", r =>
            {
                if (r.Args is null || !r.Args.TryGetValue("level", out var name) || !Enum.TryParse<ActivityLevel>(name, out var level) || !Enum.IsDefined(level))
                    return new PipeReply(false, "set-activity needs level Calm, Medium or Lively");
                SetActivity(level);
                return new PipeReply(true, lifeDirector.Describe());
            });
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
            if (r == Native.WAIT_OBJECT_0) Safe(ticker.Fire);   // a failing frame is logged, never ends the engine
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

    /// <summary>Runs a UI-thread callback; an exception is logged instead of taking the engine down.</summary>
    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"error: {e}"); }
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
