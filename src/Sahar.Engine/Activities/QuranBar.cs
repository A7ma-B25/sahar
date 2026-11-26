using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Quran;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Menu;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Sahar.Engine.Tools;
using Windows.System;
using Windows.UI.Composition;
using Rules = Sahar.Core.Quran.QuranBar;
using Layout = Sahar.Core.Quran.QuranBarLayout;

namespace Sahar.Engine.Activities;

/// <summary>
/// The Quran on her tag, and the bar it grows into (0.16 Task 8; spec §10.3; boards V15Quran / V15QuranDark; owner
/// ruling 7). While a recitation is on, the tag reads «الكهف ١٢:٠٤»; a click on it (or Space on the open bar's keys)
/// opens the bar under her on the same thread: 340 wide, radius 20, the menu colour and the card shadow, popping in
/// (220 ms) and fading out (150 ms; reduced motion: none). Row 1 the book, the surah and the reciter (+ «واقفة» /
/// «عم تفتح…»), × stops and closes; row 2 the times and a track that fills from the right; row 3, right to left: the
/// surah before · back 10 · play/pause · forward 10 · the surah after. Failed: her line and «جرّب كمان مرة» / «سكّر».
/// It folds back into the tag after 6 s with no pointer over it, on Esc, or a click outside. Composition + D2D, no new
/// visible window: an invisible catcher (as the menu's) takes the clicks and keys only while it is open. The rules are
/// Core's (<see cref="Rules"/>); logs say kinds and seconds, never titles.
/// </summary>
internal sealed class QuranBar : IDisposable
{
    private const string TagId = "quran";
    private const float ThreadDip = 12;

    private readonly QuranTool _quran;
    private readonly ActivityHub _hub;
    private readonly SaharVisual _sahar;
    private readonly Func<RectF> _body;
    private readonly CompositionHost _host;
    private readonly Compositor _c;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _bar, _shadowNear, _shadowFar;
    private readonly ShapeVisual _thread;
    private readonly CompositionLineGeometry _threadLine;
    private readonly CompositionSpriteShape _threadShape;
    private readonly MenuCatcher _catcher;
    private readonly DispatcherQueueTimer _tagTick, _barTick;
    private SwapSurface? _surface;
    private float _dpi = 1;
    private RectF _rect;           // the bar on screen (px)
    private string? _key;
    private int _gen;
    private BarButton? _hover;
    private bool _dragging, _pointerOver;
    private TimeSpan? _preview;
    private DateTimeOffset _lastActivity;

    public Action<string, RectF?, Action?> Need { get; set; } = (_, _, then) => then?.Invoke();
    public Func<bool> Dark { get; set; } = () => false;
    public bool IsOpen { get; private set; }
    public bool Shown { get; private set; }
    public event Action<bool>? OpenChanged;
    /// <summary>Drawings so far (test diagnostics).</summary>
    public int Drawn { get; private set; }

    public QuranBar(DispatcherQueue ui, CompositionHost host, bool capturable, QuranTool quran, ActivityHub hub, SaharVisual sahar, Func<RectF> body)
    {
        (_quran, _hub, _sahar, _body, _host) = (quran, hub, sahar, body, host);
        _c = host.Compositor;
        _root = _c.CreateContainerVisual();
        _root.IsVisible = false;
        _shadowFar = _c.CreateSpriteVisual();
        _shadowNear = _c.CreateSpriteVisual();
        _bar = _c.CreateSpriteVisual();
        foreach (var v in new[] { _shadowFar, _shadowNear, _bar }) _root.Children.InsertAtTop(v);
        _threadLine = _c.CreateLineGeometry();
        _threadShape = _c.CreateSpriteShape(_threadLine);
        _threadShape.StrokeStartCap = _threadShape.StrokeEndCap = CompositionStrokeCap.Round;
        _thread = _c.CreateShapeVisual();
        _thread.Shapes.Add(_threadShape);
        _thread.IsVisible = false;
        host.Root.Children.InsertAtTop(_thread);
        host.Root.Children.InsertAtTop(_root);
        _catcher = new MenuCatcher(capturable)
        {
            Clicked = p => Safe(() => Click(p)),
            Down = p => Safe(() => Down(p)),
            Moved = p => Safe(() => Move(p)),
            Dismissed = () => Safe(Close),
            Deactivated = () => Safe(Close),
            RawKey = (vk, ctrl) => Run(() => Key(vk, ctrl)),
        };
        // The tag: «الكهف ١٢:٠٤» once a second while a recitation is on.
        _tagTick = ui.CreateTimer();
        _tagTick.Interval = TimeSpan.FromSeconds(1);
        _tagTick.Tick += (_, _) => Safe(Tag);
        _barTick = ui.CreateTimer();
        _barTick.Interval = TimeSpan.FromMilliseconds(250);
        _barTick.Tick += (_, _) => Safe(BarTick);
        quran.Changed += () => Safe(Changed);
        quran.PlayingChanged += _ => Safe(Changed);
        hub.TagClicked += a => Safe(() => { if (a.Kind == ActivityKind.Quran) Open(); });
        // The look stream's surah picker shows «وقفت عند ١٢:٠٤» on its resume row; the place is kept here.
        quran.ResumeMeta = quran.ResumeLine;
    }

    // ---- the tag ----

    private void Changed()
    {
        Tag();
        if (_quran.Playing) _tagTick.Start(); else _tagTick.Stop();
        if (!IsOpen) return;
        // Stopped or heard to the end: the bar goes with it; a failure stays to offer «جرّب كمان مرة».
        if (!_quran.Playing && _quran.Recitation != RecitationState.Failed) { Close(); return; }
        Draw();
    }

    private void Tag()
    {
        if (!_quran.Playing) { _hub.Remove(TagId); return; }
        var v = _quran.View;
        string meta = v.State == RecitationState.Opening ? "…" : Rules.Clock(v.Position);
        _hub.Set(new Activity(TagId, ActivityKind.Quran, Quran.SurahName(v.Surah), meta, "book"));
    }

    // ---- open / close ----

    public void Open()
    {
        if (IsOpen || !(_quran.Playing || _quran.Recitation == RecitationState.Failed)) return;
        IsOpen = true;
        var body = _body();
        var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        var monitor = Monitors.At(centre);
        _dpi = monitor.DpiScale;
        _hover = null;
        _dragging = false;
        _preview = null;
        _lastActivity = DateTimeOffset.Now;
        Place(body, monitor.WorkF);
        Draw();
        _catcher.Show(centre);
        int gen = ++_gen;
        Need("quran-bar", Area(), () => { if (IsOpen && gen == _gen) Enter(); });
        _barTick.Start();
        Log.Write($"quran: bar open ({_quran.Recitation})");
        OpenChanged?.Invoke(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _barTick.Stop();
        _catcher.Hide();
        _thread.IsVisible = false;
        int gen = ++_gen;
        Leave(() => { if (!IsOpen && gen == _gen) Need("quran-bar", null, null); });
        Log.Write("quran: bar closed");
        OpenChanged?.Invoke(false);
    }

    /// <summary>Under her on a 12 DIP thread when there is room, else beside her (the tag's rule); inside the work area.</summary>
    private void Place(RectF body, RectF work)
    {
        var size = new Vector2(MathF.Ceiling(Layout.Width * _dpi), MathF.Ceiling(Layout.Height(_quran.Recitation) * _dpi));
        _rect = TagPlace.Place(body, work, size, _dpi, out var side, ThreadDip);
        _root.Offset = new Vector3(_rect.X, _rect.Y, 0);
        _root.CenterPoint = new Vector3(_rect.Width / 2, 0, 0);
        if (side == TagSide.Below)
        {
            float x = Math.Clamp(MathF.Round(body.X + body.Width / 2), _rect.X + 24 * _dpi, _rect.Right - 24 * _dpi);
            _thread.Offset = Vector3.Zero;
            _thread.Size = new Vector2(_rect.Right + 8, _rect.Bottom);
            _threadLine.Start = new Vector2(x, body.Bottom - 2 * _dpi);
            _threadLine.End = new Vector2(x, _rect.Y + 1);
            _threadShape.StrokeThickness = 1.4f * _dpi;
            var b = Tokens.For(Dark()).Boundary;
            _threadShape.StrokeBrush = _c.CreateColorBrush(Windows.UI.Color.FromArgb((byte)(b.A * 0.7f), b.R, b.G, b.B));
            _threadOn = true;
        }
        else _threadOn = false;
    }

    private bool _threadOn;

    /// <summary>The bar and its shadow (px), what the overlay must hold; the thread is under her, inside her own area.</summary>
    private RectF Area()
    {
        float s = 40 * _dpi, below = 56 * _dpi;
        return new RectF(_rect.X - s, _rect.Y - s, _rect.Width + 2 * s, _rect.Height + s + below);
    }

    private void Enter()
    {
        Shown = true;
        _root.IsVisible = true;
        _thread.IsVisible = _threadOn;
        _root.StopAnimation("Opacity");
        _root.StopAnimation("Scale");
        if (ExpressionPlayer.Reduced) { _root.Opacity = 1; _root.Scale = Vector3.One; return; }
        // V15 pop: opacity, 4 DIP and scale .98 → 1 over 220 ms on (.2,.9,.3,1).
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));
        var fade = _c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        var scale = _c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(0.98f, 0.98f, 1));
        scale.InsertKeyFrame(1, Vector3.One, ease);
        scale.Duration = TimeSpan.FromMilliseconds(220);
        var drop = _c.CreateVector3KeyFrameAnimation();
        drop.InsertKeyFrame(0, new Vector3(_rect.X, _rect.Y + MathF.Round(4 * _dpi), 0));
        drop.InsertKeyFrame(1, new Vector3(_rect.X, _rect.Y, 0), ease);
        drop.Duration = TimeSpan.FromMilliseconds(220);
        _root.StartAnimation("Opacity", fade);
        _root.StartAnimation("Scale", scale);
        _root.StartAnimation("Offset", drop);
    }

    private void Leave(Action done)
    {
        if (!Shown) { done(); return; }
        Shown = false;
        if (ExpressionPlayer.Reduced) { _root.IsVisible = false; done(); return; }
        int gen = _gen;
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = _c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        _root.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (gen != _gen || IsOpen) return;
            _root.IsVisible = false;
            done();
        };
    }

    // ---- input ----

    private Vector2 Local(Vector2 p) => new((p.X - _rect.X) / _dpi, (p.Y - _rect.Y) / _dpi);

    private BarButton? HitAt(Vector2 p)
    {
        var l = Local(p);
        var hit = Layout.Hit(Layout.Buttons(_quran.Recitation), l.X, l.Y);
        return hit is { } b && Rules.Enabled(b, _quran.View) ? b : null;
    }

    private void Down(Vector2 p)
    {
        Touch();
        if (HitAt(p) != BarButton.Track || _quran.Duration is not { } d) return;
        _dragging = true;
        _preview = Rules.FromTrack(Local(p).X, Layout.Track, d);
        Draw();
    }

    private void Move(Vector2 p)
    {
        bool over = _rect.Contains(p.X, p.Y);
        if (over) Touch();
        _pointerOver = over;
        if (_dragging && _quran.Duration is { } d)
        {
            _preview = Rules.FromTrack(Local(p).X, Layout.Track, d);
            Draw();
            return;
        }
        var hover = HitAt(p);
        if (hover == _hover) return;
        _hover = hover;
        Draw();
    }

    private void Click(Vector2 p)
    {
        if (_dragging)
        {
            _dragging = false;
            if (_preview is { } at) _quran.SeekTo(at);
            _preview = null;
            Draw();
            return;
        }
        if (!_rect.Contains(p.X, p.Y)) { Close(); return; }
        if (HitAt(p) is { } b) Press(b, Local(p));
    }

    /// <summary>A target pressed (a click, a key, or the test pipe).</summary>
    public void Press(BarButton b, Vector2? at = null)
    {
        Touch();
        if (!Rules.Enabled(b, _quran.View)) return;
        Log.Write($"quran: bar {b}");
        switch (b)
        {
            case BarButton.Close: _quran.Stop(); Close(); return;
            case BarButton.Dismiss: Close(); return;
            case BarButton.Toggle: _quran.Toggle(); break;
            case BarButton.Back10: _quran.Seek(-Rules.Step); break;
            case BarButton.Forward10: _quran.Seek(Rules.Step); break;
            case BarButton.PrevSurah: _quran.Prev(); break;
            case BarButton.NextSurah: _quran.Next(); break;
            case BarButton.Retry: _quran.Retry(); Relayout(); break;
            case BarButton.Track when at is { } l && _quran.Duration is { } d: _quran.SeekTo(Rules.FromTrack(l.X, Layout.Track, d)); break;
        }
        Draw();
    }

    private bool Key(int vk, bool ctrl)
    {
        if (!IsOpen || Rules.Key(vk, ctrl) is not { } k) return false;
        Touch();
        switch (k)
        {
            case BarKey.Collapse: Close(); break;
            case BarKey.Toggle: Press(BarButton.Toggle); break;
            case BarKey.Back10: Press(BarButton.Back10); break;
            case BarKey.Forward10: Press(BarButton.Forward10); break;
            case BarKey.PrevSurah: Press(BarButton.PrevSurah); break;
            case BarKey.NextSurah: Press(BarButton.NextSurah); break;
        }
        return true;
    }

    private void Touch() => _lastActivity = DateTimeOffset.Now;

    private void BarTick()
    {
        if (!IsOpen) return;
        // While it opens he is waiting for it (and a failure needs the bar for «جرّب كمان مرة»): no idle fold then.
        if (_quran.Recitation == RecitationState.Opening) Touch();
        if (!_dragging && Rules.Collapse(_lastActivity, DateTimeOffset.Now, _pointerOver)) { Log.Write("quran: bar folded (idle)"); Close(); return; }
        Draw();
    }

    /// <summary>The state changed size (failed ↔ the controls): placed again.</summary>
    private void Relayout()
    {
        if (!IsOpen) return;
        var body = _body();
        _root.StopAnimation("Offset");   // first: stopping would put back the animation's value over the new place
        Place(body, Monitors.At(new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2)).WorkF);
        Need("quran-bar", Area(), null);
    }

    // ---- drawing ----

    private void Draw()
    {
        if (!IsOpen) return;
        var v = _quran.View;
        if (v.State != RecitationState.Failed && v.State is not (RecitationState.Playing or RecitationState.Paused or RecitationState.Opening)) return;
        float h = Layout.Height(v.State);
        if (MathF.Abs(h * _dpi - _rect.Height) > 0.5f) Relayout();
        bool dark = Dark();
        var t = Tokens.For(dark);
        var at = _preview ?? v.Position;
        var key = string.Join('|', v.Surah, v.Reciter, v.State, Rules.Clock(at), Rules.Clock(v.Duration), MathF.Round(Rules.Fill(at, v.Duration) * Layout.Track.Width * _dpi),
            v.CanSeek, _hover, dark, _dpi);
        Ensure();
        if (key != _key)
        {
            var scale = Matrix3x2.CreateScale(_dpi);
            var view = v with { Position = at };
            var hover = _hover;
            int i = _surface!.Draw(dc => { dc.Clear(); dc.SetTransform(scale); Paint(dc, view, t, hover); });
            foreach (var s in new[] { _bar, _shadowNear, _shadowFar }) s.Brush = Brush(_surface.Brushes[i]);
            _key = key;
            Drawn++;
        }
        var size = new Vector2(_surface!.Size.Width, _surface.Size.Height);
        foreach (var s in new[] { _bar, _shadowNear, _shadowFar }) s.Size = size;
        // The card's shadow (e1): light 0 1 2 .08 + 0 8 24 .12; dark 0 1 2 .45 + 0 12 32 .5.
        Shadow(_shadowNear, 2, 1, dark ? 0.45f : 0.08f);
        Shadow(_shadowFar, dark ? 32 : 24, dark ? 12 : 8, dark ? 0.5f : 0.12f);
    }

    /// <summary>The bar at (0,0) in DIP (V15QuranBar).</summary>
    private static void Paint(D2DContext dc, QuranBarView v, ThemeTokens t, BarButton? hover)
    {
        const float W = Layout.Width, P = Layout.Pad;
        float h = Layout.Height(v.State);
        var B = (Rgba c, float a) => dc.SolidBrush(new D2DColor(c, a));
        dc.FillRoundedRect(D(new RectF(0, 0, W, h)), Layout.Radius, B(t.Menu, 1));
        var buttons = Layout.Buttons(v.State);

        // Row 1: the book badge (tint), the surah (15/600) and the reciter (12, t2) with the state; × at the end.
        var badge = Layout.Badge38;
        dc.FillRoundedRect(D(badge), 11, B(t.AccentSoft, 1));
        Icon(dc, "book", badge, 20, B(t.AccentInk, 1));
        float textR = badge.X - 10, textL = P + Layout.CloseSize + 10;
        dc.TextLayout(Fit(Rules.Title(v), 15, 600, textR - textL), 15, 600, D(new RectF(textL, P, textR - textL, 20)), B(t.Ink, 1), TextAlign.Start, true);
        dc.TextLayout(Fit(Rules.Subtitle(v), 12, 400, textR - textL), 12, 400, D(new RectF(textL, P + 20, textR - textL, 18)), B(t.Text2, 1), TextAlign.Start, true);
        var close = buttons[BarButton.Close];
        if (hover == BarButton.Close) dc.FillRoundedRect(D(close), 10, B(t.Hover, 1));
        Icon(dc, "close", close, 18, B(t.Text2, 1));

        if (v.State == RecitationState.Failed)
        {
            dc.TextLayout(Rules.FailedLine, 14, 400, D(new RectF(P + 2, Layout.Row2Y + 2, W - 2 * P - 4, 22)), B(t.Warn, 1), TextAlign.Start, true);
            var retry = buttons[BarButton.Retry];
            dc.FillRoundedRect(D(retry), 10, B(t.Accent, 1));
            dc.TextLayout(Rules.RetryLabel, 14, 500, D(retry), B(t.OnAccent, 1), TextAlign.Centre, true);
            var dismiss = buttons[BarButton.Dismiss];
            dc.FillRoundedRect(D(dismiss), 10, B(hover == BarButton.Dismiss ? t.Pressed : t.Sunken, 1));
            dc.TextLayout(Rules.DismissLabel, 14, 500, D(dismiss), B(t.Ink, 1), TextAlign.Centre, true);
            return;
        }

        // Row 2: elapsed on the right, the length on the left; the track fills from the right, with its knob.
        bool live = v.State is RecitationState.Playing or RecitationState.Paused;
        dc.TextLayout(live ? Rules.Clock(v.Position) : "--:--", 12, 400, D(Layout.Elapsed), B(t.Text2, 1), TextAlign.Start, true);
        dc.TextLayout(live ? Rules.Clock(v.Duration) : "--:--", 12, 400, D(Layout.Total), B(t.Text3, 1), TextAlign.End, true);
        var track = Layout.Track;
        float cy = track.Y + track.Height / 2;
        dc.FillRoundedRect(D(new RectF(track.X, cy - 2, track.Width, 4)), 2, B(t.Sunken, 1));
        float fill = live ? Rules.Fill(v.Position, v.Duration) : 0;
        if (fill > 0) dc.FillRoundedRect(D(new RectF(track.Right - track.Width * fill, cy - 2, track.Width * fill, 4)), 2, B(t.Accent, 1));
        if (live && v.Duration is not null)
        {
            float kx = Rules.KnobX(fill, track);
            dc.FillEllipse(new Vector2(kx, cy + 1), new Vector2(7), dc.SolidBrush(new D2DColor(new Rgba(0, 0, 0, 64))));
            dc.FillEllipse(new Vector2(kx, cy), new Vector2(7), B(v.CanSeek ? t.Accent : t.Boundary, 1));
        }

        // Row 3, right to left: the surah before · back 10 · play/pause · forward 10 · the surah after.
        foreach (var (b, icon) in new[] { (BarButton.PrevSurah, "prevSurah"), (BarButton.Back10, "back10"), (BarButton.Forward10, "forward10"), (BarButton.NextSurah, "nextSurah") })
        {
            var r = buttons[b];
            bool on = Rules.Enabled(b, v);
            float a = on ? 1 : 0.38f;
            if (on && hover == b) dc.FillRoundedRect(D(r), 12, B(t.Hover, 1));
            bool ten = b is BarButton.Back10 or BarButton.Forward10;
            if (!ten) { Icon(dc, icon, r, 20, B(t.Ink, a)); continue; }
            Icon(dc, icon, new RectF(r.X, r.Y + 4.5f, r.Width, 20), 20, B(t.Ink, a));
            dc.TextLayout("١٠", 12, 400, D(new RectF(r.X, r.Y + 23.5f, r.Width, 13)), B(t.Text2, a), TextAlign.Centre, true);
        }
        var play = buttons[BarButton.Toggle];
        float pa = Rules.Enabled(BarButton.Toggle, v) ? 1 : 0.38f;
        dc.FillEllipse(new Vector2(play.X + play.Width / 2, play.Y + play.Height / 2), new Vector2(play.Width / 2), B(t.Accent, pa));
        Icon(dc, v.State == RecitationState.Playing ? "pause" : "playLeft", play, 22, B(t.OnAccent, pa));
    }

    private static void Icon(D2DContext dc, string key, RectF box, float size, nint brush)
    {
        if (!QuranIcons.All.TryGetValue(key, out var icon)) return;
        float k = size / MenuIcons.Grid;
        dc.PushTransform(new Matrix3x2(k, 0, 0, k, box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2));
        var round = dc.StrokeStyle("round");
        foreach (var path in icon.Paths) dc.DrawGeometry(dc.Path(PathData.Parse(path)), brush, MenuIcons.Stroke, round);
        dc.PopTransform();
    }

    private static string Fit(string text, float size, int weight, float max) => ActivityPainter.Fit(text, size, weight, max);

    private static D2DRect D(RectF r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };

    private void Ensure()
    {
        var size = new SizeI((int)MathF.Ceiling(Layout.Width * _dpi) + 1, (int)MathF.Ceiling(Layout.Height(RecitationState.Playing) * _dpi) + 1);
        if (_surface is null) { _surface = new SwapSurface(_host, size); _key = null; }
        else if (_surface.Size != size) { _surface.Resize(size); _key = null; }
    }

    private static CompositionSurfaceBrush Brush(CompositionSurfaceBrush b)
    {
        b.Stretch = CompositionStretch.None;
        b.HorizontalAlignmentRatio = 0;
        b.VerticalAlignmentRatio = 0;
        return b;
    }

    private void Shadow(SpriteVisual v, float blur, float y, float alpha)
    {
        var s = v.Shadow as DropShadow ?? _c.CreateDropShadow();
        s.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        s.BlurRadius = blur * _dpi;
        s.Offset = new Vector3(0, y * _dpi, 0);
        s.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        s.Opacity = alpha;
        v.Shadow = s;
    }

    public void DeviceLost()
    {
        _key = null;
        _surface?.Redraw();
    }

    // ---- tests ----

    /// <summary>"open=… shown=… state=… bar=x,y,w,h buttons=id:cx,cy;…" (screen px).</summary>
    public string Describe()
    {
        string B(bool b) => b ? "true" : "false";
        var v = _quran.View;
        var buttons = string.Join(';', Layout.Buttons(v.State).Select(p =>
            FormattableString.Invariant($"{p.Key}:{_rect.X + (p.Value.X + p.Value.Width / 2) * _dpi:0},{_rect.Y + (p.Value.Y + p.Value.Height / 2) * _dpi:0}:{(Rules.Enabled(p.Key, v) ? "on" : "off")}")));
        return FormattableString.Invariant(
            $"open={B(IsOpen)} shown={B(Shown)} state={v.State} surah={v.Surah} bar={_rect.X:0},{_rect.Y:0},{_rect.Width:0},{_rect.Height:0} thread={B(_thread.IsVisible)} hover={_hover?.ToString() ?? "none"} drawn={Drawn} fill={Rules.Fill(v.Position, v.Duration):0.000} buttons={buttons}");
    }

    /// <summary>The pipe's quran-bar command: open, close, press id, key vk (ctrl), hover id.</summary>
    public string Command(IReadOnlyDictionary<string, string>? args)
    {
        args ??= new Dictionary<string, string>();
        string op = args.GetValueOrDefault("op") ?? "";
        switch (op)
        {
            case "open": Open(); break;
            case "close": Close(); break;
            case "press" when Enum.TryParse<BarButton>(args.GetValueOrDefault("id"), true, out var b):
                Vector2? at = null;
                if (b == BarButton.Track && float.TryParse(args.GetValueOrDefault("fill"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    at = new Vector2(Layout.Track.Right - Layout.Track.Width * f, Layout.Track.Y + Layout.Track.Height / 2);
                Press(b, at);
                break;
            case "key" when int.TryParse(args.GetValueOrDefault("vk"), out var vk):
                Key(vk, args.GetValueOrDefault("ctrl") == "true");
                break;
            case "hover":
                _hover = Enum.TryParse<BarButton>(args.GetValueOrDefault("id"), true, out var hb) ? hb : null;
                Touch();
                Draw();
                break;
            case "idle":   // as if the pointer left 6 s ago
                _lastActivity = DateTimeOffset.Now - Rules.IdleCollapse;
                _pointerOver = false;
                break;
        }
        return Describe();
    }

    private static bool Run(Func<bool> f)
    {
        try { return f(); }
        catch (Exception e) { Log.Error("quran bar", e); return false; }
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("quran bar", e); }
    }

    public void Dispose()
    {
        _tagTick.Stop();
        _barTick.Stop();
        _surface?.Dispose();
        _root.Dispose();
        _thread.Dispose();
        Native.DestroyWindow(_catcher.Hwnd);
    }
}
