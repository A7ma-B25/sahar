using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// The app in front and how long you have been on it (spec §8.1), and her gentle comments about it (§8.2: at most
/// one per 45 min, fewer when you click them away). Only the program's name is used; window titles are read just
/// to skip private browsing windows and never kept.
/// </summary>
internal sealed unsafe class AppWatch
{
    private readonly AppTime _time = new();
    private readonly CommentLimiter _limiter = new();
    private readonly CommentPicker _picker = new();
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Now);
    private readonly Func<bool> _allowed;
    private readonly Func<bool> _away;
    private readonly Action<string> _say;
    private TimeSpan _skip;   // test mode: a fake clock offset

    /// <param name="ownComments">False on a test machine: its real clock (a morning hello) must never talk over other
    /// tests; there only the pipe's comment-now makes her comment.</param>
    public AppWatch(DispatcherQueue ui, Func<bool> allowed, Func<bool> away, Action<string> say, bool ownComments = true)
    {
        _allowed = allowed;
        _away = away;
        _say = say;
        Foreground.Changed += () => Safe(Front);
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromMinutes(1);
        t.Tick += (_, _) => Safe(() => { if (_away()) _time.Away(Now); else Front(); if (ownComments) Consider(); });
        t.Start();
        Front();
    }

    private DateTimeOffset Now => DateTimeOffset.Now + _skip;
    public CommentLimiter Limiter => _limiter;

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"app watch error: {e.GetType().Name}"); }
    }

    private void Front()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0 || Win.Own.Contains(fg)) return;
        char* title = stackalloc char[256];
        int n = Native.GetWindowText(fg, title, 256);
        if (n > 0 && PrivacyLists.IsPrivateWindow(new string(title, 0, n))) { _time.Away(Now); return; }
        var exe = Native.WindowProcessPath(fg);
        if (exe.Length == 0) return;
        _time.Front(exe, Now);
    }

    /// <summary>A comment, if one fits now (rules, the 45-minute limiter, something worth saying).</summary>
    public bool Consider()
    {
        var now = Now;
        var today = DateOnly.FromDateTime(now.Date);
        if (today != _day) { _day = today; _limiter.NewDay(); }
        bool away = _away();
        if (!_allowed() && !away) return false;
        var line = _picker.Next(_time.Current, _time.Continuous(now), now, away || !_allowed());
        if (line is null || !_limiter.TryComment(now)) return false;
        Log.Write("comment: shown");
        _say(line);
        return true;
    }

    public void Skip(TimeSpan by) => _skip += by;

    public string Describe() =>
        FormattableString.Invariant($"app={PrivacyLists.ExeName(_time.Current ?? "")} minutes={_time.Continuous(Now).TotalMinutes:0} gapMinutes={_limiter.Gap.TotalMinutes:0}");
}
