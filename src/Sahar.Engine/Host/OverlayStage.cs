using System.Numerics;
using Sahar.Core.Geometry;
using Windows.UI.Composition;

namespace Sahar.Engine.Host;

/// <summary>
/// Keeps the overlay window on her (<see cref="OverlayPlan"/>): her canvas plus margins, grown to what a feature needs
/// while it runs. Every visual keeps overlay coordinates (= screen pixels): the composition root is shifted by the
/// window's top-left, so moving the window never moves anything on screen. While she flies, the window moves with
/// her (position only, batched with her input window); at rest nothing happens.
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

    public OverlayStage(OverlayWindow overlay, ContainerVisual root, IReadOnlyList<RectI> monitors)
    {
        _overlay = overlay;
        _root = root;
        _monitors = monitors;
    }

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

    /// <summary>A feature needs an area drawn (screen pixels) while it runs; null when it ends.</summary>
    public void Need(string who, RectF? area)
    {
        if (area is { } a) _needs[who] = a;
        else if (!_needs.Remove(who)) return;
        if (_placed) Apply(null);
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

    private void Apply(WindowMove? with)
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
    }
}
