using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// The panel surface beside her (pickers, input cards, ask / choose bubbles, «الدرج»): one at a time. A new one closes
/// the last (its handler gets "closed"). Keeps the spec on show for her pipe's test commands (debug-list, list-pick,
/// debug-bubble, …), which call the same handler a click in the page would.
/// </summary>
internal sealed class Panel(SurfaceClient client, Func<RectF> body)
{
    public const string Surface = "panel";
    private PanelSpec? _spec;
    private Action<SurfaceEvent>? _handler;
    private int _seq;

    public bool IsOpen => _spec is not null;
    public PanelSpec? Spec => _spec;
    public string? Name => _spec?.Name;
    /// <summary>A panel just opened or closed (her pipe and the card's mutual exclusion listen).</summary>
    public event Action<bool>? OpenChanged;
    /// <summary>A panel is about to show (its mode): an ask bubble above her clears her speech bubble first.</summary>
    public event Action<string>? Opening;

    /// <param name="activate">False for an ask she starts herself (review I2): shown without his focus, answered by a
    /// click only, after a short guard.</param>
    public void Show(PanelSpec spec, Action<SurfaceEvent> handler, string mode = "beside", System.Numerics.Vector2? anchor = null, bool activate = true)
    {
        Opening?.Invoke(mode);
        int seq = 0;
        seq = client.Show(Surface, spec.Name, mode, spec.ToModel(), body(), e => Event(seq, e), anchor, activate);   // the last one hears "closed" first
        _seq = seq;
        bool was = IsOpen;
        _spec = spec;
        _handler = handler;
        if (!was) OpenChanged?.Invoke(true);
    }

    /// <summary>The same panel with new content (a drill, an error, suggestions): it keeps its place.</summary>
    public void Update(PanelSpec spec)
    {
        if (!IsOpen) return;
        _spec = spec;
        client.Update(Surface, _seq, spec.ToModel());
    }

    private void Event(int seq, SurfaceEvent e)
    {
        if (seq != _seq) return;
        var h = _handler;
        if (e.Kind is "closed" or "failed") Forget();
        h?.Invoke(e);
    }

    public void Close()
    {
        if (!IsOpen) return;
        var h = _handler;
        client.Hide(Surface, _seq);
        Forget();
        h?.Invoke(new SurfaceEvent("closed", null));
    }

    /// <summary>The handler is done with it (it picked): hide it without calling back.</summary>
    public void Done()
    {
        if (!IsOpen) return;
        client.Hide(Surface, _seq);
        Forget();
    }

    private void Forget()
    {
        if (_spec is null) return;
        _spec = null;
        _handler = null;
        OpenChanged?.Invoke(false);
    }

    // ---- her pipe's test commands (the same paths as a click) ----

    public bool PickLine(int index)
    {
        if (_spec is not { } s) return false;
        var rows = s.Kind is "ask" && s.Rows.Count == 0 ? s.Buttons.Select(b => new PickRow(b.Id, b.Label)).ToList() : s.Rows;
        if (index < 0 || index >= rows.Count) return false;
        _handler?.Invoke(new SurfaceEvent("pick", rows[index].Id));
        return true;
    }

    public bool PressButton()
    {
        if (_spec?.Button is not { } b) return false;
        _handler?.Invoke(new SurfaceEvent(_spec.FootAction is null && _spec.SubAction is null && _spec.FootLink is null ? "secondary" : "action", b.Id));
        return true;
    }

    public bool Submit(string text)
    {
        if (_spec is null) return false;
        _handler?.Invoke(new SurfaceEvent("submit", text));
        return true;
    }

    public string Describe()
    {
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        if (_spec is not { } s) return "open=false lines=0 lines64= button64= title64= kind=none";
        return $"open=true kind={s.Name} lines={s.Lines.Count} lines64={B64(string.Join('\n', s.Lines))} button64={B64(s.Button?.Label ?? "")} " +
               $"title64={B64(s.Sub is null ? s.Title : s.Title + "\n" + s.Sub)} error64={B64(s.Error ?? "")} value64={B64(s.Value ?? "")} placeholder64={B64(s.Placeholder ?? "")}";
    }
}
