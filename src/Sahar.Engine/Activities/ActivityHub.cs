using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Brain;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// Her live activities (spec 0.13): the sources put activities and moments here; the hub keeps the stack
/// (<see cref="ActivityStack"/>), hangs the front one on her tag, opens its card on a click, knocks from the top edge
/// while she hides for a fullscreen app, keeps finishes for «وإنت غايب», and says moments in her bubble (not when she
/// is hushed). The rules are Core's; this class only wires them to the screen. Logs say kinds and counts, never text.
/// </summary>
internal sealed class ActivityHub : IDisposable
{
    private readonly ActivityStack _stack = new();
    private readonly TagVisual _tag;
    private readonly InputWindow _input;
    private readonly ActivityCard _card;
    private readonly KnockWindow _knock;
    private readonly AwayLog _away = new();
    private readonly SaharVisual _sahar;
    private readonly Func<RectF> _body;
    private readonly DispatcherQueueTimer _tick;
    private readonly Dictionary<string, Func<CardData?>> _cards = [];
    private TagView? _view;
    private TagGeometry _geometry;
    private bool _shown, _lockedAway, _fullscreenAway;
    private string? _lastLoggedFront;
    private Need _need = Need.None;

    private enum Need { None, Tag }

    public Func<bool> Dark { get; set; } = () => false;
    public Func<bool> MenuOpen { get; set; } = () => false;
    public Func<bool> Hidden { get; set; } = () => false;
    public Func<bool> Dragged { get; set; } = () => false;
    public Func<bool> Hushed { get; set; } = () => false;
    public Func<bool> InCall { get; set; } = () => false;
    public Func<SaharSettings> Settings { get; set; } = () => new SaharSettings();
    public Func<FullscreenWhere> Fullscreen { get; set; } = () => FullscreenWhere.None;
    public Func<RectI?> FullscreenMonitor { get; set; } = () => null;
    public Action<string> Say { get; set; } = _ => { };
    public Action<string, RectF?, Action?> NeedArea { get; set; } = (_, _, then) => then?.Invoke();
    /// <summary>The front activity's card pressed a button (the sources act on it).</summary>
    public event Action<string, Activity?>? Pressed;
    /// <summary>A tag click that has no card of its own (her timer): the caller opens what fits.</summary>
    public event Action<Activity>? TagClicked;

    public ActivityHub(DispatcherQueue ui, CompositionHost host, bool capturable, SaharVisual sahar, Func<RectF> body)
    {
        _sahar = sahar;
        _body = body;
        _tag = new TagVisual(host);
        _input = new InputWindow(capturable, new RectI(0, 0, 1, 1)) { Handler = OnTagMessage };
        _input.SetVisible(false);
        _card = new ActivityCard(host, capturable);
        _card.Need = (who, area, then) => NeedArea(who, area, then);
        _card.Dark = () => Dark();
        _card.Placement = () => Settings().MenuPlace;
        _card.Pressed += id => Safe(() => Press(id));
        _card.OpenChanged += _ => Safe(Refresh);
        _knock = new KnockWindow(host, ui, capturable);
        _tick = ui.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += (_, _) => Safe(Tick);
        _tick.Start();
    }

    public ActivityStack Stack => _stack;
    public ActivityCard Card => _card;
    public bool CardOpen => _card.IsOpen;

    // ---- what the sources call ----

    /// <summary>A live activity (an update keeps its place in time). Off in the settings: not shown.</summary>
    public void Set(Activity a, Func<CardData?>? card = null)
    {
        if (card is not null) _cards[a.Id] = card;
        if (!ActivitySwitch.On(a.Kind, Settings())) { Remove(a.Id); return; }
        bool isNew = !_stack.Has(a.Id);
        _stack.Set(a, DateTimeOffset.Now);
        if (isNew)
        {
            Log.Write($"activity: {a.Kind} on");
            if (a.Kind == ActivityKind.ClaudeAsk) Knocked(a);
        }
        Refresh();
    }

    /// <summary>A moment (6 s; the volume 1.5 s): while she hides for fullscreen it knocks, is kept, or is dropped.</summary>
    public void Moment(Activity a, Func<CardData?>? card = null, string? line = null)
    {
        if (card is not null) _cards[a.Id] = card;
        if (!ActivitySwitch.On(a.Kind, Settings())) return;
        if (_away.Locked || _fullscreenAway) _away.Note(a.Kind, DateTimeOffset.Now);
        if (_fullscreenAway)
        {
            var act = Knock.Decide(a.Kind, FullscreenWhere.Hers, a.Alert);
            Log.Write($"activity: {a.Kind} during fullscreen ({act})");
            if (act == FullscreenAct.Knock) Knocked(a);
            return;
        }
        if (_lockedAway) return;   // the lock screen: kept for «وإنت غايب»
        _stack.Moment(a, DateTimeOffset.Now);
        Log.Write($"activity: {a.Kind} moment");
        if (line is not null && !Hushed()) Say(line);
        Refresh();
    }

    public void Remove(string id)
    {
        if (!_stack.Has(id)) return;
        _stack.Remove(id);
        Refresh();
    }

    public void RemoveWhere(Func<string, bool> match)
    {
        foreach (var a in _stack.All(DateTimeOffset.Now).Where(a => match(a.Id)).ToList()) _stack.Remove(a.Id);
        Refresh();
    }

    /// <summary>Settings changed: kinds switched off go now.</summary>
    public void SettingsChanged()
    {
        var s = Settings();
        foreach (var a in _stack.All(DateTimeOffset.Now).Where(a => !ActivitySwitch.On(a.Kind, s)).ToList()) _stack.Remove(a.Id);
        Refresh();
    }

    /// <summary>The PC locked (true) or unlocked: finishes are kept, and on unlock after ≥ 2 min she says what happened.</summary>
    public void Locked(bool locked)
    {
        _lockedAway = locked;
        if (locked) { _away.Lock(DateTimeOffset.Now); return; }
        Welcome();
    }

    /// <summary>She hid for a fullscreen app on her monitor (true) or came back.</summary>
    public void FullscreenAway(bool away)
    {
        if (away == _fullscreenAway) return;
        _fullscreenAway = away;
        if (away) { _away.Lock(DateTimeOffset.Now); return; }
        Welcome();
    }

    public event Action? Welcomed;

    private void Welcome()
    {
        if (_away.Unlock(DateTimeOffset.Now) is not { } summary || !Settings().ActAway) return;
        Log.Write($"activity: away summary ({summary.Total} things, {summary.Rows.Count} kinds)");
        var a = new Activity("away", ActivityKind.Back, ActivityText.Away, ActivityText.N(summary.Total), "lock");
        _cards["away"] = () => new CardData(CardKind.Away, Away: summary);
        // A minute (spec 0.13 §2): a live item that the tick removes.
        _stack.Set(a, DateTimeOffset.Now);
        _awayUntil = DateTimeOffset.Now.AddMinutes(1);
        if (!Hushed()) Say(ActivityText.AwayBubble(summary.Total));
        Welcomed?.Invoke();
        Refresh();
    }

    private DateTimeOffset? _awayUntil;

    // ---- the tag ----

    private void Tick()
    {
        if (_awayUntil is { } until && DateTimeOffset.Now >= until) { _awayUntil = null; _stack.Remove("away"); }
        Refresh();
        if (_card.IsOpen && _cardFor is { } id && _cards.TryGetValue(id, out var make) && make() is { } data) _card.Update(data);
    }

    private string? _cardFor;

    /// <summary>Re-reads the stack and shows, moves or hides the tag.</summary>
    public void Refresh()
    {
        var now = DateTimeOffset.Now;
        _view = _stack.View(now);
        bool show = _view is not null && ActivityStack.TagShown(_card.IsOpen, MenuOpen(), Hidden(), Dragged());
        var front = _view?.Front.Kind.ToString();
        if (front != _lastLoggedFront) { _lastLoggedFront = front; Log.Write($"activity: tag {front ?? "none"} ({_view?.Beads.Count ?? 0} beads)"); }
        if (!show)
        {
            if (_shown) { _tag.Hide(); _input.SetVisible(false); _shown = false; }
            SetNeed(null);
            return;
        }
        var v = _view!;
        var front2 = InCall() ? ActivityText.ForCall(v.Front) : v.Front;
        _geometry = Geometry(v);
        var outside = TagPlace.OutsideFollow(TagPlace.WithShadow(_geometry.Tag, _sahar.DpiScale),
            OverlayPlan.Follow(new RectF(_sahar.Position.X, _sahar.Position.Y, _sahar.CanvasPx.Width, _sahar.CanvasPx.Height), _sahar.DpiScale));
        var draw = () =>
        {
            if (_view is null) return;
            var (cover, colour) = CoverOf?.Invoke(front2) ?? (null, null);
            _tag.Show(front2, v.Beads.Count, v.Knot, _geometry, Tokens.For(Dark()), Dark(), cover, colour, _sahar.DpiScale);
            MoveInput();
            if (!_shown) { _input.SetVisible(true); _shown = true; }
        };
        if (outside) SetNeed(TagPlace.WithShadow(_geometry.Tag, _sahar.DpiScale), draw);
        else { SetNeed(null); draw(); }
    }

    /// <summary>The cover for the music tag (the music source sets it).</summary>
    public Func<Activity, (Pixels?, Rgba?)>? CoverOf { get; set; }

    private void SetNeed(RectF? area, Action? then = null)
    {
        if (area is null && _need == Need.None) { then?.Invoke(); return; }
        _need = area is null ? Need.None : Need.Tag;
        NeedArea("tag", area, then);
    }

    /// <summary>She moved (every frame of a flight): the tag follows, whole pixels.</summary>
    public void OnMoved()
    {
        if (!_shown || _view is null) return;
        var g = Geometry(_view);
        if (g.Side != _geometry.Side || MathF.Abs(g.Tag.Width - _geometry.Tag.Width) > 0.5f) { Refresh(); return; }
        _geometry = g;
        _tag.Move(g);
        MoveInput();
        if (Dragged()) Refresh();
    }

    private TagGeometry Geometry(TagView v)
    {
        float dpi = _sahar.DpiScale;
        var body = _body();
        var work = Monitors.At(new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2)).WorkF;
        float w = MathF.Ceiling(ActivityPainter.TagWidth(InCall() ? ActivityText.ForCall(v.Front) : v.Front) * dpi);
        float thread = TagPlace.Thread(v.Beads.Count, v.Knot);
        var tag = TagPlace.Place(body, work, new Vector2(w, MathF.Ceiling(TagPlace.HeightDip * dpi)), dpi, out var side, thread);
        var to = new Vector2(tag.X + tag.Width / 2, tag.Y + 5.5f * dpi);
        var from = side switch
        {
            TagSide.Below => new Vector2(MathF.Round(body.X + body.Width / 2), body.Bottom - 2 * dpi),
            TagSide.Right => new Vector2(body.Right - 4 * dpi, body.Y + body.Height * 0.62f),
            _ => new Vector2(body.X + 4 * dpi, body.Y + body.Height * 0.62f),
        };
        return new TagGeometry(tag, from, to, side);
    }

    private void MoveInput()
    {
        var t = _geometry.Tag;
        float dpi = _sahar.DpiScale;
        // The tag and its beads (from 4 DIP below her, so her own edge keeps her clicks).
        var a = _geometry.From + Vector2.Normalize(_geometry.To - _geometry.From) * 4 * dpi;
        float l = MathF.Min(t.X, a.X - 5 * dpi), r = MathF.Max(t.Right, a.X + 5 * dpi);
        float top = MathF.Min(t.Y, a.Y), bottom = MathF.Max(t.Bottom, a.Y);
        Win.Apply(_input.MoveTo(new RectI((int)MathF.Floor(l), (int)MathF.Floor(top), (int)MathF.Ceiling(r), (int)MathF.Ceiling(bottom))));
    }

    private nint? OnTagMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
                return 0;
            case Native.WM_LBUTTONUP:
                var r = _input.Rect;
                var p = new Vector2(r.Left + (short)(lParam & 0xFFFF), r.Top + (short)((lParam >> 16) & 0xFFFF));
                Safe(() => ClickAt(p));
                return 0;
            case ActNative.WM_MOUSEWHEEL:
                int delta = (short)((wParam >> 16) & 0xFFFF);
                Safe(() => Wheel(delta > 0 ? -1 : 1));
                return 0;
            default:
                return null;
        }
    }

    private void ClickAt(Vector2 p)
    {
        if (_view is null) return;
        float dpi = _sahar.DpiScale;
        for (int i = 0; i < _view.Beads.Count; i++)
        {
            var b = _geometry.From + Vector2.Normalize(_geometry.To - _geometry.From) * (7 + i * 9) * dpi;
            if (Vector2.Distance(b, p) <= 6 * dpi) { PickBead(i); return; }
        }
        if (_geometry.Tag.Contains(p.X, p.Y)) OpenCard();
    }

    public void PickBead(int i)
    {
        if (_view is null || i >= _view.Beads.Count) return;
        _stack.Pick(_view.Beads[i].Id, DateTimeOffset.Now);
        Log.Write("activity: bead picked");
        Refresh();
    }

    public void Wheel(int steps)
    {
        _stack.Wheel(steps, DateTimeOffset.Now);
        Refresh();
    }

    /// <summary>A click on the tag: its card opens beside her (as the menu does).</summary>
    public void OpenCard(string? id = null)
    {
        var front = id is null ? _view?.Front : _stack.All(DateTimeOffset.Now).FirstOrDefault(a => a.Id == id);
        if (front is null) return;
        if (!_cards.TryGetValue(front.Id, out var make) || make() is not { } data)
        {
            TagClicked?.Invoke(front);
            return;
        }
        _cardFor = front.Id;
        _card.Open(data, _body());
        Refresh();
    }

    public void CloseCard() => _card.Close();

    private void Press(string id)
    {
        var front = _cardFor is { } f ? _stack.All(DateTimeOffset.Now).FirstOrDefault(a => a.Id == f) : null;
        if (id == "away-ok") { _stack.Remove("away"); _card.Close(); return; }
        Pressed?.Invoke(id, front);
        if (_card.IsOpen && _cardFor is { } cf && _cards.TryGetValue(cf, out var make) && make() is { } data) _card.Update(data);
    }

    /// <summary>Re-draws the open card from its source (after a source changed what it shows).</summary>
    public void CardChanged()
    {
        if (_card.IsOpen && _cardFor is { } cf && _cards.TryGetValue(cf, out var make) && make() is { } data) _card.Update(data);
    }

    // ---- the knock ----

    private void Knocked(Activity a)
    {
        if (!_fullscreenAway || Fullscreen() != FullscreenWhere.Hers || FullscreenMonitor() is not { } m) return;
        if (Knock.Decide(a.Kind, FullscreenWhere.Hers, a.Alert) != FullscreenAct.Knock) return;
        float dpi = Monitors.At(new Vector2(m.Left + m.Width / 2f, m.Top + m.Height / 2f)).DpiScale;
        _knock.Show(InCall() ? ActivityText.ForCall(a) : a, m, dpi, Dark());
        Log.Write($"activity: knock ({a.Kind})");
    }

    /// <summary>Test mode: a knock on a monitor as if something were fullscreen there.</summary>
    public void TestKnock(Activity a, RectI monitor)
    {
        float dpi = Monitors.At(new Vector2(monitor.Left + monitor.Width / 2f, monitor.Top + monitor.Height / 2f)).DpiScale;
        _knock.Show(a, monitor, dpi, Dark());
    }

    public void DeviceLost()
    {
        _tag.Redraw();
        _card.DeviceLost();
    }

    public string Describe()
    {
        var v = _view;
        string B(bool b) => b ? "true" : "false";
        var beads = v is null ? "" : string.Join(',', v.Beads.Select(b => b.Kind));
        return $"front={v?.Front.Kind.ToString() ?? "none"} frontId={v?.Front.Id ?? "none"} beads={beads} knot={B(v?.Knot ?? false)} tagShown={B(_shown)} " +
            $"side={_geometry.Side} {_tag.Describe(_geometry)} input={_input.Rect.Left},{_input.Rect.Top},{_input.Rect.Width},{_input.Rect.Height} " +
            $"drawn={_tag.Drawn} away={B(_away.Locked)} {_knock.Describe()} {_card.Describe()} count={_stack.All(DateTimeOffset.Now).Count}";
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"activity error: {e}"); }
    }

    public void Dispose()
    {
        _tick.Stop();
        _tag.Dispose();
        _card.Dispose();
        _knock.Dispose();
        Native.DestroyWindow(_input.Hwnd);
    }
}
