using System.Numerics;
using Sahar.Core.Brain;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Windows.System;
using Windows.UI.Composition;

namespace Sahar.Engine.Tools;

/// <summary>
/// Her focus timer (spec §7.1): a ring of time around her that empties on the compositor; when it ends the
/// "timer done" face for a minute (a poke dismisses it), and a small celebration after a long focus.
/// Ends on the wall clock: checked at least once a minute, and on resume and unlock.
/// </summary>
internal sealed class TimerTool
{
    private static readonly Windows.UI.Color Track = Windows.UI.Color.FromArgb(70, 0xB9, 0xB0, 0xFF),
        Fill = Windows.UI.Color.FromArgb(255, 0xB9, 0xB0, 0xFF);

    private readonly FocusTimer _timer = new();
    private readonly DispatcherQueueTimer _check;
    private readonly SaharVisual _v;
    private readonly Director _director;
    private ShapeVisual? _ring;

    public TimerTool(DispatcherQueue ui, SaharVisual v, Director director)
    {
        _v = v;
        _director = director;
        _check = ui.CreateTimer();
        _check.IsRepeating = false;
        _check.Tick += (_, _) =>
        {
            try { Check(); }
            catch (Exception e) { Log.Write($"timer error: {e}"); }
        };
    }

    public bool Running => _timer.Running;
    /// <summary>Time left (null when no timer runs).</summary>
    public TimeSpan? Remaining => _timer.Running ? _timer.Remaining(DateTimeOffset.Now) : null;

    public void Start(TimeSpan length)
    {
        _timer.Start(length, DateTimeOffset.Now);
        Log.Write(FormattableString.Invariant($"timer: {length.TotalMinutes:0.#} min"));
        Schedule();
        DrawRing();
    }

    public void Cancel()
    {
        if (!_timer.Running) return;
        _timer.Cancel();
        _check.Stop();
        RemoveRing();
        Log.Write("timer: stopped");
    }

    /// <summary>Ends it if its time has passed (also after sleep); otherwise waits for the next check.</summary>
    public void Check()
    {
        var now = DateTimeOffset.Now;
        if (_timer.TakeDone(now))
        {
            _check.Stop();
            RemoveRing();
            Log.Write("timer: done");
            _director.Post(EventKind.TimerDone, TimeSpan.FromSeconds(60));
            if (_timer.IsLong) _director.Post(EventKind.Celebrate, TimeSpan.FromSeconds(70));
            return;
        }
        if (_timer.Running) Schedule();
    }

    /// <summary>After a DPI change or device loss.</summary>
    public void Redraw()
    {
        if (_timer.Running) DrawRing();
    }

    public string Describe() =>
        FormattableString.Invariant($"running={(_timer.Running ? "true" : "false")} remaining={_timer.Remaining(DateTimeOffset.Now).TotalSeconds:0}");

    private void Schedule()
    {
        var left = _timer.Remaining(DateTimeOffset.Now);
        _check.Stop();
        _check.Interval = (left < TimeSpan.FromMinutes(1) ? left : TimeSpan.FromMinutes(1)) + TimeSpan.FromMilliseconds(50);
        _check.Start();
    }

    private void DrawRing()
    {
        RemoveRing();
        var c = _v.Root.Compositor;
        var units = SaharVisual.BodyUnits;
        var centre = new Vector2(units.X + units.Width / 2, units.Y + units.Height / 2) * _v.Scale;
        float radius = units.Width / 2 * _v.Scale + 10 * _v.DpiScale, stroke = 3 * _v.DpiScale;
        _ring = c.CreateShapeVisual();
        _ring.Size = _v.Root.Size;
        _ring.Shapes.Add(Circle(c, centre, radius, stroke, Track, 1f));
        var fill = Circle(c, centre, radius, stroke, Fill, Fraction());
        _ring.Shapes.Add(fill);
        _v.Root.Children.InsertAtBottom(_ring);

        var left = _timer.Remaining(DateTimeOffset.Now);
        if (left > TimeSpan.Zero)
        {
            var empty = c.CreateScalarKeyFrameAnimation();
            empty.InsertKeyFrame(0f, Fraction());
            empty.InsertKeyFrame(1f, 0f, c.CreateLinearEasingFunction());
            empty.Duration = left;
            ((CompositionEllipseGeometry)fill.Geometry).StartAnimation("TrimEnd", empty);
        }
    }

    private float Fraction() =>
        _timer.Length <= TimeSpan.Zero ? 0 : (float)(_timer.Remaining(DateTimeOffset.Now) / _timer.Length);

    private static CompositionSpriteShape Circle(Compositor c, Vector2 centre, float radius, float stroke, Windows.UI.Color color, float trimEnd)
    {
        var g = c.CreateEllipseGeometry();
        g.Center = centre;
        g.Radius = new Vector2(radius);
        g.TrimEnd = trimEnd;
        var s = c.CreateSpriteShape(g);
        s.StrokeBrush = c.CreateColorBrush(color);
        s.StrokeThickness = stroke;
        s.StrokeStartCap = CompositionStrokeCap.Round;
        s.StrokeEndCap = CompositionStrokeCap.Round;
        s.CenterPoint = centre;
        s.RotationAngleInDegrees = -90;   // from the top, clockwise
        return s;
    }

    private void RemoveRing()
    {
        if (_ring is null) return;
        _v.Root.Children.Remove(_ring);
        _ring.Dispose();
        _ring = null;
    }
}
