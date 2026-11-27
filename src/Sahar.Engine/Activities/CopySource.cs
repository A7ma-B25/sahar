using System.Runtime.InteropServices;
using Sahar.Core.Activities;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// File copies (spec 0.13 §2, an approved extra): Explorer's own copy dialog (<c>OperationStatusWindow</c>), read-only.
/// 0.17.2 (owner: other apps lagged, Explorer worst): no global window-show hook any more (every object shown in every
/// process was queued to her busy UI thread). While «النسخ» is on, a FindWindow every 2 s (the kernel walks the
/// top-level windows; nothing is sent to Explorer) looks for a VISIBLE dialog; its title's percentage is read with
/// InternalGetWindowText (sends nothing); the time left is our own smoothed pace. Hidden again = closed. When it closes
/// after progress, «خلص النسخ» is a moment. Nothing about the files is read.
/// </summary>
internal sealed unsafe class CopySource
{
    private const string Class = "OperationStatusWindow";
    private readonly ActivityHub _hub;
    private readonly DispatcherQueueTimer _timer;
    private readonly CopyWatch _watch = new();
    private bool _enabled;

    public CopySource(DispatcherQueue ui, ActivityHub hub)
    {
        _hub = hub;
        _timer = ui.CreateTimer();
        _timer.Interval = CopyWatch.PollEvery;
        _timer.Tick += (_, _) => Safe(() => Perf.Measure("copy.poll", Poll));
        hub.Pressed += (id, _) => { if (id == "copy-show") Safe(ShowDialog); };
    }

    /// <summary>«النسخ» on and she is not hidden by him: the poll runs; off: it stops and a shown copy goes.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled) return;
            _enabled = value;
            if (value) { _timer.Start(); Safe(Poll); }
            else
            {
                _timer.Stop();
                if (_watch.Window != 0) { _watch.Tick(false, "", DateTimeOffset.Now); _hub.Remove("copy"); }
            }
        }
    }

    private void Poll()
    {
        if (_watch.Window == 0)
        {
            nint w;
            fixed (char* c = Class) w = ActNative.FindWindow(c, null);
            bool visible = w != 0 && Native.IsWindowVisible(w) != 0;
            if (!_watch.Accept(w, visible)) return;
            Log.Write("copy: started");
        }
        Read();
    }

    private void Read()
    {
        if (_watch.Window is var w && w == 0) return;
        bool exists = Native.IsWindow(w) != 0 && Native.IsWindowVisible(w) != 0;   // hidden again = closed
        var u = _watch.Tick(exists, exists ? ActCom.Title(w) : "", DateTimeOffset.Now);
        switch (u.Step)
        {
            case CopyStep.Progress:
                _hub.Set(new Activity("copy", ActivityKind.Copy, ActivityText.Copying, ActivityText.Percent(u.Percent), "copy", (float)(u.Percent / 100)), Card);
                break;
            case CopyStep.Done or CopyStep.Closed:
                _hub.Remove("copy");
                Log.Write(u.Step == CopyStep.Done ? "copy: done" : "copy: closed");
                if (u.Step == CopyStep.Done) _hub.Moment(new Activity("copy-done", ActivityKind.CopyDone, ActivityText.CopyDone, null, "check"), Card);
                break;
        }
    }

    private CardData? Card() => _watch.Pace is { } p ? new CardData(CardKind.Copy, Copy: new CopyCard(p.Percent, p.Left)) : new CardData(CardKind.Copy, Copy: new CopyCard(100, null));

    /// <summary>«فرجيني نافذة النسخ» (after your click on the card, so Windows lets us bring it forward).</summary>
    private void ShowDialog()
    {
        var w = _watch.Window;
        if (w == 0 || Native.IsWindow(w) == 0) return;
        ActNative.ShowWindow(w, 9 /* SW_RESTORE */);
        ActNative.SetForegroundWindow(w);
        _hub.CloseCard();
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("copy source", e); }
    }
}
