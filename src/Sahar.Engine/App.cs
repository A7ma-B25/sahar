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
using Sahar.Engine.Menu;
using Sahar.Engine.Tools;
using Sahar.Core.Tools;
using Sahar.Core.Senses;
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
        using var menu = new RadialMenu(host, _options.Capturable, () => motion.Body, () => sahar.Origin);
        Director? dismissDirector = null;
        Action<bool>? stopShow = null;
        Func<bool> dismissSpeech = () => false;
        Func<bool> showRunning = () => false;
        _ = new InputController(input, motion, () =>
        {
            // A click during her show stops it.
            if (showRunning()) { stopShow?.Invoke(false); return; }
            if (dismissSpeech()) return;   // a click on her closes her comment (and she'll comment less)
            // A poke on the timer's face says "seen": it goes away.
            if (dismissDirector?.CurrentKey == "timer-done") dismissDirector.End(EventKind.TimerDone);
            if (dismissDirector?.CurrentKey == "reminder") dismissDirector.End(EventKind.Reminder);
            Express("poked", true);
            life?.OnPoked();
        }, () =>
        {
            if (_state.Hidden || motion.Current is MotionKind.Drag or MotionKind.Throw) return;
            if (showRunning()) stopShow?.Invoke(false);   // first, so the menu opens around where she stays
            menu.Toggle();
        });
        // Senses → brain → what she shows. Hidden means gone: she shrinks away, then the overlay is hidden
        // (no dot over a fullscreen app); she comes back where she was.
        void SetHidden(bool hidden)
        {
            _state.Hidden = hidden;
            if (hidden) { menu.Close(); stopShow?.Invoke(false); }
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
        dismissDirector = director;
        var timerTool = new TimerTool(ui, sahar, director);
        var dndTool = new DndTool(ui, director);
        var bubble = new TextBubble(_options.Capturable);
        var notes = new NoteTool(bubble, new NotesStore(NotesStore.DefaultPath), () => motion.Body, Express);
        var listBubble = new ListBubble(_options.Capturable);
        sahar.Rescaled += () => Safe(timerTool.Redraw);
        host.DeviceLost += () => Safe(() => { timerTool.Redraw(); menu.DeviceLost(); });
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
        // Quit from the tray: she waves goodbye and turns into stars first (motion 19), then the app ends.
        var quitTimer = ui.CreateTimer();
        quitTimer.IsRepeating = false;
        quitTimer.Interval = TimeSpan.FromSeconds(2.5);
        quitTimer.Tick += (_, _) => Native.PostQuitMessage(0);
        motion.GoodbyeDone += () => { if (quitTimer.IsRunning) Native.PostQuitMessage(0); };
        tray.Quit += () => Safe(() =>
        {
            menu.Close();
            if (_state.Hidden) { Native.PostQuitMessage(0); return; }
            motion.Play(MotionKind.Goodbye);
            quitTimer.Start();
        });
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
        // Her menu: life pauses and she hovers while it is open.
        menu.Level = () => settings.Activity;
        // What you copy (spec §7.2): kept by the rules, a short "copied" face at most every 30 s.
        var clipboard = new ClipboardSense(tray.Hwnd, new ClipboardHistory(ClipboardHistory.DefaultFolder));
        var clipboardTool = new ClipboardTool(listBubble, clipboard, () => motion.Body);
        var lastCopiedFace = DateTimeOffset.MinValue;
        clipboard.Kept += () =>
        {
            if (DateTimeOffset.Now - lastCopiedFace < TimeSpan.FromSeconds(30) || _state.Hidden) return;
            lastCopiedFace = DateTimeOffset.Now;
            Log.Write("clipboard: copied face");
            Express("copied", true);
        };
        tray.ClipboardChanged += () => Safe(clipboard.Changed);
        // Downloads, screenshot keys, a busy CPU (spec §8.1).
        using var downloads = new Downloads(ui, director.Post, _options.DownloadsDir);
        var screenshotKeys = new ScreenshotKeys();
        int printScreenSeen = 0;   // test diagnostics: a count, never which keys
        tray.SessionLocked += _ => screenshotKeys.Reset();   // key-ups on the lock screen never reach us
        tray.KeyChanged += (vk, down) => Safe(() =>
        {
            if (vk == 0x2C) printScreenSeen++;
            if (!screenshotKeys.Update(vk, down)) return;
            Log.Write("screenshot: keys pressed");
            director.Post(EventKind.Screenshot);
        });
        var cpu = new CpuSense(ui, director.Post);
        var cameraMic = new CameraMic(ui);
        cameraMic.Changed += on => Safe(() => director.SetCameraMic(on));
        menu.TimerRunning = () => timerTool.Running;
        menu.DndOn = () => dndTool.On;
        lifeDirector.DndOn = () => dndTool.On;
        lifeDirector.CameraMicOn = () => cameraMic.InUse;
        // The app in front, and her gentle comments about it (spec §8.1, §8.2).
        using var speech = new SpeechBubble(ui, sahar, () => motion.Body);
        var apps = new AppWatch(ui,
            allowed: () => CommentRules.Allowed(lifeDirector.Typing, director.Quiet, menu.IsOpen, _state.Hidden) && !showRunning(),
            away: () => director.CurrentKey == "sleep",
            say: line => speech.Say(line, comment: true));
        // Ignored or clicked away, she comments less (spec §8.2); a new day brings her back to 45 minutes.
        speech.Ended += _ => apps.Limiter.Dismissed(DateTimeOffset.Now);
        dismissSpeech = () => { if (!speech.Showing) return false; speech.Dismiss(); return true; };
        // Rain coming (opt-in): the tray item asks for your city.
        using var weather = new Weather(ui, director.Post, _options.WeatherStubDir,
            settings.WeatherEnabled ? settings.WeatherLatitude : null, settings.WeatherEnabled ? settings.WeatherLongitude : null);
        tray.WeatherCity = weather.On ? settings.WeatherCity : null;
        void SaveSettings()
        {
            try { SettingsStore.Save(SettingsStore.DefaultPath, settings); }
            catch (Exception e) { Log.Write($"settings not saved: {e.Message}"); }
        }
        void AskCity(string title) => bubble.Show(motion.Body, title, [], city => SetCity(city));
        void SetCity(string city) => weather.FindAsync(city).ContinueWith(t =>
        {
            var (place, error) = t.IsCompletedSuccessfully ? t.Result : (null, "ما في نت هلق، جرّب بعدين");
            ui.TryEnqueue(() => Safe(() =>
            {
                if (place is null) { AskCity($"{error} — جرّب مدينة تانية"); return; }
                settings = settings with { WeatherEnabled = true, WeatherCity = place.Name ?? city.Trim(), WeatherLatitude = place.Latitude, WeatherLongitude = place.Longitude };
                SaveSettings();
                tray.WeatherCity = settings.WeatherCity;
                weather.Use(place);
            }));
        });
        tray.WeatherClicked += () => Safe(() =>
        {
            if (weather.On)
            {
                weather.TurnOff();
                settings = settings with { WeatherEnabled = false };
                SaveSettings();
                tray.WeatherCity = null;
            }
            else AskCity("شو مدينتك؟ (للمطر بس)");
        });
        // 🕌 Prayer times: the city search is the weather's (same stub in tests).
        using var prayer = new PrayerTool(ui, listBubble, bubble, () => motion.Body, weather.FindAsync, director,
            line => speech.Say(line), () => settings, s => { settings = s; SaveSettings(); }, _options.TestMode);
        lifeDirector.AdhanOn = () => director.Adhan;
        var music = new MusicTool(ui, media, line => speech.Say(line));
        using var quran = new QuranTool(ui, listBubble, () => motion.Body, media, line => speech.Say(line),
            () => settings, s => { settings = s; SaveSettings(); }, _options.QuranBase);
        quran.PlayingChanged += on => Safe(() => director.SetQuran(on));
        menu.QuranPlaying = () => quran.Playing;
        lifeDirector.QuranOn = () => quran.Playing;
        menu.RingOpened += ring => Safe(() => { if (ring == "music") music.SayNowPlaying(); });
        tray.PowerChanged += () => Safe(() => { timerTool.Check(); dndTool.Check(); prayer.Check(); });
        tray.SessionLocked += locked => Safe(() => { if (!locked) { timerTool.Check(); dndTool.Check(); } });
        using var show = new ShowcasePlayer(ui, motion, sahar, key => Express(key, false), () => Express("", false));
        menu.OpenChanged += open =>
        {
            _state.MenuOpen = open;
            if (open) show.Stop(flyHome: false);
            lifeDirector.Paused = open || show.Running;
            if (open) motion.Play(MotionKind.Hover);
        };
        show.RunningChanged += running =>
        {
            tray.ShowRunning = running;
            lifeDirector.Paused = running || menu.IsOpen;
        };
        tray.ShowToggled += () => Safe(() => { if (show.Running) show.Stop(); else show.Start(); });
        motion.DragStarted += () => Safe(() => show.Stop(flyHome: false));
        stopShow = flyHome => show.Stop(flyHome);
        showRunning = () => show.Running;
        var playRng = new Random();
        menu.Pressed += id => Safe(() =>
        {
            switch (id)
            {
                case "activity-calm": SetActivity(ActivityLevel.Calm); break;
                case "activity-medium": SetActivity(ActivityLevel.Medium); break;
                case "activity-lively": SetActivity(ActivityLevel.Lively); break;
                case "hide": director.HideFor(TimeSpan.FromHours(1)); break;
                case "timer-5": timerTool.Start(TimeSpan.FromMinutes(5)); break;
                case "timer-15": timerTool.Start(TimeSpan.FromMinutes(15)); break;
                case "timer-25": timerTool.Start(TimeSpan.FromMinutes(25)); break;
                case "timer-45": timerTool.Start(TimeSpan.FromMinutes(45)); break;
                case "timer-cancel": timerTool.Cancel(); break;
                case "note": notes.Open(); break;
                case "clipboard": clipboardTool.Open(); break;
                case "prayer": prayer.Open(); break;
                case "music-prev" or "music-toggle" or "music-next": music.Press(id); break;
                case "quran": quran.Open(); break;
                case "quran-stop": quran.Stop(); break;
                case "timer-custom":
                    bubble.Show(motion.Body, "كم دقيقة؟ (1–600)", [], text => Safe(() =>
                    {
                        if (TimerInput.ParseMinutes(text) is { } minutes) timerTool.Start(TimeSpan.FromMinutes(minutes));
                        else
                        {
                            Log.Write($"timer: typed time not understood ({text.Length} characters)");   // never what was typed (spec §14)
                            Express("error", true);
                        }
                    }));
                    break;
                case "dnd-30": dndTool.TurnOn(TimeSpan.FromMinutes(30)); break;
                case "dnd-60": dndTool.TurnOn(TimeSpan.FromHours(1)); break;
                case "dnd-120": dndTool.TurnOn(TimeSpan.FromHours(2)); break;
                case "dnd-on": dndTool.TurnOn(null); break;
                case "dnd-off": dndTool.TurnOff(); break;
                case "play":
                    var picks = Showcase.PlayPicks;
                    int pick = playRng.Next(picks.Count + 1);
                    if (pick == picks.Count)
                        motion.PlayFlight(Enum.GetValues<FlightPattern>()[playRng.Next(Enum.GetValues<FlightPattern>().Length)], force: true);
                    else motion.Play(picks[pick]);
                    break;
            }
        });
        tray.EndSession += () => Safe(lifeDirector.Save);   // shutdown, logoff, update restart
        tray.EndSession += () => Safe(() => { if (!_state.Hidden) motion.Play(MotionKind.Goodbye); });
        // The PC just started (or a test says so): she flies in and says hello (motion 18).
        if (Hello.ShouldGreet(DateTimeOffset.Now, SignIn.Time(), _options.TestMode, _options.SimulateBoot))
            ui.TryEnqueue(() => Safe(() => { Log.Write("hello: the PC just started"); motion.Play(MotionKind.StartupEntry); }));
        Foreground.Start();

        void OnDisplayChanged()
        {
            menu.Close();
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
                $" face={sahar.FaceShown.Eyes}/{sahar.FaceShown.Mouth} bodyScale={(motion.BodyScaleBusy ? "busy" : "free")}" +
                FormattableString.Invariant($" gcHeapMB={GC.GetTotalMemory(false) / 1048576.0:0.0} privateMB={Environment.WorkingSet / 1048576.0:0.0}")));
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
            pipe.Register("menu-open", _ =>
            {
                if (show.Running) show.Stop(flyHome: false);
                if (!_state.Hidden) menu.Open();
                return new PipeReply(true, menu.Describe(), _state.Snapshot());
            });
            pipe.Register("menu-close", _ => { menu.Close(); return new PipeReply(true, menu.Describe(), _state.Snapshot()); });
            pipe.Register("menu-press", r =>
            {
                if (r.Args is null || !r.Args.TryGetValue("id", out var id)) return new PipeReply(false, "menu-press needs 'id'");
                if (!menu.IsOpen) return new PipeReply(false, "menu is closed");
                menu.Press(id);
                return new PipeReply(true, menu.Describe(), _state.Snapshot());
            });
            pipe.Register("debug-menu", _ => new PipeReply(true, menu.Describe()));
            pipe.Register("debug-timer", _ => new PipeReply(true, timerTool.Describe()));
            pipe.Register("debug-clipboard", _ =>
            {
                var items = clipboard.History.Items;
                var first = items.Count > 0 ? items[0].Text ?? "" : "";
                return new PipeReply(true, $"count={items.Count} first={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(first))}");
            });
            pipe.Register("clipboard-pick", r => new PipeReply(clipboardTool.Pick((int)Arg(r, "index"))));
            pipe.Register("clipboard-clear", _ => { clipboardTool.Clear(); return new PipeReply(true); });
            pipe.Register("debug-cameramic", r =>
            {
                cameraMic.Simulate(r.Args?.TryGetValue("on", out var on) == true && on == "true");
                return new PipeReply(true, lifeDirector.Describe());
            });
            pipe.Register("weather-open", _ => { if (!weather.On) AskCity("شو مدينتك؟ (للمطر بس)"); return new PipeReply(true); });
            pipe.Register("debug-weather", _ => new PipeReply(true, $"on={(weather.On ? "true" : "false")}"));
            pipe.Register("debug-apptime", _ => new PipeReply(true, apps.Describe()));
            pipe.Register("apptime-skip", r => { apps.Skip(TimeSpan.FromMinutes(Arg(r, "minutes"))); return new PipeReply(true, apps.Describe()); });
            pipe.Register("say", r =>
            {
                var text = r.Args?.TryGetValue("text64", out var t) == true ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t)) : "";
                speech.Say(text, comment: true);
                return new PipeReply(true);
            });
            pipe.Register("comment-now", _ => new PipeReply(apps.Consider(), apps.Describe()));
            pipe.Register("debug-comment", _ => new PipeReply(true,
                $"showing={(speech.Showing ? "true" : "false")} text64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(speech.Text ?? ""))} " + apps.Describe()));
            pipe.Register("debug-keys", _ => new PipeReply(true, $"printScreen={printScreenSeen}"));
            pipe.Register("debug-cpu", r => { cpu.Simulate(Arg(r, "percent"), (int)Arg(r, "count")); return new PipeReply(true, State: _state.Snapshot()); });
            pipe.Register("debug-list", _ => new PipeReply(true, $"open={(listBubble.IsOpen ? "true" : "false")} lines={listBubble.Lines} " +
                $"lines64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(listBubble.Text))} button64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(listBubble.ButtonText))}"));
            pipe.Register("list-pick", r =>
            {
                if (!listBubble.IsOpen) return new PipeReply(false, "no list");
                listBubble.PickLine((int)Arg(r, "index"));
                return new PipeReply(true);
            });
            pipe.Register("list-button", _ =>
            {
                if (!listBubble.IsOpen) return new PipeReply(false, "no list");
                listBubble.PressButton();
                return new PipeReply(true);
            });
            pipe.Register("quran-play", r =>
            {
                int surah = (int)Arg(r, "surah");
                if (surah is < 1 or > 114) return new PipeReply(false, "surah 1–114");
                Func<IRecitation>? fake = r.Args?.ContainsKey("fake") == true ? () => new FakeRecitation(ui, TimeSpan.FromSeconds(Arg(r, "fake"))) : null;
                quran.Play(surah, r.Args?.TryGetValue("reciter", out var who) == true ? who : settings.QuranReciter, fake);
                return new PipeReply(true, quran.Describe());
            });
            pipe.Register("debug-quran", _ => new PipeReply(true, quran.Describe()));
            pipe.Register("debug-music", _ => new PipeReply(true, music.Describe()));
            pipe.Register("debug-prayer", _ => new PipeReply(true, prayer.Describe()));
            pipe.Register("prayer-at", r =>
            {
                if (r.Args is null || !r.Args.TryGetValue("at", out var at) ||
                    !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
                    return new PipeReply(false, "prayer-at needs 'at' (ISO 8601)");
                prayer.SetClock(when);
                return new PipeReply(true, prayer.Describe());
            });
            pipe.Register("list-close", _ => { listBubble.Close(); return new PipeReply(true); });
            pipe.Register("showcase", r =>
            {
                show.Start(r.Args?.ContainsKey("from") == true ? (int)Arg(r, "from") : 0);
                return new PipeReply(true, show.Describe());
            });
            pipe.Register("showcase-stop", _ => { show.Stop(); return new PipeReply(true, show.Describe()); });
            pipe.Register("debug-showcase", _ => new PipeReply(true, show.Describe()));
            pipe.Register("timer", r => { timerTool.Start(TimeSpan.FromSeconds(Arg(r, "seconds"))); return new PipeReply(true, timerTool.Describe()); });
            pipe.Register("debug-dnd", _ => new PipeReply(true, dndTool.Describe()));
            pipe.Register("debug-bubble", _ =>
            {
                var e = bubble.EditRect;
                return new PipeReply(true, bubble.IsOpen
                    ? $"open=true x={e.Left} y={e.Top} w={e.Width} h={e.Height} edit={(long)bubble.EditHwnd} title64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(bubble.Title))} " +
                      $"lines64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', bubble.Lines)))}"
                    : "open=false");
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
