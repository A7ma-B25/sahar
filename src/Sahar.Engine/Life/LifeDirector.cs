using System.Globalization;
using System.Numerics;
using Sahar.Core.Brain;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Life;
using Sahar.Core.Motion;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Motion;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Life;

/// <summary>
/// Her daily life. Once a second it feeds the routine, typing and media senses; every so often (by activity
/// level, calmer in the hours you usually work) it picks something to do; it puts her to sleep while you are
/// away, and learns from pokes (liked) and grabs (disliked). Learned state lives in life.json.
/// </summary>
internal sealed unsafe class LifeDirector : IDisposable
{
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(10);

    private readonly MotionController _motion;
    private readonly Director _director;
    private readonly Media _media;
    private readonly CursorRest _cursorRest = new();
    private readonly ShyDetector _shy = new();
    private readonly AttentionPacer _attention = new();
    private bool _cursorResting;
    private string? _waiting;
    private readonly DispatcherQueueTimer _timer;
    private readonly Preferences _prefs;
    private readonly Routine _routine;
    private readonly TypingDetector _typing = new();
    private readonly Random _rng = new();
    private readonly string _path;

    private DateTimeOffset _due;
    private readonly ActionMemory _memory = new();
    private readonly Func<TimeSpan> _sinceKeyboard;
    private readonly Action<Eyes, Mouth, float> _face;
    private readonly Func<string> _expressionKey;
    private readonly Action _poke;
    private LifeAction _lastAction = LifeAction.None;
    private LifeAction? _forceNext;
    private bool _sleepingForAway, _ownSleep;
    private DateTimeOffset _lastSave = DateTimeOffset.Now;
    private DateOnly _lastDecay;
    private ActivityLevel _level;

    public bool Enabled { get; set; } = true;
    /// <summary>Are you away (from real input, never from her face)? While away she does nothing by herself.</summary>
    public Func<bool> Away { get; set; } = () => false;
    public int Actions { get; private set; }
    public bool Typing { get; private set; }
    private TimeSpan? _gapOverride;

    /// <summary>Test mode: a fixed gap; takes effect at once (the queued turn is rescheduled).</summary>
    public TimeSpan? GapOverride
    {
        get => _gapOverride;
        set { _gapOverride = value; Schedule(DateTimeOffset.Now); }
    }
    public event Action<bool>? TypingChanged;

    public LifeDirector(DispatcherQueue ui, MotionController motion, Director director, Media media, ActivityLevel level, string path,
        Func<TimeSpan> sinceKeyboard, Action<Eyes, Mouth, float> face, Func<string> expressionKey, Action squish)
    {
        _sinceKeyboard = sinceKeyboard;
        _face = face;
        _expressionKey = expressionKey;
        _poke = squish;
        _motion = motion;
        _director = director;
        _media = media;
        _level = level;
        _path = path;
        var state = LifeStore.Load(path);
        _prefs = Preferences.FromState(state.Weights);
        _routine = Routine.FromState(state.Hours);
        // Preferences fade a step a day, including the days she was not running.
        _lastDecay = DateOnly.FromDateTime(DateTime.Now);
        if (DateOnly.TryParseExact(state.LastDecay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last))
            _prefs.DecayDays(_lastDecay.DayNumber - last.DayNumber);
        Schedule(DateTimeOffset.Now);
        motion.DragStarted += OnDragStarted;
        motion.PointerNear += distanceDip =>
        {
            if (!Enabled || Paused || _motion.Current is not (MotionKind.Hover or MotionKind.EyesFollow)) return;
            if (!_shy.Update(DateTimeOffset.Now, distanceDip, clicked: false)) return;
            Log.Write("life: Shy (the pointer kept coming close)");
            _motion.Play(MotionKind.Shy);
        };
        motion.ExtraAvoid = () => Typing ? TypingAvoid() : [];
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            try { Tick(DateTimeOffset.Now); }
            catch (Exception e) { Log.Write($"life error: {e}"); }
        };
        _timer.Start();
    }

    /// <summary>"Do not disturb" is on: only small things (like a video in front).</summary>
    public Func<bool> DndOn { get; set; } = () => false;
    /// <summary>The camera or microphone is in use: only small things too.</summary>
    public Func<bool> CameraMicOn { get; set; } = () => false;
    /// <summary>The minutes after a prayer's time: small things only, and no drawing your eye.</summary>
    public Func<bool> AdhanOn { get; set; } = () => false;
    /// <summary>The Quran is playing: she stays still (small things only, no dance, no drawing your eye).</summary>
    public Func<bool> QuranOn { get; set; } = () => false;

    /// <summary>Her menu is open (or a show is running): no own actions.</summary>
    public bool Paused { get; set; }
    /// <summary>She is sulking after an angry shake: no moves of her own until it passes.</summary>
    public Func<bool> Sulking { get; set; } = () => false;
    private DateTimeOffset? _lastTypeAlong;
    private bool _forceTypeAlong;

    /// <summary>False on a test machine: typing tests must not get a random type-along (only a forced one).</summary>
    public bool RandomTypeAlong { get; set; } = true;

    /// <summary>Test mode: the next time you start typing she types along.</summary>
    public void ForceTypeAlong() => _forceTypeAlong = true;

    public ActivityLevel Level
    {
        get => _level;
        set { _level = value; Schedule(DateTimeOffset.Now); }
    }

    /// <summary>Test mode: act at the next tick (a given action, or her own choice).</summary>
    public void ActNow(LifeAction? action)
    {
        _forceNext = action ?? LifeAction.None;
        _due = DateTimeOffset.Now;
    }

    private bool Resting => _motion.Current is MotionKind.Hover or MotionKind.EyesFollow or MotionKind.LandAndSit or MotionKind.PerchWindow;

    private void Tick(DateTimeOffset now)
    {
        // Senses.
        var info = new LASTINPUTINFO { CbSize = (uint)sizeof(LASTINPUTINFO) };
        var sinceInput = Native.GetLastInputInfo(&info) != 0
            ? TimeSpan.FromMilliseconds(unchecked(Native.GetTickCount() - info.DwTime))
            : TimeSpan.MaxValue;
        _routine.Tick(now, sinceInput < TimeSpan.FromMinutes(1));
        bool typing = _typing.Update(now, _sinceKeyboard());
        if (typing != Typing)
        {
            Typing = typing;
            TypingChanged?.Invoke(typing);
            // "بتكتب معك": sometimes, when you start typing, she sits beside your window and taps along.
            if (typing && Enabled && !Paused && !Sulking() && Resting && !_director.Hidden && !_director.Quiet &&
                (_forceTypeAlong || (RandomTypeAlong && TypeAlongPolicy.Should(now, _lastTypeAlong, _rng.NextDouble()))))
            {
                _forceTypeAlong = false;
                if (_motion.TypeAlong(() => Typing))
                {
                    _lastTypeAlong = now;
                    Log.Write("life: TypeAlong");
                }
            }
        }
        // Her routine follows life: a test machine's own clock (CI runs at night too) never posts it into other stages.
        if (Enabled && _routine.TakeBreakDue()) _director.Post(EventKind.Break);
        if (Enabled && _routine.TakeLateNightDue()) _director.Post(EventKind.LateNight);
        Housekeeping(now);
        // Something needs you (Claude asks, the timer is done, a reminder): she draws your eye, paced.
        if (!_director.Hidden && !Paused && _motion.Current is not (MotionKind.Drag or MotionKind.Throw or MotionKind.Goodbye or MotionKind.Game)
            && _attention.ShouldPlay(_director.CurrentKey, now, DndOn() || AdhanOn() || QuranOn()))
        {
            Log.Write($"life: Attention for '{_director.CurrentKey}'");
            _motion.Play(MotionKind.Attention);
        }
        if (!Enabled || Paused || Sulking()) return;
        var cursor = Native.CursorPos();
        var body = _motion.Body;
        _cursorResting = _cursorRest.Update(now, new Vector2(cursor.X, cursor.Y),
            new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2), _motion.DpiScale);
        if (_motion.Current == MotionKind.Dance && (!_media.MusicPlaying || _media.VideoPlaying || DndOn() || CameraMicOn() || AdhanOn() || QuranOn()))
        {
            Log.Write("life: the music stopped (or a video is in front), dance ends");
            _motion.Play(MotionKind.Hover);
        }
        if (_motion.PerchIsStale()) { _motion.Play(MotionKind.Wander); return; }   // her window moved, resized or closed
        if (Typing) { Waiting("you are typing"); StepAwayFromTyping(); return; }   // no own actions while you type

        // Away: she naps in the corner and wakes when you are back.
        bool away = Away();
        if (away && !_sleepingForAway && Resting && !_director.Hidden)
        {
            _sleepingForAway = true;
            _motion.Play(_rng.Next(2) == 0 ? MotionKind.SleepInCorner : MotionKind.NapOnIcon);   // corner, or her favourite icon
            return;
        }
        if (!away && _sleepingForAway)
        {
            _sleepingForAway = false;
            if (_motion.Current is MotionKind.SleepInCorner or MotionKind.NapOnIcon) _motion.Play(MotionKind.Wake);
            Schedule(now);
            return;
        }
        if (_sleepingForAway) { Waiting("you are away"); return; }
        if (now < _due) { Waiting($"next turn at {_due.ToLocalTime():HH:mm:ss}"); return; }

        // A late-night nap she chose herself ends at her next turn.
        if (_ownSleep && _motion.Current == MotionKind.SleepInCorner)
        {
            _ownSleep = false;
            _motion.Play(MotionKind.Wake);
            Schedule(now);
            return;
        }

        var forced = _forceNext;
        _forceNext = null;
        var context = new LifeContext(Typing, _media.VideoPlaying || DndOn() || CameraMicOn() || AdhanOn() || QuranOn(), _media.MusicPlaying, away, _director.Hidden, !Resting,
            now.Hour, ForegroundInfo.IsPerchable(Native.GetForegroundWindow()), _cursorResting);
        var action = forced is { } f && f != LifeAction.None && !_director.Hidden
            ? f
            : LifeChooser.Choose(context, _prefs.Weight, _rng);
        if (action == LifeAction.None)   // not now: try again next second, same gap
        {
            Waiting(_director.Hidden ? "hidden" : !Resting ? $"busy ({_motion.Current})" : "nothing fits now");
            return;
        }

        switch (action)
        {
            case LifeAction.Flight:
                // A pattern that fits where she is (and doesn't end under the pointer); none fits: a wander.
                FlightPattern? flown = null;
                foreach (var p in Enum.GetValues<FlightPattern>().OrderBy(_ => _rng.Next()))
                    if (_motion.PlayFlight(p)) { flown = p; break; }
                if (flown is { } fp) Log.Write($"life: Flight {fp}{Why(context)}");
                else
                {
                    Log.Write($"life: Wander (no flight fits here){Why(context)}");
                    action = LifeAction.Wander;
                    _motion.Play(MotionKind.Wander);
                }
                break;
            case LifeAction.Moment:
                if (PlayMoment() is { } moment) { Log.Write($"life: Moment {moment}{Why(context)}"); break; }
                // An event face is showing: her face stays; a slow breath instead of nothing.
                Log.Write($"life: no Moment over the '{_expressionKey()}' face, Breathe instead");
                action = LifeAction.Breathe;
                _motion.Play(MotionKind.Breathe);
                break;
            default:
                Log.Write($"life: {action}{Why(context)}");
                _motion.Play(action switch
                {
                    LifeAction.Wander => MotionKind.Wander,
                    LifeAction.PeekTaskbar => MotionKind.PeekTaskbar,
                    LifeAction.PerchWindow => MotionKind.PerchWindow,
                    LifeAction.CelebrateLoop => MotionKind.CelebrateLoop,
                    LifeAction.PlayWithMoon => MotionKind.PlayWithMoon,
                    LifeAction.Curious => MotionKind.Curious,
                    LifeAction.Dance => MotionKind.Dance,
                    LifeAction.Breathe => MotionKind.Breathe,
                    LifeAction.OrbitCursor => MotionKind.OrbitCursor,
                    LifeAction.FollowCursor => MotionKind.FollowCursor,
                    LifeAction.HideBehindWindow => MotionKind.HideBehindWindow,
                    LifeAction.HopIcons => MotionKind.HopIcons,
                    LifeAction.WalkTaskbar => MotionKind.WalkTaskbar,
                    LifeAction.Stretch => MotionKind.Stretch,
                    LifeAction.Yawn => MotionKind.Yawn,
                    LifeAction.ScratchHead => MotionKind.ScratchHead,
                    LifeAction.WatchYou => MotionKind.WatchYou,
                    LifeAction.HopWindows => MotionKind.HopWindows,
                    LifeAction.MoonToss => MotionKind.MoonToss,
                    LifeAction.ChaseButterfly => MotionKind.ChaseButterfly,
                    _ => MotionKind.SleepInCorner,
                });
                break;
        }
        _waiting = null;
        _ownSleep = action == LifeAction.SleepInCorner;
        _memory.Started(action, now);
        _lastAction = action;
        Actions++;
        Schedule(now);
    }

    /// <summary>A small face moment; only while her face is her own (never over an event's face).</summary>
    private MomentKind? PlayMoment()
    {
        // Her own face is idle, or music's headphones (sticky while something plays); never an event's face.
        if (_director.CurrentKey is not ("idle" or "music") || _expressionKey() is not ("idle" or "music")) return null;
        var kind = Enum.GetValues<MomentKind>()[_rng.Next(Enum.GetValues<MomentKind>().Length)];
        var (eyes, mouth, seconds) = Moments.Face(kind);
        _face(eyes, mouth, seconds);
        if (kind == MomentKind.Giggle) _poke();
        return kind;
    }

    /// <summary>Logs why she is not acting, once per change of reason (a new "next turn at" time is not a new reason).</summary>
    private void Waiting(string reason)
    {
        var kind = Sahar.Core.Diagnostics.LogRules.WaitKind(reason);
        if (kind == _waiting) return;
        _waiting = kind;
        Log.Write($"life: waiting: {reason}");
    }

    private static string Why(LifeContext c) =>
        (c.MusicPlaying ? " (music)" : "") + (c.VideoPlaying ? " (video in front)" : "") + (c.CursorResting ? " (pointer resting)" : "");

    public const float NearTypingDip = 120f, AvoidTypingDip = 150f;

    /// <summary>Where you are typing: the caret, or the mouse pointer in apps without a system caret.</summary>
    private static RectF TypingSpot(out RectI window) => TypingSpot(out window, out _);

    private static RectF TypingSpot(out RectI window, out bool fromCaret)
    {
        var (hwnd, w, caret, _) = ForegroundInfo.Current();
        window = w;
        fromCaret = hwnd != 0 && caret is not null;
        if (caret is { } c && fromCaret) return new RectF(c.Left, c.Top, Math.Max(1, c.Right - c.Left), Math.Max(1, c.Bottom - c.Top));
        var p = Native.CursorPos();
        return new RectF(p.X, p.Y, 1, 20);
    }

    private IReadOnlyList<RectF> TypingAvoid()
    {
        var spot = TypingSpot(out var w);
        float m = AvoidTypingDip * _motion.DpiScale;
        var areas = new List<RectF> { new(spot.X - m, spot.Y - m, spot.Width + 2 * m, spot.Height + 2 * m) };
        if (w.Right > w.Left) areas.Add(new RectF(w.Left, w.Top, w.Right - w.Left, (w.Bottom - w.Top) / 4f));   // title and toolbar
        return areas;
    }

    /// <summary>If she is close to where you are typing, she drifts somewhere else (not counted as an own action).</summary>
    private void StepAwayFromTyping()
    {
        if (_director.Hidden || !Resting) return;
        var spot = TypingSpot(out _, out bool fromCaret);
        var body = _motion.Body;
        // Without a caret the pointer stands in for the typing spot, unless the pointer is on her: then you are
        // playing with her, not typing next to her.
        float pad = 40 * _motion.DpiScale;
        if (!fromCaret && spot.X >= body.X - pad && spot.X <= body.X + body.Width + pad &&
            spot.Y >= body.Y - pad - body.Height / 2 && spot.Y <= body.Y + body.Height + pad) return;
        var d = Vector2.Distance(new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2),
            new Vector2(spot.X + spot.Width / 2, spot.Y + spot.Height / 2));
        if (d < NearTypingDip * _motion.DpiScale + body.Width / 2) _motion.Play(MotionKind.Wander);
    }

    /// <summary>A poke shortly after one of her own actions: she learns you liked it.</summary>
    public void OnPoked()
    {
        _shy.Update(DateTimeOffset.Now, 0, clicked: true);
        if (_memory.TakeForFeedback(DateTimeOffset.Now) is { } a) _prefs.Liked(a);   // once per action
    }

    private void OnDragStarted()
    {
        var now = DateTimeOffset.Now;
        if (_memory.TakeForFeedback(now) is { } a) _prefs.Disliked(a);
        Schedule(now);   // being grabbed restarts the wait
    }

    private void Schedule(DateTimeOffset now) =>
        _due = now + (_gapOverride ?? LifeTiming.NextGap(_level, _rng, _routine.BusyFactor(now.Hour)));

    private void Housekeeping(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        if (today != _lastDecay) { _lastDecay = today; _prefs.Decay(); }
        if (now - _lastSave >= SaveEvery) Save();
    }

    public void Save()
    {
        _lastSave = DateTimeOffset.Now;
        try { LifeStore.Save(_path, new LifeState(1, _prefs.ToState(), _routine.ProfileToState(), _lastDecay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))); }
        catch (Exception e) { Log.Write($"life.json not saved: {e.Message}"); }
    }

    /// <summary>Test-mode diagnostics: space-separated key=value pairs.</summary>
    public string Describe()
    {
        var next = Math.Max(0, (_due - DateTimeOffset.Now).TotalSeconds);
        var weights = string.Join(",", Enum.GetValues<LifeAction>().Where(a => a != LifeAction.None)
            .Select(a => string.Create(CultureInfo.InvariantCulture, $"{a}:{_prefs.Weight(a):0.00}")));
        return string.Create(CultureInfo.InvariantCulture,
            $"level={_level} enabled={(Enabled ? "true" : "false")} next={next:0.0} last={_lastAction} " +
            $"typing={(Typing ? "true" : "false")} quiet={(_director.Quiet ? "true" : "false")} video={(_media.VideoPlaying ? "true" : "false")} actions={Actions} weights={weights} spot={SpotText()}");
    }

    private static string SpotText()
    {
        var s = TypingSpot(out _);
        return string.Create(CultureInfo.InvariantCulture, $"{s.X + s.Width / 2:0},{s.Y + s.Height / 2:0}");
    }

    public void Dispose()
    {
        _timer.Stop();
        Save();
    }
}
