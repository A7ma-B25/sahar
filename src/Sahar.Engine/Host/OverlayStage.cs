using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Sahar.Engine.Host;

/// <summary>
/// Keeps the overlay window on her (<see cref="OverlayPlan"/>): her canvas plus margins, grown to what a feature needs
/// while it runs. Every visual keeps overlay coordinates (= screen pixels): the composition root is shifted by the
/// window's top-left, so moving the window never moves anything on screen. While she flies, the window moves with
/// her (position only, batched with her input window); at rest nothing happens.
/// <para>
/// A need that moves the window's top-left (her menu, a game, the showcase) never moves the window she is drawn in
/// (0.11, the owner's menu glitch: a window move and the root's new offset reach the screen by different paths, and CI
/// frames showed her whole drawing 225 px off for up to 0.4 s, then for one frame). There are two overlay windows: the
/// spare is placed at the new rect while it is empty, and once it is there the root moves to it — detached from one
/// window's target, attached to the other's, with its new offset — in one composition commit, which the screen shows
/// whole. A need that only grows or shrinks the right and bottom edges just resizes (nothing can shift). A feature
/// shows its visuals only once the stage has settled (<see cref="Need"/>'s callback).
/// </para>
/// </summary>
internal sealed class OverlayStage
{
    private OverlayWindow _active, _spare;
    private DesktopWindowTarget _activeTarget, _spareTarget;
    private readonly ContainerVisual _root;
    private readonly Dictionary<string, RectF> _needs = [];
    private IReadOnlyList<RectI> _monitors;
    private RectF _canvas;
    private float _dpi = 1;
    private bool _placed, _swapping, _hidden;
    private Vector3 _rootOffset = new(float.NaN);
    private readonly DispatcherQueue? _ui;
    private bool _scheduled;
    private readonly List<Action> _waiting = [];

    public OverlayStage(OverlayWindow overlay, DesktopWindowTarget target, OverlayWindow spare, DesktopWindowTarget spareTarget,
        ContainerVisual root, IReadOnlyList<RectI> monitors)
    {
        _active = overlay;
        _activeTarget = target;
        _spare = spare;
        _spareTarget = spareTarget;
        _root = root;
        _monitors = monitors;
        _ui = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>The window she is drawn in now.</summary>
    public nint Hwnd => _active.Hwnd;
    public RectI Rect => _active.Rect;
    /// <summary>Window moves and resizes so far (test diagnostics: none at rest); swaps: top-left changes done by the spare.</summary>
    public int Moves { get; private set; }
    public int Resizes { get; private set; }
    public int Swaps { get; private set; }
    public bool Visible => Native.IsWindowVisible(_active.Hwnd) != 0;
    /// <summary>Test mode: the next swap's callback throws (a failure must never freeze the overlay).</summary>
    public bool ThrowOnNextSwap { get; set; }

    /// <summary>Her canvas moved (screen pixels); <paramref name="with"/> is moved in the same batch (her input window).</summary>
    public void Follow(RectF canvas, float dpiScale, WindowMove? with = null)
    {
        _canvas = canvas;
        _dpi = dpiScale;
        _placed = true;
        if (_swapping) { Win.Apply(with); return; }   // the swap re-plans when it is done; her input window still follows
        Apply(with, swap: false);
    }

    /// <summary>She hides (the overlay goes) or comes back.</summary>
    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        Native.ShowWindow(_active.Hwnd, hidden ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
    }

    /// <summary>A feature needs an area drawn (screen pixels) while it runs; null when it ends. The window changes
    /// after this turn's commit, in a turn of its own; <paramref name="settled"/> runs once that change is on screen
    /// (at once when nothing changes), so the feature's visuals never show at a stale window.</summary>
    public void Need(string who, RectF? area, Action? settled = null)
    {
        bool changed;
        if (area is { } a) { changed = !_needs.TryGetValue(who, out var old) || old != a; _needs[who] = a; }
        else changed = _needs.Remove(who);
        if (settled != null) _waiting.Add(settled);
        if (!_placed || _ui is null) { Apply(null, swap: false); RunWaiting(); return; }
        if (!changed && !_scheduled && !_swapping) { RunWaiting(); return; }
        if (_scheduled || _swapping) return;   // the pending change picks this up
        _scheduled = true;
        AfterCommit(() =>
        {
            _scheduled = false;
            if (!Apply(null, swap: true)) RunWaiting();
        });
    }

    /// <summary>The monitors changed (plugged, unplugged, resolution).</summary>
    public void Replan(IReadOnlyList<RectI> monitors)
    {
        _monitors = monitors;
        _active.Refresh();   // Windows may have moved it itself: plan from where it really is
        if (_placed && !_swapping) Apply(null, swap: false);
    }

    public string Describe()
    {
        var r = Rect;
        return $"x={r.Left} y={r.Top} w={r.Width} h={r.Height} moves={Moves} resizes={Resizes} swaps={Swaps} swapping={(_swapping ? "true" : "false")} " +
            $"needs={(_needs.Count == 0 ? "none" : string.Join(',', _needs.Keys))}";
    }

    /// <summary>Places the window and the root; true when a swap to the spare started (it finishes on its own).</summary>
    private bool Apply(WindowMove? with, bool swap)
    {
        var target = OverlayPlan.Plan(_canvas, _dpi, _needs.Values, _monitors);
        var before = _active.Rect;
        bool corner = target.Left != before.Left || target.Top != before.Top;
        if (swap && corner && !_hidden && _ui is not null)
        {
            Swap(target);
            Win.Apply(with);
            return true;
        }
        var move = _active.MoveTo(target);
        Win.Apply(move, with);
        if (move is not null)
        {
            if (target.Width == before.Width && target.Height == before.Height) Moves++;
            else Resizes++;
        }
        // Overlay coordinates stay screen pixels: the root sits at minus the window's top-left.
        var offset = new Vector3(-target.Left, -target.Top, 0);
        if (offset != _rootOffset) _root.Offset = _rootOffset = offset;
        return false;
    }

    /// <summary>The spare goes to <paramref name="target"/> while empty; two commits later (it is on screen there) the
    /// root moves to it with its new offset in one commit; the old window is hidden once that commit is done.</summary>
    private void Swap(RectI target)
    {
        _swapping = true;
        var move = _spare.MoveTo(target);
        Win.Apply(move);
        Native.ShowWindow(_spare.Hwnd, Native.SW_SHOWNOACTIVATE);
        AfterCommit(() => AfterCommit(() => Guarded(() =>
        {
            if (ThrowOnNextSwap) { ThrowOnNextSwap = false; throw new InvalidOperationException("test: a failing swap"); }
            if (_hidden)
            {
                Native.ShowWindow(_spare.Hwnd, Native.SW_HIDE);
                _swapping = false;
                Apply(null, swap: false);
                RunWaiting();
                return;
            }
            _activeTarget.Root = null;
            _spareTarget.Root = _root;
            _root.Offset = _rootOffset = new Vector3(-target.Left, -target.Top, 0);
            (_active, _spare) = (_spare, _active);
            (_activeTarget, _spareTarget) = (_spareTarget, _activeTarget);
            Native.SetWindowText(_active.Hwnd, "Sahar");
            Native.SetWindowText(_spare.Hwnd, "Sahar spare");
            Swaps++;
            AfterCommit(() => Guarded(() =>
            {
                Native.ShowWindow(_spare.Hwnd, Native.SW_HIDE);
                _swapping = false;
                // She may have moved, or a need changed, while the swap ran: catch up (a corner change swaps again).
                if (!Apply(null, swap: true)) RunWaiting();
            }));
        })));
    }

    /// <summary>A swap step that throws must never leave the overlay frozen (review 0.11 #4): the root goes back to
    /// whichever window holds it, the spare hides, the stage moves the window directly, and the waiting features run.</summary>
    private void Guarded(Action step)
    {
        try { step(); }
        catch (Exception e)
        {
            Log.Write($"stage error: swap failed ({e.GetType().Name})");
            try
            {
                if (_spareTarget.Root == _root)
                {
                    (_active, _spare) = (_spare, _active);
                    (_activeTarget, _spareTarget) = (_spareTarget, _activeTarget);
                }
                else if (_activeTarget.Root != _root) _activeTarget.Root = _root;
                Native.ShowWindow(_spare.Hwnd, Native.SW_HIDE);
            }
            catch (Exception) { }
            finally
            {
                _swapping = false;
                try { Apply(null, swap: false); } catch (Exception) { }
                RunWaiting();
            }
        }
    }

    private void AfterCommit(Action then)
    {
        var op = _root.Compositor.RequestCommitAsync();
        op.Completed = (_, _) => _ui!.TryEnqueue(() =>
        {
            try { then(); }
            catch (Exception e) { Log.Write($"stage error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
        });
    }

    private void RunWaiting()
    {
        var list = _waiting.ToArray();
        _waiting.Clear();
        foreach (var w in list)
        {
            try { w(); }
            catch (Exception e) { Log.Write($"stage error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
        }
    }
}
