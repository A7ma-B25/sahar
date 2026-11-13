using System.Runtime.InteropServices;
using Sahar.Core.Activities;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// File copies (spec 0.13 §2, an approved extra): Explorer's own copy dialog (<c>OperationStatusWindow</c>), read-only.
/// A window-show hook wakes the reader; while the dialog exists its title's percentage is read once a second (with
/// InternalGetWindowText, which sends the dialog nothing, so a busy Explorer never blocks her); the time left is our
/// own smoothed pace. When it closes after progress, «خلص النسخ» is a moment. Nothing about the files is read.
/// </summary>
internal sealed unsafe class CopySource
{
    private const string Class = "OperationStatusWindow";
    private static CopySource? _instance;
    private readonly ActivityHub _hub;
    private readonly DispatcherQueueTimer _timer;
    private nint _hook, _window;
    private CopyPace? _pace;

    public CopySource(DispatcherQueue ui, ActivityHub hub)
    {
        _hub = hub;
        _instance = this;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Safe(Read);
        _hook = Native.SetWinEventHook(ActNative.EVENT_OBJECT_SHOW, ActNative.EVENT_OBJECT_SHOW, 0, &OnShow, 0, 0, 2 /* skip own process */);
        hub.Pressed += (id, _) => { if (id == "copy-show") Safe(ShowDialog); };
        Find();
    }

    [UnmanagedCallersOnly]
    private static void OnShow(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (idObject != 0 || _instance is not { } me || me._window != 0) return;
            if (ActNative.ClassOf(hwnd) == Class) me.Start(hwnd);
        }
        catch (Exception) { }
    }

    private void Find()
    {
        fixed (char* c = Class)
        {
            var w = ActNative.FindWindow(c, null);
            if (w != 0) Start(w);
        }
    }

    private void Start(nint window)
    {
        _window = window;
        _pace = new CopyPace();
        Log.Write("copy: started");
        _timer.Start();
        Read();
    }

    private void Read()
    {
        var now = DateTimeOffset.Now;
        if (_window == 0 || _pace is null) { _timer.Stop(); return; }
        if (Native.IsWindow(_window) == 0)
        {
            _pace.Gone(now);
            End(_pace.Done);
            return;
        }
        if (CopyTitle.Percent(ActCom.Title(_window)) is { } p)
        {
            _pace.Sample(p, now);
            if (_pace.Done) { End(true); return; }
            _hub.Set(new Activity("copy", ActivityKind.Copy, ActivityText.Copying, ActivityText.Percent(p), "copy", (float)(p / 100)), Card);
        }
    }

    private void End(bool done)
    {
        _timer.Stop();
        _window = 0;
        _hub.Remove("copy");
        Log.Write(done ? "copy: done" : "copy: closed");
        if (done) _hub.Moment(new Activity("copy-done", ActivityKind.CopyDone, ActivityText.CopyDone, null, "check"), Card);
        _pace = null;
        Find();   // another dialog may still be open
    }

    private CardData? Card() => _pace is { } p ? new CardData(CardKind.Copy, Copy: new CopyCard(p.Percent, p.Left)) : new CardData(CardKind.Copy, Copy: new CopyCard(100, null));

    /// <summary>«فرجيني نافذة النسخ» (after your click on the card, so Windows lets us bring it forward).</summary>
    private void ShowDialog()
    {
        if (_window == 0 || Native.IsWindow(_window) == 0) return;
        ActNative.ShowWindow(_window, 9 /* SW_RESTORE */);
        ActNative.SetForegroundWindow(_window);
        _hub.CloseCard();
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"copy source error: {e.GetType().Name}"); }
    }
}
