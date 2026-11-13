using System.Diagnostics;
using System.Globalization;
using Sahar.Core.Brain;
using Sahar.Core.Geometry;
using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Windows.System;
using P = Sahar.Core.Prayer.Prayer;

namespace Sahar.Engine.Tools;

/// <summary>
/// 🕌 أوقات الصلاة (spec §7.1): the next prayer and today's times for the city you typed (computed on the PC,
/// in the PC's time zone), a reminder before each prayer (face 19, her drawing your eye, a line of words),
/// and quiet for the minutes after its time. The city never reaches the log (spec §14).
/// </summary>
internal sealed class PrayerTool : IDisposable
{
    private static readonly P[] Shown = [P.Fajr, P.Sunrise, P.Dhuhr, P.Asr, P.Maghrib, P.Isha];

    private readonly ListBubble _list;
    private readonly TextBubble _bubble;
    private readonly Func<RectF> _body;
    private readonly Func<string, Task<(GeoResult? Place, string? Error)>> _find;
    private readonly Director _director;
    private readonly Action<string> _say;
    private readonly Func<SaharSettings> _get;
    private readonly Action<Func<SaharSettings, SaharSettings>> _set;   // load → change → save (two writers)
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _timer;
    private readonly bool _testMode;
    private PrayerSchedule? _schedule;
    private DateTimeOffset? _fakeBase;
    private readonly Stopwatch _fakeClock = new();

    public PrayerTool(DispatcherQueue ui, ListBubble list, TextBubble bubble, Func<RectF> body,
        Func<string, Task<(GeoResult? Place, string? Error)>> find, Director director, Action<string> say,
        Func<SaharSettings> get, Action<Func<SaharSettings, SaharSettings>> set, bool testMode)
    {
        (_ui, _list, _bubble, _body, _find, _director, _say, _get, _set, _testMode) =
            (ui, list, bubble, body, find, director, say, get, set, testMode);
        Rebuild();
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(testMode ? 1 : 20);
        _timer.Tick += (_, _) => Safe(Check);
        _timer.Start();
    }

    public bool On => _schedule is not null;

    /// <summary>The next prayer and how long until it (null without a city), from its schedule and clock.</summary>
    public (Sahar.Core.Prayer.Prayer Prayer, TimeSpan Left)? Next()
    {
        if (_schedule is null) return null;
        var now = Now;
        return _schedule.Next(now) is { } n ? (n.Prayer, n.At - now) : null;
    }

    /// <summary>Real time, or (test mode) the clock a test set, moving on from there.</summary>
    private DateTimeOffset Now => _fakeBase is { } b ? b + _fakeClock.Elapsed : DateTimeOffset.Now;

    private static TimeSpan Offset(DateTimeOffset at) => TimeZoneInfo.Local.GetUtcOffset(at);

    private void Rebuild()
    {
        var s = _get();
        _schedule = s.PrayerLatitude is { } lat && s.PrayerLongitude is { } lon
            ? new PrayerSchedule(lat, lon, s.PrayerMethod, s.PrayerHanafiAsr, Offset)
            : null;
    }

    public void Open()
    {
        var s = _get();
        if (_schedule is null || s.PrayerCity is null) { AskCity("شو مدينتك؟ (لأوقات الصلاة)"); return; }
        var now = Now.ToOffset(Offset(Now));
        var day = _schedule.Day(DateOnly.FromDateTime(now.Date), now);
        var lines = new List<string>();
        lines.Add(_schedule.Next(now) is { } next ? PrayerText.NextLine(next.Prayer, next.At - now) : "");
        lines.AddRange(Shown.Select(p => PrayerText.TimeLine(p, day[p].ToOffset(Offset(day[p])))));
        lines.Add(PrayerText.MethodLine(s.PrayerMethod));
        _list.Show(_body(), "🕌 أوقات الصلاة", lines, PrayerText.CityLine(s.PrayerCity),
            i => { if (i == lines.Count - 1) _ui.TryEnqueue(() => Safe(OpenMethods)); },
            () => _ui.TryEnqueue(() => Safe(() => AskCity("شو مدينتك؟ (لأوقات الصلاة)"))));
    }

    private void OpenMethods()
    {
        var methods = Enum.GetValues<PrayerMethod>();
        var current = _get().PrayerMethod;
        _list.Show(_body(), "طريقة الحساب", methods.Select(m => (m == current ? "✓ " : "") + PrayerNames.Method(m)).ToList(), "رجوع",
            i =>
            {
                if (i < 0 || i >= methods.Length) return;
                _set(s => s with { PrayerMethod = methods[i] });
                Log.Write($"prayer: method {methods[i]}");
                Rebuild();
                _ui.TryEnqueue(() => Safe(Open));
            },
            () => _ui.TryEnqueue(() => Safe(Open)));
    }

    private void AskCity(string title) => _bubble.Show(_body(), title, [], city => Safe(() => SetCity(city)));

    private void SetCity(string city) => _find(city).ContinueWith(t =>
    {
        var (place, error) = t.IsCompletedSuccessfully ? t.Result : (null, "ما في نت هلق، جرّب بعدين");
        _ui.TryEnqueue(() => Safe(() =>
        {
            if (place is null) { AskCity($"{error} — جرّب مدينة تانية"); return; }
            _set(s => s with { PrayerCity = place.Name ?? city.Trim(), PrayerLatitude = place.Latitude, PrayerLongitude = place.Longitude });
            Log.Write("prayer: city set");   // never which city (spec §14)
            Rebuild();
            Open();
        }));
    });

    /// <summary>A reminder before each prayer; at its time, quiet for the adhan and the reminder ends.</summary>
    public void Check()
    {
        // Test machines wait for a test's clock, so a real prayer time during a run never disturbs other stages.
        if (_schedule is null || (_testMode && _fakeBase is null)) { _director.SetAdhan(false); return; }
        var now = Now;
        int before = Math.Clamp(_get().PrayerReminderMinutes, 1, 60);
        // Switched off in the settings (0.12): the reminder is still taken, so turning it on never fires a late one.
        if (_schedule.TakeReminder(now, before, out var p) && _get().PrayerReminderOn && _schedule.Next(now) is { } next)
        {
            Log.Write($"prayer: reminder for {p}");
            // Real time for the brain: the face stays until the prayer's time (or a poke).
            _director.Post(EventKind.Reminder, next.At - now);
            if (!_director.Quiet) _say(PrayerText.Reminder(p, (int)Math.Ceiling((next.At - now).TotalMinutes - 1e-9)));
        }
        if (_schedule.TakeAdhan(now, out var a))
        {
            Log.Write($"prayer: adhan quiet for {a}");
            _director.End(EventKind.Reminder);
        }
        bool quiet = _schedule.InAdhanQuiet(now);
        if (quiet != _director.Adhan) Log.Write($"prayer: quiet {(quiet ? "on" : "off")}");
        _director.SetAdhan(quiet);
    }

    /// <summary>The settings changed (city, method, Asr, reminder): the schedule is rebuilt from them.</summary>
    public void Reload()
    {
        Rebuild();
        Check();
    }

    /// <summary>Test mode: the prayer clock jumps to <paramref name="at"/> and moves on from there.</summary>
    public void SetClock(DateTimeOffset at)
    {
        _fakeBase = at;
        _fakeClock.Restart();
        Check();
    }

    public string Describe()
    {
        if (_schedule is null) return "on=false quiet=false";
        var now = Now;
        var day = _schedule.Day(DateOnly.FromDateTime(now.ToOffset(Offset(now)).Date), now);
        var next = _schedule.Next(now);
        string Iso(DateTimeOffset t) => t.ToString("o", CultureInfo.InvariantCulture);
        return $"on=true next={next?.Prayer} nextAt={(next is { } n ? Iso(n.At) : "")} " +
            $"minutes={(next is { } m ? (int)Math.Ceiling((m.At - now).TotalMinutes) : 0)} quiet={(_director.Adhan ? "true" : "false")} " +
            string.Join(' ', Shown.Select(p => $"{p.ToString().ToLowerInvariant()}={Iso(day[p])}"));
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"prayer error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
    }

    public void Dispose() => _timer.Stop();
}
