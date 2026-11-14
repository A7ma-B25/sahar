using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Menu;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Activities;

/// <summary>
/// An activity's card (spec 0.13 §1.2; ActCards): the 0.11 menu card's frame (296 wide, radius 22, the menu shadow),
/// opened where the menu opens and closed as it closes (Esc, a click outside, another window). The overlay grows to it
/// first (a need), and the card shows only once the stage settled. Buttons raise <see cref="Pressed"/>.
/// </summary>
internal sealed class ActivityCard : IDisposable
{
    private readonly CardVisual _visual;
    private readonly MenuCatcher _catcher;
    private readonly Dictionary<string, RectF> _hits = [];
    private CardData? _data;
    private RectF _card;
    private float _dpi = 1;
    private int _gen;
    private string? _hovered;

    public Action<string, RectF?, Action?> Need { get; set; } = (_, _, then) => then?.Invoke();
    public Func<bool> Dark { get; set; } = () => false;
    public Func<MenuPlace> Placement { get; set; } = () => MenuPlace.Beside;
    public bool IsOpen { get; private set; }
    public CardKind? Kind => IsOpen ? _data?.Kind : null;
    public RectF Rect => _card;
    public event Action<string>? Pressed;
    public event Action<bool>? OpenChanged;
    /// <summary>A shelf tile pulled out of the card (pressed and moved more than 6 px).</summary>
    public event Action<string>? DragOut;
    private string? _downId;
    private Vector2 _downAt;
    private bool _dragged;

    private void Drag(Vector2 p)
    {
        if (_downId is not { } id || _dragged || !id.StartsWith("tile-", StringComparison.Ordinal) || Native.GetKeyState(0x01) >= 0) return;
        if (Vector2.Distance(p, _downAt) < 6 * _dpi) return;
        _dragged = true;
        _downId = null;
        DragOut?.Invoke(id);
    }

    public ActivityCard(Host.CompositionHost host, bool capturable)
    {
        _visual = new CardVisual(host) { Clasp = false };
        _catcher = new MenuCatcher(capturable)
        {
            Clicked = p => Safe(() => Click(p)),
            Moved = p => Safe(() => { Hover(p); Drag(p); }),
            Down = p => Safe(() => { _downId = Hit(p); _downAt = p; _dragged = false; }),
            Dismissed = () => Safe(Close),
            Deactivated = () => Safe(Close),
            KeyPressed = k => Safe(() => { if (k == MenuKey.Escape) Close(); }),
        };
    }

    public void Open(CardData data, RectF body)
    {
        if (IsOpen) Close();
        IsOpen = true;
        _data = data;
        var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        var monitor = Monitors.At(centre);
        _dpi = monitor.DpiScale;
        float h = Draw(fade: false);
        var place = MenuCardLayout.Place(body, monitor.WorkF, new Vector2(ActivityPainter.CardWidth, h) * _dpi, Placement(), _dpi);
        _card = place.Card;
        _catcher.Show(centre);
        int gen = ++_gen;
        Need("act-card", Area(place.Card), () =>
        {
            if (!IsOpen || gen != _gen) return;
            _visual.Open(place.Card, place.Near, place.MoonCentre);
        });
        Log.Write($"activity: card open ({data.Kind})");
        OpenChanged?.Invoke(true);
    }

    /// <summary>New data for the open card (the song moved on, the copy went on): redrawn in place.</summary>
    public void Update(CardData data)
    {
        if (!IsOpen || data.Kind != _data?.Kind) return;
        _data = data;
        float h = Draw(fade: false);
        if (MathF.Abs(h * _dpi - _card.Height) > 0.5f)
        {
            _card = _card with { Height = h * _dpi };
            Need("act-card", Area(_card), null);
        }
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        int gen = ++_gen;
        _catcher.Hide();
        _visual.Close(() => { if (!IsOpen && gen == _gen) Need("act-card", null, null); });
        _hovered = null;
        Log.Write("activity: card closed");
        OpenChanged?.Invoke(false);
    }

    /// <summary>Draws the card's two layers; returns its height (DIP).</summary>
    private float Draw(bool fade)
    {
        var d = _data!;
        bool dark = Dark();
        var t = Tokens.For(dark);
        var now = DateTimeOffset.Now;
        _hits.Clear();
        float h = ActivityPainter.Card(null, 0, d, t, dark, now, _hits);
        var key = Key(d, dark, h, now);
        _visual.Draw(dc => ActivityPainter.Card(dc, 1, d, t, dark, now, new Dictionary<string, RectF>(), h),
            dc => ActivityPainter.Card(dc, 2, d, t, dark, now, new Dictionary<string, RectF>(), h), t, _dpi, fade, key);
        return h;
    }

    private static string Key(CardData d, bool dark, float h, DateTimeOffset now) =>
        string.Join('|', d.GetHashCode(), dark, h, d.Kind is CardKind.Claude or CardKind.Weather or CardKind.Copy ? now.ToString("HH:mm") : "");

    /// <summary>The card plus its shadow (as the menu's), in screen px.</summary>
    private RectF Area(RectF card)
    {
        float s = 40 * _dpi, below = 56 * _dpi;
        return new RectF(card.X - s, card.Y - s, card.Width + 2 * s, card.Height + s + below);
    }

    private RectF Screen(RectF r) => new(_card.X + r.X * _dpi, _card.Y + r.Y * _dpi, r.Width * _dpi, r.Height * _dpi);

    private string? Hit(Vector2 p)
    {
        if (!_visual.Shown) return null;
        foreach (var (id, r) in _hits) if (Screen(r).Contains(p.X, p.Y)) return id;
        return null;
    }

    private void Click(Vector2 p)
    {
        if (_dragged) { _dragged = false; return; }
        if (Hit(p) is { } id)
        {
            _visual.SetHover(_hits[id], true, 10);
            Log.Write($"activity: card press {id}");
            Pressed?.Invoke(id);
        }
        else if (!_card.Contains(p.X, p.Y)) Close();
    }

    private void Hover(Vector2 p)
    {
        var id = Hit(p);
        if (id == _hovered) return;
        _hovered = id;
        _visual.SetHover(id is not null ? _hits[id] : null, false, 10);
    }

    /// <summary>"open=… kind=… card=x,y,w,h buttons=id:cx,cy;…" (screen px) for test diagnostics.</summary>
    public string Describe()
    {
        if (!IsOpen) return "cardOpen=false";
        var b = string.Join(';', _hits.Select(p => { var r = Screen(p.Value); return FormattableString.Invariant($"{p.Key}:{r.X + r.Width / 2:0},{r.Y + r.Height / 2:0}"); }));
        return FormattableString.Invariant($"cardOpen=true cardKind={_data!.Kind} cardShown={(_visual.Shown ? "true" : "false")} card={_card.X:0},{_card.Y:0},{_card.Width:0},{_card.Height:0} buttons={b}");
    }

    /// <summary>A button press from the pipe (tests).</summary>
    public bool Press(string id)
    {
        if (!IsOpen || !_hits.ContainsKey(id)) return false;
        Pressed?.Invoke(id);
        return true;
    }

    public void DeviceLost()
    {
        Close();
        _visual.Redraw();
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"activity card error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
    }

    public void Dispose() => _visual.Dispose();
}
