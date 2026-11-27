using System.Globalization;
using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;
using Windows.System;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Her speech bubble (spec 0.15 §4.1, boards V15Talk / V15TalkDark): Porcelain — the menu colour in light and dark, Rubik
/// 15, radius 18, a rounded tail pointing at her leaf, the card's soft shadow. Above her head (under her when there is no
/// room), inside her screen's work area; it follows her. say: up to 3 lines, it stays 8 s plus 60 ms a character (at most
/// 14 s). long: clamped to 3 lines plus «كمّل بالمحادثة» (<see cref="ContinueChat"/>). A new line replaces the old one
/// with a cross-fade; a click on her or on the bubble sends it away. It never takes the focus (its click window does not
/// activate). Emoji are hers and draw in colour (owner, 2026-11-26).
/// </summary>
internal sealed class SpeechBubble : IDisposable
{
    private readonly SaharVisual _v;
    private readonly Func<RectF> _body;
    private readonly DispatcherQueueTimer _hide;
    private readonly InputWindow _input;
    private readonly Compositor _c;
    private Shown? _now;
    private int _gen;
    private RectF? _need;

    /// <summary>One bubble on screen: its root (enter / exit), three sprites (content, near and far shadow), its surface.</summary>
    private sealed class Shown
    {
        public required ContainerVisual Root;
        public required SpriteVisual[] Sprites;
        public required SwapSurface Surface;
        public required BubbleText Text;
        public required bool Dark;
        public required float Dpi;
        public BubblePlace Place;
        public string? DrawnKey;
    }

    public string? Text { get; private set; }
    public bool Showing => Text != null;
    public BubbleKind? Kind => _now?.Text.Kind;
    /// <summary>A comment ended: it went away by itself (true) or was clicked away (false). Her tool lines
    /// (a reminder, what is playing, a Quran error) are not comments and raise nothing.</summary>
    public event Action<bool>? Ended;
    /// <summary>«كمّل بالمحادثة» on a long bubble: the whole line, for the chat window (plan Task 5 opens it there).</summary>
    public event Action<string>? ContinueChat;
    private bool _comment;

    /// <summary>Light or dark (the card's setting: Appearance, or Windows' apps mode).</summary>
    public Func<bool> Dark { get; set; } = () => false;
    /// <summary>The bubble may take clicks (not while a game has the screen).</summary>
    public Func<bool> Clickable { get; set; } = () => true;
    /// <summary>The overlay grows to an area (screen px) and calls back once it holds it (OverlayStage.Need).</summary>
    public Action<string, RectF?, Action?> Need { get; set; } = (_, _, then) => then?.Invoke();
    /// <summary>How many times he clicked a bubble away (a bubble that timed out is not counted).</summary>
    public int Clicks { get; private set; }

    public SpeechBubble(DispatcherQueue ui, SaharVisual v, Func<RectF> body, bool capturable = false)
    {
        _v = v;
        _body = body;
        _c = v.Root.Compositor;
        _hide = ui.CreateTimer();
        _hide.IsRepeating = false;
        _hide.Tick += (_, _) => Safe(() => End(timedOut: true));
        _input = new InputWindow(capturable, new RectI(0, 0, 1, 1)) { Handler = OnInput };
        _input.SetVisible(false);
    }

    public void Say(string text, bool comment = false)
    {
        int gen = ++_gen;
        bool again = Showing;   // 0.15.0: her streamed words update the bubble in place (no fade per word)
        Leave(fade: !again);
        Text = text;
        _comment = comment;
        bool dark = Dark();
        float dpi = _v.DpiScale;
        var measured = BubblePainter.Measure(text);
        var size = BubblePainter.Size(measured);
        var px = new SizeI((int)MathF.Ceiling(size.Width * dpi) + 2, (int)MathF.Ceiling(size.Height * dpi) + 2);
        var root = _c.CreateContainerVisual();
        root.Opacity = 0;
        var sprites = new SpriteVisual[3];   // far shadow, near shadow, the bubble
        for (int i = 0; i < 3; i++)
        {
            sprites[i] = _c.CreateSpriteVisual();
            root.Children.InsertAtTop(sprites[i]);
        }
        _now = new Shown { Root = root, Sprites = sprites, Surface = new SwapSurface(_v.Host, px), Text = measured, Dark = dark, Dpi = dpi };
        Shadow(sprites[0], dark ? 18 : 14, dark ? 5 : 4, dark ? 0.42f : 0.10f, dpi);
        Shadow(sprites[1], 2, 1, dark ? 0.5f : 0.08f, dpi);
        _v.Host.Root.Children.InsertAtTop(root);
        Place(_now);
        Log.Write($"bubble: {measured.Kind.ToString().ToLowerInvariant()} ({measured.Lines} lines, {text.Length} chars)");   // never the words
        var area = Area(_now);
        if (TagPlace.OutsideFollow(area, Follow())) NeedArea(area, () => { if (gen == _gen) Enter(again); });
        else { NeedArea(null, null); Enter(again); }
        _hide.Stop();
        _hide.Interval = Bubble.Duration(text);
        _hide.Start();
    }

    /// <summary>A click on her while it shows: away it goes.</summary>
    public void Dismiss()
    {
        if (!Showing) return;
        Clicks++;   // review I2: a reply streaming into the bubble stays away once he clicked it
        End(timedOut: false);
    }

    /// <summary>Gone without a word and without counting as his click (she hides for a fullscreen app; a panel or a game
    /// takes her place — review I1): no Ended, no Clicks, so a reply still on its way is not dropped.</summary>
    public void Clear(bool fade = false)
    {
        _gen++;
        _hide.Stop();
        Leave(fade);
        NeedArea(null, null);
    }

    /// <summary>She moved (every frame of a flight): the bubble and its click window follow, whole pixels.</summary>
    public void OnMoved()
    {
        if (_now is null) return;
        Place(_now);
        var area = Area(_now);
        if (TagPlace.OutsideFollow(area, Follow()) && !(_need is { } n && Inside(area, n))) NeedArea(area, null);
    }

    /// <summary>A game started or ended: the click window follows <see cref="Clickable"/>.</summary>
    public void InputChanged()
    {
        if (_now is not null) Place(_now);
    }

    /// <summary>After a device loss: its drawing again.</summary>
    public void Redraw() => _now?.Surface.Redraw();


    private void End(bool timedOut)
    {
        _gen++;
        _hide.Stop();
        Leave(fade: true);
        NeedArea(null, null);
        if (_comment) Ended?.Invoke(timedOut);
    }

    private void Enter(bool instant = false)
    {
        if (_now is not { } s) return;
        var root = s.Root;
        root.CenterPoint = new Vector3(s.Place.TailX, s.Place.Below ? s.Place.Body.Y : s.Place.Body.Bottom, 0);
        if (ExpressionPlayer.Reduced || instant) { root.Opacity = 1; return; }
        // Porcelain motion: opacity, 4 px and scale 0.98 over 220 ms on cubic-bezier(.2,.9,.3,1).
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));
        var fade = _c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        var rise = _c.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, MathF.Round(4 * s.Dpi) * (s.Place.Below ? -1 : 1), 0));
        rise.InsertKeyFrame(1, Vector3.Zero, ease);
        rise.Duration = TimeSpan.FromMilliseconds(220);
        var grow = _c.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0.98f, 0.98f, 1));
        grow.InsertKeyFrame(1, Vector3.One, ease);
        grow.Duration = TimeSpan.FromMilliseconds(220);
        root.Opacity = 1;
        root.StartAnimation("Opacity", fade);
        root.StartAnimation("Offset", rise);
        root.StartAnimation("Scale", grow);
    }

    /// <summary>The bubble on screen goes: a 150 ms fade (or at once), then its visuals and surface are freed.</summary>
    private void Leave(bool fade)
    {
        Text = null;
        _input.SetVisible(false);
        if (_now is not { } s) return;
        _now = null;
        void Free()
        {
            _v.Host.Root.Children.Remove(s.Root);
            foreach (var v in s.Sprites) v.Dispose();
            s.Root.Dispose();
            s.Surface.Dispose();
        }
        if (!fade || ExpressionPlayer.Reduced) { Free(); return; }
        var a = _c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1, 0);
        a.Duration = TimeSpan.FromMilliseconds(150);
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        s.Root.StartAnimation("Opacity", a);
        batch.End();
        batch.Completed += (_, _) => Safe(Free);
    }

    /// <summary>Where it goes now (above her leaf, inside her work area); redraws only when the tail must move along it.</summary>
    private void Place(Shown s)
    {
        float dpi = s.Dpi, scale = _v.Scale;
        var tip = _v.Position + new Vector2(Bubble.TipXUnits, Bubble.TipYUnits) * scale - new Vector2(0, Bubble.GapDip * dpi);
        var b = _body();
        var work = Monitors.At(new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2)).WorkF;
        var box = s.Text.Box;
        var size = new SizeF(MathF.Ceiling(box.Width * dpi), MathF.Ceiling(box.Height * dpi));
        var p = Bubble.Place(tip, work, size, dpi, under: b.Bottom + Bubble.GapDip * dpi);
        s.Place = p;
        float tailDip = MathF.Round((p.TailX - p.Body.X) / dpi * 2) / 2;   // half-DIP steps: a flight never redraws it
        var key = string.Create(CultureInfo.InvariantCulture, $"{tailDip}|{p.Below}");
        if (key != s.DrawnKey)
        {
            var t = Tokens.For(s.Dark);
            var text = s.Text;
            bool up = p.Below;
            int i = s.Surface.Draw(dc =>
            {
                dc.Clear();
                dc.SetTransform(System.Numerics.Matrix3x2.CreateScale(dpi));
                BubblePainter.Draw(dc, text, t, tailDip, up);
            });
            foreach (var v in s.Sprites)
            {
                var brush = s.Surface.Brushes[i];
                brush.Stretch = CompositionStretch.None;
                brush.HorizontalAlignmentRatio = 0;
                brush.VerticalAlignmentRatio = 0;
                v.Brush = brush;
                v.Size = new Vector2(s.Surface.Size.Width, s.Surface.Size.Height);
            }
            s.DrawnKey = key;
        }
        float top = p.Below ? p.Body.Y - Bubble.TailHeight * dpi : p.Body.Y;
        var at = new Vector3(MathF.Round(p.Body.X), MathF.Round(top), 0);
        foreach (var v in s.Sprites) v.Offset = at;
        // The click window: the body (the tail is too small to aim at), never while a game has the screen.
        var r = new RectI((int)at.X, (int)MathF.Round(p.Body.Y), (int)at.X + (int)size.Width, (int)MathF.Round(p.Body.Y) + (int)size.Height);
        Win.Apply(_input.MoveTo(r));
        _input.SetVisible(Showing && Clickable());
    }

    /// <summary>The screen area it draws in, its shadow included.</summary>
    private static RectF Area(Shown s)
    {
        var p = s.Place;
        float d = s.Dpi, tail = Bubble.TailHeight * d;
        var r = new RectF(p.Body.X, p.Below ? p.Body.Y - tail : p.Body.Y, p.Body.Width, p.Body.Height + tail);
        return TagPlace.WithShadow(r, d);
    }

    private RectI Follow() => OverlayPlan.Follow(new RectF(_v.Position.X, _v.Position.Y, _v.CanvasPx.Width, _v.CanvasPx.Height), _v.DpiScale);

    private void NeedArea(RectF? area, Action? then)
    {
        if (area is null && _need is null) { then?.Invoke(); return; }
        _need = area;
        Need("bubble", area, then);
    }

    private static bool Inside(RectF a, RectF b) => a.X >= b.X && a.Y >= b.Y && a.Right <= b.Right && a.Bottom <= b.Bottom;

    private nint? OnInput(nint hwnd, uint msg, nuint wParam, nint lParam)
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
            default:
                return null;
        }
    }

    /// <summary>A click on the bubble (screen px): «كمّل بالمحادثة» opens the chat on this line; anywhere else sends it away.</summary>
    public void ClickAt(Vector2 p)
    {
        if (_now is not { } s || Text is not { } text) return;
        Clicks++;
        if (LinkRect(s) is { } link && link.Contains(p.X, p.Y))
        {
            Log.Write($"bubble: continue in the chat ({text.Length} chars)");
            ContinueChat?.Invoke(text);
            if (ContinueChat is null) Log.Write("bubble: no chat to continue in yet");
        }
        End(timedOut: false);
    }

    private static RectF? LinkRect(Shown s)
    {
        if (s.Text.LinkButton is not { } b) return null;
        var o = s.Place.Body;
        float d = s.Dpi;
        return new RectF(o.X + b.X * d, o.Y + b.Y * d, b.Width * d, b.Height * d);
    }

    /// <summary>Test diagnostics: kind, lines, where (px), theme and its colours — never the words.</summary>
    public string Describe()
    {
        if (_now is not { } s) return "showing=false";
        var p = s.Place;
        var t = Tokens.For(s.Dark);
        var link = LinkRect(s);
        return string.Create(CultureInfo.InvariantCulture,
            $"showing=true kind={s.Text.Kind.ToString().ToLowerInvariant()} lines={s.Text.Lines} rect={(int)MathF.Round(p.Body.X)},{(int)MathF.Round(p.Body.Y)},{(int)p.Body.Width},{(int)p.Body.Height} " +
            $"tailX={(int)MathF.Round(p.TailX)} below={(p.Below ? "true" : "false")} theme={(s.Dark ? "dark" : "light")} surface={Hex(t.Menu)} ink={Hex(t.Ink)} " +
            $"link={(link is { } l ? $"{(int)l.X},{(int)l.Y},{(int)l.Width},{(int)l.Height}" : "none")} input={(Native.IsWindowVisible(_input.Hwnd) != 0 ? "true" : "false")} " +
            $"inputRect={_input.Rect.Left},{_input.Rect.Top},{_input.Rect.Width},{_input.Rect.Height} clipped={(s.Text.Shown.Length < (Text?.Length ?? 0) ? "true" : "false")} clicks={Clicks}");
    }

    private static string Hex(Sahar.Core.Art.Rgba c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    private void Shadow(SpriteVisual v, float blur, float y, float alpha, float dpi)
    {
        var s = _c.CreateDropShadow();
        s.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        s.BlurRadius = blur * dpi;
        s.Offset = new Vector3(0, y * dpi, 0);
        s.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        s.Opacity = alpha;
        v.Shadow = s;
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("bubble", e); }
    }

    public void Dispose()
    {
        _hide.Stop();
        Leave(fade: false);
        Native.DestroyWindow(_input.Hwnd);
    }
}
