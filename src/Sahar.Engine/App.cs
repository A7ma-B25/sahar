using Sahar.Engine.Activities;
using Sahar.Engine.Games;
using Sahar.Core.Games;
using System.Globalization;
using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
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
using Sahar.Core.Update;
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
    /// <summary>«امسح كل شي عني» (the settings app's erase-all): set before the engine quits; Program erases her data
    /// folder once everything is disposed (so nothing writes it back).</summary>
    public static bool EraseRequested { get; private set; }

    private readonly EngineOptions _options;
    private readonly StateReporter _state = new();

    public App(EngineOptions options) => _options = options;

    public int Run()
    {
        var ui = CompositionHost.EnsureDispatcherQueue();
        var (_, work, dpi) = Native.MonitorAt(new POINT());
        // A 1-px placeholder until her first position: the stage sizes the overlay to her (never a whole monitor).
        var overlay = new OverlayWindow(_options.Capturable, new RectI(work.Right - 1, work.Bottom - 1, work.Right, work.Bottom));
        using var host = new CompositionHost(overlay.Hwnd);
        // Settings: a bad or locked file never stops her (defaults); a failed load also blocks saves (SettingsStore).
        var settingsLog = new Sahar.Core.Diagnostics.OneShot();
        var settings = new SaharSettings();
        try { settings = SettingsStore.Load(SettingsStore.DefaultPath); }
        catch (Exception e) { Log.Write($"settings: not loaded ({e.GetType().Name})"); }
        Address.You = settings.UserGender;   // her words agree with who she talks to
        Address.Name = settings.UserName;
        // Two programs write settings.json (0.12): every save is load → change → save under the store's lock, never a
        // whole copy kept in memory, so a change the settings app made meanwhile is kept. A refused save keeps what she
        // has in memory and logs once per kind; never the path (it holds the user name).
        // What the file holds beyond her own change (the settings app saved it, its reload not here yet) is applied
        // too, never just absorbed (review fix m4): set once the parts it applies exist.
        Action<SaharSettings, SaharSettings> applyOthers = (_, _) => { };
        void Save(Func<SaharSettings, SaharSettings> change)
        {
            var mine = change(settings);
            settings = mine;
            try
            {
                var saved = SettingsStore.Update(SettingsStore.DefaultPath, change);
                settings = saved;
                applyOthers(mine, saved);
            }
            catch (Exception e) { if (settingsLog.First(e.GetType().Name)) Log.Write(e is SettingsSaveRefusedException ? "settings: not saved (the file could not be read; it is kept)" : $"settings: not saved ({e.GetType().Name})"); }
        }
        // Overlay coordinates are screen pixels (Origin 0,0): the stage shifts the composition root by the window's place.
        // Her saved size (small 48, medium 80, large 120 DIP; 0.12: it was always medium before).
        var sahar = new SaharVisual(host, Rig.Load(), (float)settings.Size, dpi / 96f) { Origin = Vector2.Zero };
        sahar.Position = StartPosition(sahar, work);
        sahar.SetInnerStars(settings.InnerStars);
        // Her soul (0.10a): float, sway, leaf lag, hop, look and blink on the compositor; her original loops beside it.
        var soul = new SoulLayer(sahar, ui);
        // Windows "Animation effects" off: no idle motion, instant state changes (spec §2). A test machine (the CI
        // runner has them off) ignores Windows here: only the pipe's debug-reduced-motion turns it on there.
        bool machineReduced = ReducedMotion.AnimationsOff();
        if (machineReduced) Log.Write(_options.TestMode ? "motion: Windows animation effects off (ignored in test mode)" : "motion: reduced on");
        soul.SetReducedMotion(machineReduced && !_options.TestMode);
        ExpressionPlayer.Reduced = soul.Reduced;
        IdleAnimations.Start(sahar, soul.Reduced);
        soul.Start();
        var player = new ExpressionPlayer(sahar, soul);
        sahar.Rescaled += () =>
        {
            IdleAnimations.Start(sahar, soul.Reduced);
            soul.Start();
            player.Reapply();
        };
        host.DeviceLost += () =>
        {
            sahar.Redraw();
            soul.Start();
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
        // A spare overlay window: a need that moves the overlay's corner moves her drawing to it in one commit (0.11).
        var spareOverlay = new OverlayWindow(_options.Capturable, new RectI(work.Right - 1, work.Bottom - 1, work.Right, work.Bottom),
            visible: false, title: "Sahar spare");
        var stage = new OverlayStage(overlay, host.Target, spareOverlay, host.AddTarget(spareOverlay.Hwnd), host.Root,
            monitors.Select(m => m.Bounds).ToList());
        RectF Canvas() => new(sahar.Position.X, sahar.Position.Y, sahar.CanvasPx.Width, sahar.CanvasPx.Height);
        sahar.Need = (who, area) => stage.Need(who, area);
        ActivityHub? acts = null;   // 0.13: her tag follows her (set below, once the menu and the senses exist)
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
            // One batch per move: her input window and the overlay (position only while the size stays).
            stage.Follow(Canvas(), sahar.DpiScale, input.MoveTo(sahar.HitRect));
            acts?.OnMoved();
        }
        MoveTo(sahar.Position);   // her monitor, her DPI, and the overlay on her from the first frame

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
        var motion = new MotionController(sahar, player, ticker, MoveTo, Express, Face, soul);
        soul.Motion = () => motion.Current;
        soul.Still = () => motion.Still;
        soul.Grounded = () => player.Current.Extras.HasFlag(Sahar.Core.Expressions.Extras.Grounded);
        soul.CurrentFace = () => (player.Current.Eyes, player.Current.Mouth);
        void SetReduced(bool on)
        {
            if (on == soul.Reduced) return;
            soul.SetReducedMotion(on);
            ExpressionPlayer.Reduced = on;
            IdleAnimations.Start(sahar, on);
            Log.Write($"motion: reduced {(on ? "on" : "off")}");
        }
        overlay.SettingChanged += () => Safe(() => { if (!_options.TestMode) SetReduced(ReducedMotion.AnimationsOff()); });
        _state.MotionName = () => motion.Current.ToString();
        LifeDirector? life = null;
        using var menu = new MenuCard(host, _options.Capturable, () => motion.Body, sahar, ui) { Need = stage.Need };
        Director? dismissDirector = null;
        Action<bool>? stopShow = null;
        Func<bool> dismissSpeech = () => false;
        Func<bool> sulkPoke = () => false;
        Func<bool> showRunning = () => false;
        Action poke = () => { };
        _ = new InputController(input, motion, () => poke(), how =>
        {
            // Opening (spec §0.6): right-click and the shortcut by default; a refused double-click is a poke, as ever.
            var trigger = how == "double" ? MenuTrigger.DoubleClick : MenuTrigger.RightClick;
            if (!MenuOpenRule.Allows(settings.MenuOpen, trigger)) { if (trigger == MenuTrigger.DoubleClick) poke(); return; }
            if (_state.Hidden || motion.Current is MotionKind.Drag or MotionKind.Throw) return;
            if (showRunning()) stopShow?.Invoke(false);   // first, so the card opens around where she stays
            menu.Toggle(how);
        });
        poke = () =>
        {
            // A click during her show stops it.
            if (showRunning()) { stopShow?.Invoke(false); return; }
            if (sulkPoke()) return;        // sulking: "هِم!", or the third poke makes up
            if (dismissSpeech()) return;   // a click on her closes her comment (and she'll comment less)
            // A poke on the timer's face says "seen": it goes away.
            if (dismissDirector?.CurrentKey == "timer-done") dismissDirector.End(EventKind.TimerDone);
            if (dismissDirector?.CurrentKey == "reminder") dismissDirector.End(EventKind.Reminder);
            life?.OnPoked();
            // A tap does one thing (0.11, the owner's report): the hop with her joy face; no "poked" face or mood
            // flash on top (that was three face changes in a second). Refused hop: the joy face alone.
            if (!soul.Hop(1f)) soul.HappyFace();
        };
        // Senses → brain → what she shows. Hidden means gone: she shrinks away, then the overlay is hidden
        // (no dot over a fullscreen app); she comes back where she was.
        void SetHidden(bool hidden)
        {
            _state.Hidden = hidden;
            acts?.FullscreenAway(hidden && director?.Where == FullscreenWhere.Hers);
            acts?.Refresh();
            if (hidden) { menu.Close(); stopShow?.Invoke(false); }
            input.SetVisible(!hidden);
            if (hidden) motion.Play(MotionKind.FullscreenHide);
            else
            {
                stage.SetHidden(false);
                motion.Play(MotionKind.Wake);
            }
        }
        motion.HiddenAway += () => { if (_state.Hidden) stage.SetHidden(true); };
        // Fullscreen hides her only on her own monitor (0.10.1); indexes in her monitor list, never titles, in the log.
        director = new Director(ui, key => Express(key, false), SetHidden,
            handle => monitors.FindIndex(m => m.Handle == handle) is var i && i >= 0 ? i : FullscreenScope.UnknownMonitor,
            () => monitorIndex);
        dismissDirector = director;
        var timerTool = new TimerTool(ui, sahar, director);
        var dndTool = new DndTool(ui, director);
        var bubble = new TextBubble(_options.Capturable);
        var notes = new NoteTool(bubble, new NotesStore(NotesStore.DefaultPath), () => motion.Body, Express);
        var listBubble = new ListBubble(_options.Capturable);
        sahar.Rescaled += () => Safe(timerTool.Redraw);
        host.DeviceLost += () => Safe(() => { timerTool.Redraw(); menu.DeviceLost(); });
        using var tray = new Tray { Activity = settings.Activity };
        void SetActivity(ActivityLevel level)
        {
            Log.Write($"activity: {level}");
            Save(s => s with { Activity = level });
            tray.Activity = level;
            if (life != null) life.Level = level;
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
        var idle = new Idle(director.Post, () => tray.SinceKeyboard);
        tray.SessionLocked += locked => Safe(() => { if (!_options.TestMode) idle.SetLocked(locked); if (!locked) host.CheckDevice(); });
        // Nothing draws while she rests, so ask the GPU every second whether it is still there.
        var deviceTimer = ui.CreateTimer();
        deviceTimer.Interval = TimeSpan.FromSeconds(1);
        deviceTimer.Tick += (_, _) => Safe(() => host.CheckDevice());
        deviceTimer.Start();
        var idleTimer = ui.CreateTimer();
        idleTimer.Interval = TimeSpan.FromSeconds(1);
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
            Away = () => idle.Away,
            AutoMood = () => settings.AutoMood,
            PlaysAlone = () => settings.PlaysAlone,
        };
        life = lifeDirector;
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
        lifeDirector.DndOn = () => dndTool.On;
        lifeDirector.CameraMicOn = () => cameraMic.InUse;
        // The app in front, and her gentle comments about it (spec §8.1, §8.2).
        using var speech = new SpeechBubble(ui, sahar, () => motion.Body);
        var apps = new AppWatch(ui,
            allowed: () => settings.AwarenessEnabled && CommentRules.Allowed(lifeDirector.Typing, director.Quiet, menu.IsOpen, _state.Hidden) && !showRunning(),
            away: () => idle.Away,
            say: line => speech.Say(Address.ToYou(line), comment: true), ownComments: !_options.TestMode);   // her words agree with you
        // Ignored or clicked away, she comments less (spec §8.2); a new day brings her back to 45 minutes.
        speech.Ended += _ => apps.Limiter.Dismissed(DateTimeOffset.Now);
        dismissSpeech = () => { if (!speech.Showing) return false; speech.Dismiss(); return true; };
        // Rain coming (opt-in): the tray item asks for your city.
        // Owner ruling 4 (0.13): with no city of its own the weather uses the prayer place.
        var weatherPlace = settings.WeatherEnabled ? Sahar.Core.Activities.WeatherCity.Resolve(settings) : null;
        using var weather = new Weather(ui, _options.WeatherStubDir, weatherPlace?.Lat, weatherPlace?.Lon);
        tray.WeatherCity = weather.On ? settings.WeatherCity : null;
        void AskCity(string title) => bubble.Show(motion.Body, title, [], city => SetCity(city));
        void SetCity(string city) => weather.FindAsync(city).ContinueWith(t =>
        {
            var (place, error) = t.IsCompletedSuccessfully ? t.Result : (null, "ما في نت هلق، جرّب بعدين");
            ui.TryEnqueue(() => Safe(() =>
            {
                if (place is null) { AskCity($"{error} — جرّب مدينة تانية"); return; }
                Save(s => s with { WeatherEnabled = true, WeatherCity = place.Name ?? city.Trim(), WeatherLatitude = place.Latitude, WeatherLongitude = place.Longitude });
                tray.WeatherCity = settings.WeatherCity;
                weather.Use(place);
            }));
        });
        tray.WeatherClicked += () => Safe(() =>
        {
            if (weather.On)
            {
                weather.TurnOff();
                Save(s => s with { WeatherEnabled = false });
                tray.WeatherCity = null;
            }
            else AskCity("شو مدينتك؟ (للمطر بس)");
        });
        // 🕌 Prayer times: the city search is the weather's (same stub in tests).
        using var prayer = new PrayerTool(ui, listBubble, bubble, () => motion.Body, weather.FindAsync, director,
            line => speech.Say(line), () => settings, Save, _options.TestMode);
        lifeDirector.AdhanOn = () => director.Adhan;

        // Shaken while dragged (0.8): dizzy, sick, or angry — angry is more likely if you did it again within 5 minutes.
        // Angry, she sulks for 20 s: the pointer coming near makes her drift away, a click says "هِم!", the third
        // gentle poke (or a face that needs you) makes up.
        var shakes = new List<DateTimeOffset>();
        var sulk = new Sulk();
        var sulkTimer = ui.CreateTimer();   // one timer, kept alive; each sulk knows its own number
        sulkTimer.IsRepeating = false;
        int sulkTimerGen = 0;
        sulkTimer.Tick += (_, _) => Safe(() => { if (sulk.TimerFired(sulkTimerGen)) SulkOver("time"); });
        ShakeReaction? forcedReaction = null;
        var lastDrift = DateTimeOffset.MinValue;
        bool Sulking() => sulk.Active(DateTimeOffset.UtcNow);
        void SulkOver(string why)
        {
            Log.Write($"shake: sulk over ({why})");
            Express("", false);   // her angry face goes
            if (why == "made up") { speech.Say("خلص سامحتك 💜"); Express("poked", true); }
        }
        motion.ShakeReact = strength =>
        {
            var now = DateTimeOffset.UtcNow;
            shakes.RemoveAll(t => now - t > TimeSpan.FromMinutes(5));
            var r = forcedReaction ?? ShakeMood.Pick(strength, shakes.Count, Random.Shared.NextDouble());
            forcedReaction = null;
            shakes.Add(now);
            return r;
        };
        motion.Shaken += (r, strength) => Safe(() =>
        {
            Log.Write($"shake: {r} (strength {strength})");
            if (r != ShakeReaction.Angry) return;
            int gen = sulk.Start(DateTimeOffset.UtcNow);
            sulkTimer.Stop();
            sulkTimer.Interval = Sulk.Length;
            sulkTimerGen = gen;
            sulkTimer.Start();
        });
        sulkPoke = () =>
        {
            switch (sulk.Poke())
            {
                case SulkPoke.NotSulking: return false;
                case SulkPoke.MadeUp: SulkOver("made up"); return true;
                default: speech.Say("هِم! 😤"); motion.Squish(); return true;
            }
        };
        motion.PointerNear += d => Safe(() =>
        {
            if (!Sulking() || d > 70 || DateTimeOffset.UtcNow - lastDrift < TimeSpan.FromSeconds(1.5)) return;
            lastDrift = DateTimeOffset.UtcNow;
            motion.DriftAway();
        });
        // 🎮 Games (0.9): while one runs, life waits and the game moves her.
        using var games = new GamePlayer(ui, host, sahar, motion, line => speech.Say(line), Express, _options.Capturable);
        lifeDirector.Sulking = () => Sulking() || games.Running;
        games.RunningChanged += on => Safe(() => input.SetVisible(!on && !_state.Hidden));   // the game gets every click
        lifeDirector.RandomTypeAlong = !_options.TestMode;
        motion.Sulking = Sulking;
        bool chatHop = false;   // her answer to a chat line is a hop, with the card open (spec §5)
        soul.Busy = () => _state.Hidden || Sulking() || games.Running || (menu.IsOpen && !chatHop);   // nothing else hops her while the menu is open
        director.NeedsYou += () => Safe(() => { if (sulk.End()) SulkOver("a face needs you"); });

        // The first start: she says hello and asks your name and whether you are a man or a woman, so her words
        // agree with you. Skipped answers are fine; closing the name bubble asks again next start.
        void StartHello()
        {
            Log.Write("hello: asking who you are");
            speech.Say("أهلين! أنا سَحَر 🌙");
            bubble.Show(motion.Body, "شو بدك ناديك؟ (اسمك)", [], text => Safe(() =>
            {
                var name = Address.CleanName(text);
                ui.TryEnqueue(() => Safe(() => listBubble.Show(motion.Body, "انت شب ولا بنت؟", ["شب 👦", "بنت 👧"], "ما بدي قول",
                    i => ui.TryEnqueue(() => Safe(() => FinishHello(name, i switch { 0 => Gender.Male, 1 => Gender.Female, _ => Gender.Unknown }))),
                    () => ui.TryEnqueue(() => Safe(() => FinishHello(name, Gender.Unknown))))));
            }));
        }

        void FinishHello(string? name, Gender you)
        {
            Save(s => s with { UserName = name, UserGender = you, HelloDone = true });
            Address.You = you;
            Address.Name = name;
            Log.Write($"hello: done ({you})");   // never the name (spec §14)
            speech.Say(name is null ? "تشرفنا 💜" : $"تشرفنا يا {name} 💜");
        }

        tray.WhoAmI += () => Safe(StartHello);
        tray.SettingsAvailable = () => SettingsLauncher.Available;
        tray.SettingsClicked += () => Safe(() => SettingsLauncher.Open(_options.TestMode));
        if (!_options.TestMode && !settings.HelloDone)
        {
            var helloTimer = ui.CreateTimer();
            helloTimer.IsRepeating = false;
            helloTimer.Interval = TimeSpan.FromSeconds(4);
            helloTimer.Tick += (_, _) => Safe(StartHello);
            helloTimer.Start();
        }
        var music = new MusicTool(ui, media, line => speech.Say(line));
        using var quran = new QuranTool(ui, listBubble, () => motion.Body, media, line => speech.Say(line),
            () => settings, Save, _options.QuranBase);
        quran.PlayingChanged += on => Safe(() => director.SetQuran(on));
        menu.State = () => new Sahar.Core.Menu.MenuState(timerTool.Running, dndTool.On, quran.Playing);
        menu.Facts = () => new Sahar.Core.Menu.MenuFacts(timerTool.Remaining, director.CurrentKey == "claude-permission", prayer.Next(),
            quran.Playing, dndTool.Remaining, dndTool.UntilOff);
        menu.Mood = () => player.Current.Mood;
        menu.Reload(settings, _ => true);
        overlay.SettingChanged += () => Safe(menu.Refresh);   // light or dark apps changed (WM_SETTINGCHANGE ImmersiveColorSet)
        // The shortcut (default Ctrl+Alt+S): opens the card with the focus in the chat row, or closes it.
        var hotkey = new Hotkey(tray.Hwnd);
        var shortcut = Shortcuts.Check(settings.MenuShortcut);
        if (shortcut.Ok) hotkey.TryRegister(shortcut.Value, out _);
        else Log.Write("hotkey: invalid");   // never the text
        tray.HotkeyPressed += id => Safe(() =>
        {
            if (id != Hotkey.Id) return;
            if (menu.IsOpen) { menu.Close(); return; }
            // As a right-click: not while hidden, dragged or thrown; and a game keeps the screen (review 0.11 #5).
            if (_state.Hidden || games.Running || motion.Current is MotionKind.Drag or MotionKind.Throw) return;
            if (showRunning()) stopShow?.Invoke(false);
            menu.Open("shortcut", caretInChat: true);
        });
        lifeDirector.QuranOn = () => quran.Playing;
        // 0.13 «النشاطات»: the tag she carries, its cards, the knock, and the sources that feed them.
        acts = new ActivityHub(ui, host, _options.Capturable, sahar, () => motion.Body)
        {
            Dark = () => menu.Dark(),
            MenuOpen = () => menu.IsOpen,
            Hidden = () => _state.Hidden,
            Dragged = () => motion.Current is MotionKind.Drag or MotionKind.Throw,
            Hushed = () => dndTool.On,
            InCall = () => cameraMic.InUse,
            Settings = () => settings,
            Fullscreen = () => director.Where,
            FullscreenMonitor = () => director.FullscreenMonitor != 0 ? Monitors.Describe(director.FullscreenMonitor).Bounds : null,
            Say = line => speech.Say(line),
            NeedArea = stage.Need,
        };
        using var activities = acts;
        var claudeSource = new ClaudeSource(acts, director.Post);
        Task.Run(HookMode.Follow);   // linked hooks follow her when she moved (review #6)
        var weatherSource = new WeatherSource(acts, weather, () => settings, director.Post);
        var mediaSource = new MediaSource(ui, acts, () => settings, Save, Path.Combine(Log.Folder, "lyrics"));
        // Her expressions for activities (0.13 §1.5): music sways her and tints her halo with the cover; rain droops her
        // leaf; back from away she hops.
        void Expressions()
        {
            bool music = mediaSource.Playing && settings.ActMusic;
            soul.SetMusicSway(music);
            sahar.SetHaloTint(music && mediaSource.Colour is { } c ? Sahar.Core.Activities.MediaNow.HaloTint(c) : null);
            soul.SetDroop(weatherSource.RainSoon && settings.ActWeather ? 14f : 0f);
        }
        mediaSource.PlayingChanged += () => Safe(Expressions);
        weatherSource.Changed += () => Safe(Expressions);
        acts.Welcomed += () => Safe(() => { if (!soul.Hop(1f)) soul.HappyFace(); });
        var systemSource = new SystemSource(ui, acts, cameraMic);
        var copySource = new CopySource(ui, acts);
        // Her own timer is a thing you wait for (rank 3): «المؤقت» with the time left; a click opens her menu.
        var timerTag = ui.CreateTimer();
        timerTag.Interval = TimeSpan.FromSeconds(1);
        timerTag.Tick += (_, _) => Safe(() =>
        {
            if (timerTool.Remaining is { } left)
                acts.Set(new Sahar.Core.Activities.Activity("timer", Sahar.Core.Activities.ActivityKind.Timer, "المؤقت",
                    Sahar.Core.Activities.ActivityText.SongTime(left), "hourglass"));
            else acts.Remove("timer");
        });
        timerTag.Start();
        bool sessionLocked = false;
        tray.SessionLocked += locked => sessionLocked = locked;
        using var screenTime = new ScreenTimeSource(ui, acts, Path.Combine(Log.Folder, "usage"), () => settings, () => idle.Away, () => sessionLocked,
            () => media.VideoPlaying);
        using var shelfSource = new ShelfSource(ui, acts, Path.Combine(Log.Folder, "shelf.json"), () => settings, line => speech.Say(line),
            () => { motion.Squish(); }, clipboard);
        {
            // Files dropped on her land on her shelf (WM_DROPFILES on her input window; the rest goes on as before).
            var inputHandler = input.Handler;
            ActNative.DragAcceptFiles(input.Hwnd, 1);
            input.Handler = (h, m, w, l) => m == ActNative.WM_DROPFILES ? Run(() => shelfSource.Drop((nint)w)) : inputHandler?.Invoke(h, m, w, l);
            static nint? Run(Action a) { Safe(a); return 0; }
        }
        power.Changed += (pct, charging, low, plugged) => Safe(() => systemSource.Battery(pct, charging, low, plugged));
        power.Check();
        tray.SessionLocked += locked => Safe(() => { if (!locked) weather.Stale(); });
        tray.SessionLocked += locked => Safe(() => acts.Locked(locked));
        host.DeviceLost += () => Safe(acts.DeviceLost);
        overlay.SettingChanged += () => Safe(acts.Refresh);
        motion.DragStarted += () => Safe(acts.Refresh);
        acts.TagClicked += a => Safe(() => { if (a.Kind == Sahar.Core.Activities.ActivityKind.Timer) menu.Open("tag"); });
        menu.RingOpened += ring => Safe(() => { if (ring == "music") music.SayNowPlaying(); });
        tray.PowerChanged += () => Safe(() => { timerTool.Check(); dndTool.Check(); prayer.Check(); });
        tray.SessionLocked += locked => Safe(() => { if (!locked) { timerTool.Check(); dndTool.Check(); } });
        using var show = new ShowcasePlayer(ui, motion, sahar, key => Express(key, false), () => Express("", false));
        menu.OpenChanged += open =>
        {
            _state.MenuOpen = open;
            if (open) acts.CloseCard();
            acts.Refresh();
            if (open)
            {
                // A double-click is a poke, then the menu: the hop and the joy face are cut (eased) and a passing face
                // (poked) goes, so the buttons pop around her settled, with one face (0.10.1, the owner's report).
                if (revert.IsRunning) Express("", false);   // first: the joy face ends straight into her own face
                soul.Settle();
                show.Stop(flyHome: false);
            }
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
        // Now and then, at rest, a small hop or a glance aside (spec §4); never while you are away (0.10a pre-flight ruling).
        using var soulDirector = new SoulDirector(ui, soul, kind => SoulIdle.MayAct(motion.Current, kind,
            busy: menu.IsOpen || show.Running || games.Running || _state.Hidden || Sulking() || idle.Away,
            quiet: director.Quiet, typing: lifeDirector.Typing, reduced: soul.Reduced))
        {
            Enabled = !_options.TestMode,
        };
        var playRng = new Random();
        menu.Pressed += id => Safe(() =>
        {
            switch (id)
            {
                case "activity-calm": SetActivity(ActivityLevel.Calm); break;
                case "activity-medium": SetActivity(ActivityLevel.Medium); break;
                case "activity-lively": SetActivity(ActivityLevel.Lively); break;
                case "hide": director.HideFor(TimeSpan.FromHours(1)); break;
                case "settings": SettingsLauncher.Open(_options.TestMode); break;
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
                case "game-chase": games.Start(GameKind.Chase); break;
                case "game-hide": games.Start(GameKind.HideSeek); break;
                case "game-ball": games.Start(GameKind.Ball); break;
                case "game-stars": games.Start(GameKind.Stars); break;
                case "play-random":
                    var picks = Showcase.PlayPicks;
                    int pick = playRng.Next(picks.Count + 1);
                    if (pick == picks.Count)
                        motion.PlayFlight(Enum.GetValues<FlightPattern>()[playRng.Next(Enum.GetValues<FlightPattern>().Length)], force: true);
                    else motion.Play(picks[pick]);
                    break;
            }
        });
        // Her chat row (spec §5.1): local commands; engine.log gets the kind only, never the text.
        int greetings = 0;
        var sleepTimer = ui.CreateTimer();
        sleepTimer.IsRepeating = false;
        sleepTimer.Interval = TimeSpan.FromSeconds(1.2);
        sleepTimer.Tick += (_, _) => Safe(() => director.HideFor(TimeSpan.FromHours(1)));
        void ChatHop()
        {
            chatHop = true;
            try { if (!soul.Hop(1f)) soul.HappyFace(); }
            finally { chatHop = false; }
        }
        Sahar.Engine.Mind.MindEngine? mind = null;   // 0.14: her brain (built once the pipe exists)
        menu.ChatSent += text => Safe(() =>
        {
            var c = Sahar.Core.Chat.ChatCommands.Parse(text);
            Log.Write($"chat: {c.Kind}");
            if (c.Kind == Sahar.Core.Chat.ChatKind.Empty) return;
            if (mind?.TakeChat(c, text) == true) return;   // 0.14: the Mind took a line the fast path does not run
            var reply = Sahar.Core.Chat.ChatReply.For(c, new Sahar.Core.Chat.ChatContext(prayer.Next(), greetings), Address.You);
            switch (c.Kind)
            {
                case Sahar.Core.Chat.ChatKind.Greeting: greetings++; break;
                case Sahar.Core.Chat.ChatKind.Timer:
                    if (!c.OutOfRange && c.Minutes is { } minutes) timerTool.Start(TimeSpan.FromMinutes(minutes));
                    break;
                case Sahar.Core.Chat.ChatKind.Note when c.Text is { } note: notes.Save(note); break;
                case Sahar.Core.Chat.ChatKind.Note: menu.Close(); notes.Open(); return;   // «ملاحظة» alone: the note bubble (Ruling 9)
                case Sahar.Core.Chat.ChatKind.Hush: dndTool.TurnOn(c.HushFor); break;
                case Sahar.Core.Chat.ChatKind.Sleep:
                    menu.Close();
                    speech.Say(reply);
                    sleepTimer.Stop();
                    sleepTimer.Start();
                    return;
                case Sahar.Core.Chat.ChatKind.Play: menu.Press("play"); break;
                case Sahar.Core.Chat.ChatKind.Game when c.Game is { } game:
                    menu.Close();
                    speech.Say(reply);
                    games.Start(game);
                    return;
                case Sahar.Core.Chat.ChatKind.Move when c.Move is { } move:
                    menu.Close();
                    speech.Say(reply);
                    motion.Play(move);
                    return;
                case Sahar.Core.Chat.ChatKind.Prayer when prayer.Next() is null: menu.Close(); prayer.Open(); return;
            }
            menu.SetReply(reply);
            ChatHop();
        });
        // A newer version (owner roadmap: installer + auto-update): she asks in her bubble; yes runs the signed setup.
        // Test mode reaches only a fake GitHub given with --update-host, and checks only when the pipe says so.
        string updateKey = UpdateKeys.Production;
        if (_options.TestMode && _options.UpdateKeyFile is { } keyFile)
            try { updateKey = File.ReadAllText(keyFile).Trim(); } catch (Exception e) { Log.Write($"update: test key not read ({e.GetType().Name})"); }
        string? updateHost = _options.TestMode ? _options.UpdateHost : null;
        using var updates = new UpdateTool(ui, new Updater(updateHost, updateKey), listBubble, () => motion.Body,
            line => speech.Say(line), () => settings, Save,
            busy: () => _state.Hidden || menu.IsOpen || games.Running || show.Running || director.Quiet || idle.Away || lifeDirector.Typing ||
                listBubble.IsOpen || bubble.IsOpen || Sulking(),
            automatic: !_options.TestMode, reachable: !_options.TestMode || updateHost is not null,
            installed: InstallInfo.Installed(), marker: Path.Combine(Log.Folder, "moved-to-installer.txt"));
        var movedTimer = ui.CreateTimer();
        movedTimer.IsRepeating = false;
        movedTimer.Interval = TimeSpan.FromSeconds(_options.TestMode ? 1 : 6);
        movedTimer.Tick += (_, _) => Safe(updates.SayIfMoved);
        movedTimer.Start();
        tray.CheckUpdate += () => Safe(() => updates.Check(manual: true));
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
            stage.Replan(monitors.Select(m => m.Bounds).ToList());
            MoveTo(sahar.Position);
            motion.DisplayChanged();
            host.CheckDevice();
        }
        overlay.DisplayChanged += () => Safe(OnDisplayChanged);

        // A new size keeps her body centre where it is (as a DPI change does in MoveTo).
        void ApplySize(float dip)
        {
            var b = sahar.BodyRect;
            var centre = new Vector2((b.Left + b.Right) / 2f, (b.Top + b.Bottom) / 2f);
            sahar.SetSize(dip, sahar.DpiScale);
            var units = SaharVisual.BodyUnits;
            MoveTo(centre - new Vector2(units.X + units.Width / 2, units.Y + units.Height / 2) * sahar.Scale);
        }
        // The shortcut: a new one replaces the old only when it can be registered; otherwise the old one stays.
        string TryShortcut(string? text, out string? reason)
        {
            var check = Shortcuts.Check(text);
            if (!check.Ok) { reason = check.Reason; Log.Write("hotkey: invalid"); return "invalid"; }   // never the text
            if (hotkey.Current == check.Value) { reason = null; return "ok"; }
            var old = hotkey.Current;
            if (hotkey.TryRegister(check.Value, out reason)) return "ok";
            if (old is { } o) hotkey.TryRegister(o, out _);
            return "taken";
        }
        void ApplyWeather(SaharSettings next)
        {
            if (next.WeatherEnabled && Sahar.Core.Activities.WeatherCity.Resolve(next) is { } place)
            {
                weather.Use(new GeoResult(place.Label, place.Lat, place.Lon));
                tray.WeatherCity = place.Label;
            }
            else
            {
                weather.TurnOff();
                tray.WeatherCity = null;
            }
        }
        // The settings app saved something (0.12): read the file and apply what changed, then she hops.
        (IReadOnlyList<string> Applied, string Hotkey) ReloadSettings(bool askHello)
        {
            SaharSettings next;
            try { next = SettingsStore.Load(SettingsStore.DefaultPath); }
            catch (Exception e)
            {
                Log.Write($"settings: reload failed ({e.GetType().Name})");
                return ([], "same");
            }
            var old = settings;
            settings = next;
            var (changed, hk) = ApplyChanges(old, next);
            Log.Write($"settings: reloaded ({string.Join(", ", changed)})");   // the groups' names only, never a value
            if (askHello && !next.HelloDone) StartHello();   // «نبلّش من جديد»: she asks your name again (owner ruling 2026-11-13)
            // Her answer: a hop with her joy face — never in the adhan's silence, never over another face (review fix m5).
            if (SettingsAppPolicy.CheerFor(director.Adhan, _state.Expression) == Cheer.HopOrFace && !soul.Hop(1f)) soul.HappyFace();
            return (changed, hk);
        }
        // What differs between two settings, applied to her: size, activity, address, menu, stars, prayer, weather, shortcut.
        (IReadOnlyList<string> Applied, string Hotkey) ApplyChanges(SaharSettings old, SaharSettings next)
        {
            var changed = SettingsChanges.Between(old, next);
            string hk = "same";
            foreach (var group in changed)
            {
                switch (group)
                {
                    case "size": ApplySize((float)next.Size); break;
                    case "activity":
                        tray.Activity = next.Activity;
                        lifeDirector.Level = next.Activity;
                        break;
                    case "address":
                        Address.You = next.UserGender;
                        Address.Name = next.UserName;
                        break;
                    case "menu": menu.Reload(next, _ => true); acts.Refresh(); break;
                    case "stars": sahar.SetInnerStars(next.InnerStars); break;
                    case "prayer":
                        prayer.Reload();
                        if (next.WeatherEnabled && string.IsNullOrWhiteSpace(next.WeatherCity)) ApplyWeather(next);   // the weather follows the prayer place
                        break;
                    case "weather": ApplyWeather(next); break;
                    case "shortcut": hk = TryShortcut(next.MenuShortcut, out _); break;
                    case "activities": acts.SettingsChanged(); Expressions(); break;
                    case "mind": mind?.RaiseSettings(next); break;
                }
            }
            return (changed, hk);
        }
        applyOthers = (mine, saved) =>
        {
            var (others, _) = ApplyChanges(mine, saved);
            if (others.Count > 0) Log.Write($"settings: applied on save ({string.Join(", ", others)})");
        };
        static string B64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));

        using var pipe = new PipeServer(ui);
        // The settings app's commands (0.12): production, not test-only. Replies carry key=value data in the error slot.
        pipe.Register("settings-reload", r =>
        {
            var (applied, hk) = ReloadSettings(r.Args?.TryGetValue("hello", out var h) == true && h == "ask");
            return new PipeReply(true, $"applied={string.Join(',', applied)} hotkey={hk}", _state.Snapshot());
        });
        pipe.RegisterAsync("city-search", r =>
        {
            var text = r.Args?.TryGetValue("text64", out var t) == true ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t)) : "";
            return weather.FindAsync(text).ContinueWith(task =>
            {
                var (place, error) = task.IsCompletedSuccessfully ? task.Result : (null, "ما في نت هلق، جرّب بعدين");
                Log.Write(place is null ? "settings: city not found" : "settings: city found");   // never the text
                return place is { } p
                    ? new PipeReply(true, FormattableString.Invariant($"ok=true name64={B64(p.Name ?? text.Trim())} lat={p.Latitude:R} lon={p.Longitude:R}"))
                    : new PipeReply(true, $"ok=false reason64={B64(error ?? "ما لقيت هالمدينة")}");
            }, TaskScheduler.Default);
        });
        pipe.Register("shortcut-try", r =>
        {
            var result = TryShortcut(r.Args?.TryGetValue("value", out var v) == true ? v : null, out var reason);
            return new PipeReply(true, result == "ok" ? "ok=true" : $"ok=false reason64={B64(reason ?? Shortcuts.Taken)}");
        });
        pipe.Register("clipboard-clear", _ => { clipboardTool.Clear(); return new PipeReply(true, "ok=true"); });
        // Claude Code's hooks (0.13, production): one line per hook from «Sahar.Engine.exe hook»; notify only.
        pipe.Register("claude-hook", r =>
        {
            if (Sahar.Core.Activities.ClaudeHook.FromArgs(r.Args) is not { } signal) return new PipeReply(false, "bad hook");
            if (settings.ActClaude) claudeSource.Apply(signal);
            return new PipeReply(true);
        });
        // «امسح كل شي عني» (review fix m2): erased first, then the answer names what is still in use (a pending marker
        // finishes it at her next start); nothing of hers is saved on the way out.
        pipe.Register("erase-all", _ =>
        {
            lifeDirector.KeepOnExit = false;
            var report = DataEraser.Erase(CleanStart.RoamingFolder, [CleanStart.MarkerName]);
            Log.Write($"erase: {report.Removed} removed, {report.Left.Count} in use");   // counts only
            return new PipeReply(true, $"ok=true left64={B64(string.Join('\n', report.Left))}");
        }, afterReply: () =>
        {
            EraseRequested = true;
            Native.PostQuitMessage(0);
        });
        // 0.14: her brain. Each stream fills its own MindEngine.*.cs; App.cs only builds it here.
        mind = new Sahar.Engine.Mind.MindEngine
        {
            Ui = ui, Pipe = pipe, TestMode = _options.TestMode, Settings = () => settings, Save = Save, Menu = menu, Speech = speech,
            Bubble = bubble, ListBubble = listBubble, Timer = timerTool, Dnd = dndTool, Notes = notes, Director = director,
            Games = games, Motion = motion, Soul = soul, Prayer = prayer, Music = music, Quran = quran, Media = media, Idle = idle,
            CameraMic = cameraMic, Activities = acts, Express = Express, SinceKeyboard = () => tray.SinceKeyboard,
            ActivityTools = new Sahar.Engine.Mind.ActivityToolsAdapter(weatherSource, claudeSource, shelfSource, acts, () => settings),
            IsDraftAsk = t => Sahar.Core.Mind.Drafts.DraftAsk.Parse(t) is not null,
        };
        mind.Attach();
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
            // Away and back are the presence sense's (a test machine has no human): it posts them itself.
            if (kind is EventKind.UserAway or EventKind.UserBack) idle.SetAway(kind == EventKind.UserAway);
            else director.Post(kind);
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
                $"generation={host.Generation} overlayVisible={(stage.Visible ? "true" : "false")} " +
                FormattableString.Invariant($"faceOffsetX={soul.LookTarget.X * sahar.Scale:0.##} canvasX={sahar.Position.X:0} canvasY={sahar.Position.Y:0} dpi={sahar.DpiScale:0.###}") +
                $" face={sahar.FaceShown.Eyes}/{sahar.FaceShown.Mouth} faceSwaps={sahar.FaceSwaps} faceAnims={sahar.FaceAnimationsMade + FadeLayer.StepAnimationsMade} moon={(sahar.MoonProps.TryGetScalar("moon", out var mv) == Windows.UI.Composition.CompositionGetValueStatus.Succeeded ? mv : -1):0.##} bodyScale={(motion.BodyScaleBusy ? "busy" : "free")}" +
                FormattableString.Invariant($" gcHeapMB={GC.GetTotalMemory(false) / 1048576.0:0.0} privateMB={Environment.WorkingSet / 1048576.0:0.0}")));
            pipe.Register("debug-overlay", _ => new PipeReply(true, stage.Describe() + $" visible={(stage.Visible ? "true" : "false")}"));
            ActivityPipe.Register(pipe, acts, claudeSource, () => monitors, shelfSource);
            // Her activity expressions on demand (0.13 §1.5): the sway, the halo's cover colour, the drooping leaf.
            pipe.Register("activity-expr", r =>
            {
                bool On(string k) => r.Args?.TryGetValue(k, out var v) == true && v == "true";
                soul.SetMusicSway(On("music"));
                sahar.SetHaloTint(r.Args?.TryGetValue("cover", out var c) == true ? Sahar.Core.Activities.MediaNow.HaloTint(Sahar.Core.Art.Rgba.Parse(c)) : null);
                soul.SetDroop(On("rain") ? 14f : 0f);
                return new PipeReply(true, soul.Describe() + $" halo={sahar.HaloTint?.ToString() ?? "mood"}");
            });
            pipe.Register("debug-simulate-device-removed", _ => { host.SimulateRemoved(); return new PipeReply(true); });
            // Her size for pixel checks (0.11 face stage): a bigger body makes an eye tens of pixels, so two faces show.
            pipe.Register("debug-size", r =>
            {
                float dip = Arg(r, "dip");
                if (dip is < 32 or > 400) return new PipeReply(false, "dip 32–400");
                sahar.SetSize(dip, sahar.DpiScale);
                MoveTo(sahar.Position);
                return new PipeReply(true, State: _state.Snapshot());
            });
            pipe.Register("debug-font", _ => new PipeReply(true, Fonts.Describe()));
            pipe.Register("debug-swap-throw", _ => { stage.ThrowOnNextSwap = true; return new PipeReply(true); });
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
                if (!_state.Hidden) menu.Open("pipe");
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
            pipe.Register("menu-chat", r =>
            {
                if (!menu.IsOpen) return new PipeReply(false, "menu is closed");
                var text = r.Args?.TryGetValue("text64", out var t) == true ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t)) : "";
                menu.Send(text);
                return new PipeReply(true, menu.Describe(), _state.Snapshot());
            });
            pipe.Register("debug-timer", _ => new PipeReply(true, timerTool.Describe()));
            pipe.Register("debug-clipboard", _ =>
            {
                var items = clipboard.History.Items;
                var first = items.Count > 0 ? items[0].Text ?? "" : "";
                return new PipeReply(true, $"count={items.Count} first={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(first))}");
            });
            pipe.Register("clipboard-pick", r => new PipeReply(clipboardTool.Pick((int)Arg(r, "index"))));
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
            pipe.Register("shake", r =>
            {
                // A drag shaken with N swings (fed to the same detector as a real drag), then released.
                if (r.Args?.TryGetValue("force", out var f) == true && Enum.TryParse<ShakeReaction>(f, true, out var forced)) forcedReaction = forced;
                int swings = r.Args?.ContainsKey("swings") == true ? (int)Arg(r, "swings") : 6;
                motion.TestShake(swings);
                return new PipeReply(true);
            });
            pipe.Register("debug-shake", _ => new PipeReply(true,
                $"last={motion.LastReaction} sulking={(Sulking() ? "true" : "false")} pokes={sulk.Pokes} motion={motion.Current}"));
            pipe.Register("game-start", r =>
            {
                if (r.Args?.TryGetValue("kind", out var k) != true || !Enum.TryParse<GameKind>(k, true, out var kind)) return new PipeReply(false, "game-start needs kind");
                games.Start(kind, r.Args.ContainsKey("seconds") ? Arg(r, "seconds") : null);
                return new PipeReply(true, games.Describe());
            });
            pipe.Register("game-click", r => { games.Click(new Vector2(Arg(r, "x"), Arg(r, "y"))); return new PipeReply(true, games.Describe()); });
            pipe.Register("game-flick", r =>
            {
                games.Flick(new Vector2(Arg(r, "x1"), Arg(r, "y1")), new Vector2(Arg(r, "x2"), Arg(r, "y2")));
                return new PipeReply(true, games.Describe());
            });
            pipe.Register("game-stop", _ => { games.Stop(); return new PipeReply(true, games.Describe()); });
            pipe.Register("debug-game", _ => new PipeReply(true, games.Describe()));
            pipe.Register("hello-new", _ => { StartHello(); return new PipeReply(true); });
            pipe.Register("tray-pick", r =>
            {
                if (r.Args?.TryGetValue("item", out var item) != true || item is null || !tray.Pick(item)) return new PipeReply(false, "unknown tray item");
                return new PipeReply(true);
            });
            pipe.Register("debug-music", _ => new PipeReply(true, music.Describe()));
            pipe.Register("update-check", r =>
            {
                if (!updates.Reachable) return new PipeReply(false, "no --update-host: test mode never reaches GitHub");
                updates.Check(manual: r.Args?.TryGetValue("manual", out var m) == true && m == "true");
                return new PipeReply(true, updates.Describe());
            });
            pipe.Register("update-answer", r =>
            {
                string? answer = r.Args?.TryGetValue("answer", out var a) == true ? a : null;
                if (answer is not ("yes" or "later")) return new PipeReply(false, "update-answer needs answer yes or later");
                listBubble.Close();
                if (answer == "yes") updates.Install(); else updates.Later();
                return new PipeReply(true, updates.Describe());
            });
            pipe.Register("debug-update", _ => new PipeReply(true, updates.Describe()));
            pipe.Register("debug-tray-icon", _ => new PipeReply(true, $"resource={(tray.IconFromResource ? "true" : "false")}"));
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
            pipe.Register("typealong-next", _ => { lifeDirector.Enabled = true; lifeDirector.ForceTypeAlong(); return new PipeReply(true); });
            pipe.Register("set-activity", r =>
            {
                if (r.Args is null || !r.Args.TryGetValue("level", out var name) || !Enum.TryParse<ActivityLevel>(name, out var level) || !Enum.IsDefined(level))
                    return new PipeReply(false, "set-activity needs level Calm, Medium or Lively");
                SetActivity(level);
                return new PipeReply(true, lifeDirector.Describe());
            });
            pipe.Register("debug-foreground", _ => new PipeReply(false, FullscreenWatcher.Describe()));
            pipe.Register("debug-presence", _ => new PipeReply(true, idle.Describe()));
            // The soul (0.10a): a hop on demand, its state, and a frozen pose for pixel checks.
            pipe.Register("soul-hop", r =>
            {
                float power = r.Args?.TryGetValue("power", out var p) == true && float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pv) ? pv : 1f;
                bool hopped = soul.Hop(power);
                return new PipeReply(true, $"hopped={(hopped ? "true" : "false")} " + soul.Describe());
            });
            pipe.Register("debug-soul", _ => new PipeReply(true, soul.Describe()));
            pipe.Register("soul-idle", r =>
            {
                if (r.Args?.TryGetValue("gap", out _) == true) soulDirector.GapOverride = TimeSpan.FromSeconds(Arg(r, "gap"));
                if (r.Args?.TryGetValue("on", out var on) == true) soulDirector.Enabled = on == "true";
                return new PipeReply(true, soulDirector.Describe());
            });
            pipe.Register("debug-reduced-motion", r =>
            {
                SetReduced(r.Args?.TryGetValue("on", out var on) == true && on == "true");
                return new PipeReply(true, soul.Describe());
            });
            pipe.Register("debug-soul-freeze", r =>
            {
                static double? Num(PipeRequest r, string k) => r.Args?.TryGetValue(k, out var v) == true
                    && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
                soul.Freeze(Num(r, "floatT"), Num(r, "hopT"), (float)(Num(r, "power") ?? 1));
                return new PipeReply(true, soul.Describe());
            });
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
        hotkey.Unregister();
        Log.Write("message loop ended");
        Foreground.Stop();
        Native.DestroyWindow(input.Hwnd);
        Native.DestroyWindow(overlay.Hwnd);
        Native.DestroyWindow(spareOverlay.Hwnd);
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
        catch (Exception e) { Log.Write($"error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
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
