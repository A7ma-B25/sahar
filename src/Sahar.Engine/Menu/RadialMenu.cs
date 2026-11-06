using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Menu;

/// <summary>
/// Her radial menu (spec §7, §7.1): opened by a right-click or a double-click on her, closed by Esc, a click
/// outside, or when she hides. Buttons with a second ring (timer, activity, do-not-disturb) open it in place;
/// the others close the menu and raise <see cref="Pressed"/>.
/// </summary>
internal sealed class RadialMenu : IDisposable
{
    private readonly MenuVisual _visual;
    private readonly MenuCatcher _catcher;
    private readonly Func<RectF> _body;
    private readonly Func<Vector2> _origin;
    private IReadOnlyList<MenuItem> _items = [];
    private ButtonSlot? _hovered;

    public Func<bool> TimerRunning { get; set; } = () => false;
    public Func<bool> DndOn { get; set; } = () => false;
    public Func<ActivityLevel> Level { get; set; } = () => ActivityLevel.Medium;

    public bool IsOpen { get; private set; }
    /// <summary>"main", or the id whose second ring is showing.</summary>
    public string Ring { get; private set; } = "main";
    public IReadOnlyList<ButtonSlot> Slots { get; private set; } = [];

    public event Action<string>? Pressed;
    public event Action<bool>? OpenChanged;

    public RadialMenu(CompositionHost host, bool capturable, Func<RectF> body, Func<Vector2> origin)
    {
        _visual = new MenuVisual(host);
        _catcher = new MenuCatcher(capturable)
        {
            Clicked = p => Safe(() => Click(p)),
            Moved = p => Safe(() => Hover(p)),
            Dismissed = () => Safe(Close),
        };
        _body = body;
        _origin = origin;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Show("main", MenuItems.Main);
        _catcher.Show();
        Log.Write("menu: open");
        OpenChanged?.Invoke(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _catcher.Hide();
        _visual.Clear();
        Slots = [];
        _hovered = null;
        Log.Write("menu: closed");
        OpenChanged?.Invoke(false);
    }

    public void Press(string id)
    {
        if (!IsOpen) return;
        if (id == "back") { Show("main", MenuItems.Main); return; }
        var options = MenuItems.Options(id, TimerRunning(), DndOn(), Level());
        if (options.Count > 0) { Show(id, options); return; }
        Log.Write($"menu: {id}");
        Close();
        Pressed?.Invoke(id);
    }

    private void Show(string ring, IReadOnlyList<MenuItem> items)
    {
        Ring = ring;
        _items = items;
        var body = _body();
        var monitor = Monitors.At(new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2));
        Slots = RadialLayout.Place(items, body, monitor.WorkF, monitor.DpiScale);
        _visual.Show(items, Slots, monitor.DpiScale, _origin());
        _hovered = null;
    }

    private ButtonSlot? Hit(Vector2 p)
    {
        foreach (var s in Slots)
            if (Vector2.Distance(s.Centre, p) <= s.Radius + 2) return s;
        return null;
    }

    private void Click(Vector2 p)
    {
        if (Hit(p) is { } s) Press(s.Id);
        else Close();
    }

    private void Hover(Vector2 p)
    {
        var s = Hit(p);
        if (s == _hovered) return;
        _hovered = s;
        _visual.SetHover(s, _origin());
    }

    /// <summary>"open=true ring=main buttons=id:x,y,r;…" (screen px) for the UI tests.</summary>
    public string Describe() =>
        $"open={(IsOpen ? "true" : "false")} level={Ring} buttons=" +
        string.Join(';', Slots.Select(s => FormattableString.Invariant($"{s.Id}:{s.Centre.X:0},{s.Centre.Y:0},{s.Radius:0}")));

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"menu error: {e}"); }
    }

    /// <summary>The GPU device was lost: close, and redraw the buttons next time.</summary>
    public void DeviceLost()
    {
        Close();
        _visual.Forget();
    }

    public void Dispose() => _visual.Dispose();
}
