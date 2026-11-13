using Sahar.Core.Activities;
using Sahar.Core.Senses;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// Screen time (spec 0.13 §2, an approved extra, off until you turn it on): seconds per app per day while you are
/// there (not idle, not locked; a playing video counts), in <c>%LOCALAPPDATA%\Sahar\usage\</c>, 7 days. App names only.
/// It never hangs a tag by itself; its card is in the wheel's cycle.
/// </summary>
internal sealed class ScreenTimeSource : IDisposable
{
    private readonly ScreenTime _time;
    private readonly string _folder;
    private readonly ActivityHub _hub;
    private readonly Func<SaharSettings> _settings;
    private readonly Func<bool> _idle, _locked, _video;
    private string? _app;
    private DateTimeOffset _mark = DateTimeOffset.Now, _saved = DateTimeOffset.Now;

    public ScreenTimeSource(DispatcherQueue ui, ActivityHub hub, string folder, Func<SaharSettings> settings, Func<bool> idle, Func<bool> locked, Func<bool> video)
    {
        _folder = folder;
        _time = new ScreenTime(folder);
        _hub = hub;
        _settings = settings;
        _idle = idle;
        _locked = locked;
        _video = video;
        _time.Prune(DateTimeOffset.Now);
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(30);
        t.Tick += (_, _) => Safe(Step);
        t.Start();
        Foreground.Changed += () => Safe(Step);
    }

    private void Step()
    {
        var now = DateTimeOffset.Now;
        if (!_settings().ActScreenTime) { _app = null; _mark = now; _hub.Remove("screentime"); return; }
        if (_app is { } app && ScreenTime.Counts(_idle(), _locked(), _video()) && now > _mark)
            _time.Add(app, now - _mark > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : now - _mark, now);
        _mark = now;
        nint fg = Native.GetForegroundWindow();
        if (fg != 0 && !Win.Own.Contains(fg))
        {
            var exe = Native.WindowProcessPath(fg);
            _app = exe.Length == 0 || PrivacyLists.IsPasswordManager(exe) ? null : ScreenTime.AppName(exe);
        }
        if (now - _saved > TimeSpan.FromMinutes(5)) { _time.Save(); _saved = now; }
        var total = _time.Total(now);
        if (total >= TimeSpan.FromMinutes(1))
            _hub.Set(new Activity("screentime", ActivityKind.ScreenTime, ActivityText.Today, ActivityText.Duration(total), "hourglass"), Card);
    }

    private CardData? Card()
    {
        var now = DateTimeOffset.Now;
        return new CardData(CardKind.Time, Time: new TimeCard(_time.Total(now), _time.Today(now),
            ScreenTime.TotalOf(_folder, DateOnly.FromDateTime(now.Date).AddDays(-1))));
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"screen time error: {e.GetType().Name}"); }
    }

    public void Dispose() => _time.Save();
}
