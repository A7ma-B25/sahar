using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Windows.System;
using Windows.UI.Composition;

namespace Sahar.Engine.Host;

/// <summary>
/// Keeps the overlay window on her (<see cref="OverlayPlan"/>): her canvas plus margins, grown to what a feature needs
/// while it runs. Every visual keeps overlay coordinates (= screen pixels): the composition root is shifted by the
/// window's top-left, so moving the window never moves anything on screen. While she flies, the window moves with
/// her (position only, batched with her input window); at rest nothing happens.
/// A need never moves the window in the turn that asked for it (0.11, the owner's menu glitch): a feature's first
/// turn can be long (drawing its surfaces), and the window moved at once while the root's new offset waited for that
/// turn's commit — 0.4 s of her whole drawing displaced on the runner. The window and the root change together in a
/// short turn after that commit; a feature shows its visuals only once the stage has settled.
/// </summary>
internal sealed class OverlayStage
{
    private readonly OverlayWindow _overlay;
    private readonly ContainerVisual _root;
    private readonly Dictionary<string, RectF> _needs = [];
    private IReadOnlyList<RectI> _monitors;
    private RectF _canvas;
    private float _dpi = 1;
    private bool _placed;
    private Vector3 _rootOffset = new(float.NaN);
    private readonly DispatcherQueue? _ui;
    private bool _scheduled;
    private readonly List<Action> _waiting = [];

    public OverlayStage(OverlayWindow overlay, ContainerVisual root, IReadOnlyList<RectI> monitors)
    {
        _overlay = overlay;
        _root = root;
        _monitors = monitors;
        _ui = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>A need is waiting for the stage to apply it (test diagnostics).</summary>
    public bool Settling => _scheduled;

    public RectI Rect => _overlay.Rect;
    /// <summary>Window moves and resizes so far (test diagnostics: none at rest).</summary>
    public int Moves { get; private set; }
    public int Resizes { get; private set; }

    /// <summary>Her canvas moved (screen pixels); <paramref name="with"/> is moved in the same batch (her input window).</summary>
    public void Follow(RectF canvas, float dpiScale, WindowMove? with = null)
    {
        _canvas = canvas;
        _dpi = dpiScale;
        _placed = true;
        Apply(with);
    }

    /// <summary>A feature needs an area drawn (screen pixels) while it runs; null when it ends. The window changes
    /// after this turn's commit, in a turn of its own; <paramref name="settled"/> runs once that change is committed
    /// too (at once when nothing changes), so the feature's visuals never show at a stale window.</summary>
    public void Need(string who, RectF? area, Action? settled = null)
    {
        bool changed;
        if (area is { } a) { changed = !_needs.TryGetValue(who, out var old) || old != a; _needs[who] = a; }
        else changed = _needs.Remove(who);
        if (settled != null) _waiting.Add(settled);
        if (!_placed || _ui is null) { Apply(null); RunWaiting(); return; }
        if (!changed && !_scheduled) { RunWaiting(); return; }
        if (_scheduled) return;
        _scheduled = true;
        AfterCommit(() =>
        {
            _scheduled = false;
            bool moved = Apply(null);
            if (moved) AfterCommit(RunWaiting); else RunWaiting();
        });
    }

    private void AfterCommit(Action then)
    {
        var op = _root.Compositor.RequestCommitAsync();
        op.Completed = (_, _) => _ui!.TryEnqueue(() =>
        {
            try { then(); }
            catch (Exception e) { Log.Write($"stage error: {e}"); }
        });
    }

    private void RunWaiting()
    {
        var list = _waiting.ToArray();
        _waiting.Clear();
        foreach (var w in list)
        {
            try { w(); }
            catch (Exception e) { Log.Write($"stage error: {e}"); }
        }
    }

    /// <summary>The monitors changed (plugged, unplugged, resolution).</summary>
    public void Replan(IReadOnlyList<RectI> monitors)
    {
        _monitors = monitors;
        _overlay.Refresh();   // Windows may have moved it itself: plan from where it really is
        if (_placed) Apply(null);
    }

    public string Describe()
    {
        var r = Rect;
        return $"x={r.Left} y={r.Top} w={r.Width} h={r.Height} moves={Moves} resizes={Resizes} needs={(_needs.Count == 0 ? "none" : string.Join(',', _needs.Keys))}";
    }

    /// <summary>Places the window and the root; true when the window changed.</summary>
    private bool Apply(WindowMove? with)
    {
        var target = OverlayPlan.Plan(_canvas, _dpi, _needs.Values, _monitors);
        var before = _overlay.Rect;
        var move = _overlay.MoveTo(target);
        Win.Apply(move, with);
        if (move is not null)
        {
            if (target.Width == before.Width && target.Height == before.Height) Moves++;
            else Resizes++;
        }
        // Overlay coordinates stay screen pixels: the root sits at minus the window's top-left.
        var offset = new Vector3(-target.Left, -target.Top, 0);
        if (offset != _rootOffset) _root.Offset = _rootOffset = offset;
        return move is not null;
    }
}
