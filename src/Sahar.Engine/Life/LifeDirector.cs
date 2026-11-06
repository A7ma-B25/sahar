using System.Globalization;
using Sahar.Core.Brain;
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
    public static readonly TimeSpan LearnWindow = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(10);

    private readonly MotionController _motion;
    private readonly Director _director;
    private readonly Media _media;
    private readonly DispatcherQueueTimer _timer;
    private readonly Preferences _prefs;
    private readonly Routine _routine;
    private readonly TypingDetector _typing = new();
    private readonly Random _rng = new();
    private readonly string _path;

    private DateTimeOffset _due;
    private (LifeAction Action, DateTimeOffset At)? _last;
    private LifeAction? _forceNext;
    private bool _sleepingForAway, _ownSleep;
    private POINT _cursor;
    private DateTimeOffset _lastSave = DateTimeOffset.Now;
    private DateOnly _lastDecay = DateOnly.FromDateTime(DateTime.Now);
    private ActivityLevel _level;

    public bool Enabled { get; set; } = true;
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

    public LifeDirector(DispatcherQueue ui, MotionController motion, Director director, Media media, ActivityLevel level, string path)
    {
        _motion = motion;
        _director = director;
        _media = media;
        _level = level;
        _path = path;
        var state = LifeStore.Load(path);
        _prefs = Preferences.FromState(state.Weights);
        _routine = Routine.FromState(state.Hours);
        _cursor = Native.CursorPos();
        Schedule(DateTimeOffset.Now);
        motion.DragStarted += OnDragStarted;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            try { Tick(DateTimeOffset.Now); }
            catch (Exception e) { Log.Write($"life error: {e}"); }
        };
        _timer.Start();
    }

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
        var cursor = Native.CursorPos();
        bool moved = cursor.X != _cursor.X || cursor.Y != _cursor.Y;
        _cursor = cursor;
        _routine.Tick(now, sinceInput < TimeSpan.FromMinutes(1));
        bool typing = _typing.Update(now, sinceInput, moved);
        if (typing != Typing) { Typing = typing; TypingChanged?.Invoke(typing); }
        if (_routine.TakeBreakDue()) _director.Post(EventKind.Break);
        if (_routine.TakeLateNightDue()) _director.Post(EventKind.LateNight);
        Housekeeping(now);
        if (!Enabled) return;

        // Away: she naps in the corner and wakes when you are back.
        bool away = _director.CurrentKey == "sleep";
        if (away && !_sleepingForAway && Resting && !_director.Hidden)
        {
            _sleepingForAway = true;
            _motion.Play(MotionKind.SleepInCorner);
            return;
        }
        if (!away && _sleepingForAway)
        {
            _sleepingForAway = false;
            if (_motion.Current == MotionKind.SleepInCorner) _motion.Play(MotionKind.Wake);
            Schedule(now);
            return;
        }
        if (_sleepingForAway || now < _due) return;

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
        var context = new LifeContext(Typing, _media.VideoPlaying, _media.MusicPlaying, away, _director.Hidden, !Resting,
            now.Hour, ForegroundInfo.IsPerchable(Native.GetForegroundWindow()));
        var action = forced is { } f && f != LifeAction.None && !_director.Hidden
            ? f
            : LifeChooser.Choose(context, _prefs.Weight, _rng);
        if (action == LifeAction.None) return;   // not now: try again next second, same gap

        _motion.Play(action switch
        {
            LifeAction.Wander => MotionKind.Wander,
            LifeAction.PeekTaskbar => MotionKind.PeekTaskbar,
            LifeAction.PerchWindow => MotionKind.PerchWindow,
            LifeAction.CelebrateLoop => MotionKind.CelebrateLoop,
            _ => MotionKind.SleepInCorner,
        });
        _ownSleep = action == LifeAction.SleepInCorner;
        _last = (action, now);
        Actions++;
        Schedule(now);
    }

    /// <summary>A poke shortly after one of her own actions: she learns you liked it.</summary>
    public void OnPoked()
    {
        if (_last is { } l && DateTimeOffset.Now - l.At <= LearnWindow) _prefs.Liked(l.Action);
    }

    private void OnDragStarted()
    {
        var now = DateTimeOffset.Now;
        if (_last is { } l && now - l.At <= LearnWindow)
        {
            _prefs.Disliked(l.Action);
            _last = null;
        }
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
        try { LifeStore.Save(_path, new LifeState(1, _prefs.ToState(), _routine.ProfileToState())); }
        catch (Exception e) { Log.Write($"life.json not saved: {e.Message}"); }
    }

    /// <summary>Test-mode diagnostics: space-separated key=value pairs.</summary>
    public string Describe()
    {
        var next = Math.Max(0, (_due - DateTimeOffset.Now).TotalSeconds);
        var weights = string.Join(",", Enum.GetValues<LifeAction>().Where(a => a != LifeAction.None)
            .Select(a => string.Create(CultureInfo.InvariantCulture, $"{a}:{_prefs.Weight(a):0.00}")));
        return string.Create(CultureInfo.InvariantCulture,
            $"level={_level} enabled={(Enabled ? "true" : "false")} next={next:0.0} last={_last?.Action.ToString() ?? "None"} " +
            $"typing={(Typing ? "true" : "false")} video={(_media.VideoPlaying ? "true" : "false")} actions={Actions} weights={weights}");
    }

    public void Dispose()
    {
        _timer.Stop();
        Save();
    }
}
