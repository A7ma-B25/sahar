using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Geometry;
using Sahar.Core.Life;
using Sahar.Core.Motion;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Motion;
using Sahar.Engine.Render;
using Windows.System;
using Windows.UI.Composition;

namespace Sahar.Engine.Life;

/// <summary>
/// "فرجيني حركاتك": plays everything she can do (Showcase.Steps), one after another, with a caption under her.
/// Stops on a click, a drag, her menu, or when she hides; she goes back to where she was at the end.
/// </summary>
internal sealed class ShowcasePlayer : IDisposable
{
    private static readonly Rgba Ink = Rgba.Parse("#1E2350"), White = Rgba.Parse("#FFFFFF");

    private readonly MotionController _motion;
    private readonly SaharVisual _v;
    private readonly Action<string> _show;
    private readonly Action _backToBrain;
    private readonly DispatcherQueueTimer _next;
    private SpriteVisual? _caption;
    private SwapSurface? _captionSurface;
    private Vector2 _home;
    private bool _waking;

    public bool Running { get; private set; }
    public int Index { get; private set; }
    public event Action<bool>? RunningChanged;

    public ShowcasePlayer(DispatcherQueue ui, MotionController motion, SaharVisual v, Action<string> showExpression, Action backToBrain)
    {
        _motion = motion;
        _v = v;
        _show = showExpression;
        _backToBrain = backToBrain;
        _next = ui.CreateTimer();
        _next.IsRepeating = false;
        _next.Tick += (_, _) =>
        {
            try { Advance(); }
            catch (Exception e) { Log.Write($"show error: {e}"); Stop(); }
        };
    }

    public void Start(int from = 0)
    {
        if (Running) Stop();
        Running = true;
        _home = _motion.TopLeft;
        Index = Math.Clamp(from, 0, Showcase.Steps.Count - 1) - 1;
        Log.Write($"show: start at {from}");
        RunningChanged?.Invoke(true);
        Advance();
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _next.Stop();
        RemoveCaption();
        _backToBrain();
        if (_motion.Current is MotionKind.Goodbye or MotionKind.FullscreenHide or MotionKind.SleepInCorner or MotionKind.NapOnIcon)
            _motion.Play(MotionKind.Wake);
        else _motion.Play(MotionKind.Hover);
        _motion.TopLeft = _home;
        _motion.Reclamp();
        Log.Write("show: stopped");
        RunningChanged?.Invoke(false);
    }

    public string Describe() =>
        $"running={(Running ? "true" : "false")} step={Index + 1}/{Showcase.Steps.Count} id={(Running && Index >= 0 ? Showcase.Steps[Index].Id : "none")}";

    private void Advance()
    {
        if (!Running) return;
        // After a move that leaves her gone or asleep, she wakes before the next one.
        if (!_waking && _motion.Current is MotionKind.Goodbye or MotionKind.FullscreenHide or MotionKind.SleepInCorner or MotionKind.NapOnIcon)
        {
            _waking = true;
            _motion.Play(MotionKind.Wake);
            Schedule(1f);
            return;
        }
        _waking = false;
        Index++;
        if (Index >= Showcase.Steps.Count) { Stop(); return; }
        var step = Showcase.Steps[Index];
        switch (step.Kind)
        {
            case ShowcaseKind.Motion:
                _backToBrain();
                _motion.Play(Enum.Parse<MotionKind>(step.Id));
                break;
            case ShowcaseKind.Flight:
                _backToBrain();
                _motion.PlayFlight(Enum.Parse<FlightPattern>(step.Id), force: true);
                break;
            case ShowcaseKind.Expression:
                if (_motion.Current != MotionKind.Hover) _motion.Play(MotionKind.Hover);
                _show(step.Id);
                break;
        }
        Caption(step.Caption);
        Schedule(step.Seconds);
    }

    private void Schedule(float seconds)
    {
        _next.Interval = TimeSpan.FromSeconds(seconds);
        _next.Start();
    }

    /// <summary>A white pill with the step's name, under her (above her near the bottom of the screen); it follows her.</summary>
    private void Caption(string text)
    {
        var c = _v.Root.Compositor;
        float dpi = _v.DpiScale, w = 260 * dpi, h = 28 * dpi;
        var size = new SizeI((int)MathF.Ceiling(w), (int)MathF.Ceiling(h));
        if (_captionSurface is null || _captionSurface.Size != size)
        {
            RemoveCaption();
            _captionSurface = new SwapSurface(_v.Host, size);
            _caption = c.CreateSpriteVisual();
            _caption.Size = new Vector2(size.Width, size.Height);
            _v.Host.Root.Children.InsertAtTop(_caption);
        }
        _captionSurface.Draw(dc =>
        {
            dc.Clear();
            dc.SetTransform(Matrix3x2.Identity);
            float r = h / 2, textW = MathF.Min(w, (text.Length * 8f + 28) * dpi), left = (w - textW) / 2;
            var pill = dc.Path(PathData.Parse(FormattableString.Invariant(
                $"M{left + r} 0 L{left + textW - r} 0 A{r} {r} 0 0 1 {left + textW - r} {h} L{left + r} {h} A{r} {r} 0 0 1 {left + r} 0 Z")));
            dc.FillGeometry(pill, dc.SolidBrush(new D2DColor(White, 0.92f)));
            dc.Text(text, "Segoe UI", 13 * dpi, 600, new D2DRect { Left = left, Top = 0, Right = left + textW, Bottom = h }, dc.SolidBrush(new D2DColor(Ink)));
        });
        _caption!.Brush = _captionSurface.Brushes[_captionSurface.Current];
        var units = SaharVisual.BodyUnits;
        var body = _motion.Body;
        var work = Senses.Monitors.At(new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2)).WorkF;
        bool above = body.Bottom + h + 12 * dpi > work.Bottom;
        var offset = new Vector2((units.X + units.Width / 2) * _v.Scale - w / 2,
            above ? units.Y * _v.Scale - h - 8 * dpi : (units.Y + units.Height) * _v.Scale + 8 * dpi);
        var follow = c.CreateExpressionAnimation("r.Offset + o");
        follow.SetReferenceParameter("r", _v.Root);
        follow.SetVector3Parameter("o", new Vector3(offset, 0));
        _caption.StartAnimation("Offset", follow);
    }

    private void RemoveCaption()
    {
        if (_caption != null)
        {
            _caption.StopAnimation("Offset");
            _v.Host.Root.Children.Remove(_caption);
            _caption.Dispose();
            _caption = null;
        }
        _captionSurface?.Dispose();
        _captionSurface = null;
    }

    public void Dispose() => RemoveCaption();
}
