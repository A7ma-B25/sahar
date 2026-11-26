using System.Diagnostics;
using System.Globalization;
using Sahar.Core.Brain;
using Sahar.Core.Geometry;
using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Sahar.Engine.Surfaces;
using Windows.System;
using P = Sahar.Core.Prayer.Prayer;

namespace Sahar.Engine.Tools;

/// <summary>
/// أوقات الصلاة (spec §7.1): the next prayer and today's times for the city you typed (computed on the PC,
/// in the PC's time zone), a reminder before each prayer (face 19, her drawing your eye, a line of words),
/// and quiet for the minutes after its time. The city never reaches the log (spec §14).
/// </summary>
internal sealed class PrayerTool : IDisposable
{
    private static readonly P[] Shown = [P.Fajr, P.Sunrise, P.Dhuhr, P.Asr, P.Maghrib, P.Isha];

    private readonly Panel _panel;
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

    public PrayerTool(DispatcherQueue ui, Panel panel, Func<RectF> body,
        Func<string, Task<(GeoResult? Place, string? Error)>> find, Director director, Action<string> say,
        Func<SaharSettings> get, Action<Func<SaharSettings, SaharSettings>> set, bool testMode)
    {
        (_ui, _panel, _body, _find, _director, _say, _get, _set, _testMode) =
            (ui, panel, body, find, director, say, get, set, testMode);
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

    private const string Why = "لأعرف أوقات الصلاة عندك.";

    /// <summary>«أوقات الصلاة» (V15Lists): today's six times, the next one soft with «بعد ٤٢ د», past ones dim; the city
    /// chip and the method link are drills in the same panel.</summary>
    public void Open()
    {
        var s = _get();
        if (_schedule is null || s.PrayerCity is null) { AskCity(); return; }
        _panel.Show(TimesSpec(), OnPanel);
    }

    private PanelSpec TimesSpec()
    {
        var s = _get();
        var now = Now.ToOffset(Offset(Now));
        var day = _schedule!.Day(DateOnly.FromDateTime(now.Date), now);
        var next = _schedule.Next(now);
        string Clock(DateTimeOffset t)
        {
            var local = t.ToOffset(Offset(t));
            int h = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
            return Sahar.Core.Text.Digits.ToArabicIndic($"{h}:{local.Minute:00}");
        }
        var rows = Shown.Select(p =>
        {
            // After العشا the next one is tomorrow's الفجر: it is still the soft row, with tomorrow's time.
            bool isNext = next is { } n && n.Prayer == p;
            string meta = isNext ? Sahar.Core.Text.Digits.ToArabicIndic($"بعد {Short(next!.Value.At - now)}   ") + Clock(next.Value.At) : Clock(day[p]);
            return new PickRow(p.ToString(), PrayerNames.Arabic(p)) { Meta = meta, Sel = isNext, Dim = !isNext && day[p] < now };
        }).ToList();
        return new PanelSpec
        {
            Kind = "list", Name = "prayer", Title = "أوقات الصلاة", Sub = s.PrayerCity + " · غيّر", SubAction = "city", SubIcon = "pin",
            Rows = rows, ListHeight = 6 * 43, FootLink = new PickAction("method", PrayerText.MethodLine(s.PrayerMethod), "calc"),
        };
    }

    private static string Short(TimeSpan left)
    {
        int m = (int)Math.Ceiling(left.TotalMinutes - 1e-9);
        return m < 60 ? $"{Math.Max(1, m)} د" : m % 60 == 0 ? $"{m / 60} س" : $"{m / 60} س و{m % 60} د";
    }

    private PanelSpec MethodSpec()
    {
        var current = _get().PrayerMethod;
        return new PanelSpec
        {
            Kind = "list", Name = "method", Title = "طريقة الحساب", Back = true, Sub = Address.ToYou("كل بلد إلها طريقة، اختار الأقرب إلك."),
            Rows = Enum.GetValues<PrayerMethod>().Select(m => new PickRow(m.ToString(), PrayerNames.Method(m)) { Sel = m == current, Check = m == current }).ToList(),
        };
    }

    private void OnPanel(Surfaces.SurfaceEvent e) => Safe(() =>
    {
        bool methods = _panel.Name == "method";
        switch (e.Kind)
        {
            case "pick" when methods && Enum.TryParse<PrayerMethod>(e.Value, out var m):
                _set(s => s with { PrayerMethod = m });
                Log.Write($"prayer: method {m}");
                Rebuild();
                _panel.Update(TimesSpec());
                break;
            case "back" when methods:
                _panel.Update(TimesSpec());
                break;
            case "action" when e.Value == "method":
                _panel.Update(MethodSpec());
                break;
            case "action" when e.Value == "city":
                AskCity();
                break;
        }
    });

    private void AskCity() => Cards.City(_panel, _ui, Why, _find, (place, typed) => Safe(() =>
    {
        _set(s => s with { PrayerCity = place.Name ?? typed.Trim(), PrayerLatitude = place.Latitude, PrayerLongitude = place.Longitude });
        Log.Write("prayer: city set");   // never which city (spec §14)
        Rebuild();
        Open();
    }));

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
