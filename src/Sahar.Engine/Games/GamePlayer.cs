using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Games;
using Sahar.Core.Geometry;
using Sahar.Core.Motion;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Motion;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.UI.Composition;

namespace Sahar.Engine.Games;

/// <summary>
/// "العبي" (0.9): four little games with her. While one runs, an invisible catcher takes the mouse, the game's tick
/// moves her each frame, and Esc, a right-click or another window ends it; at the end she says the score.
/// </summary>
internal sealed class GamePlayer : IDisposable
{
    private readonly CompositionHost _host;
    private readonly SaharVisual _v;
    private readonly MotionController _motion;
    private readonly Action<string> _say;
    private readonly Action<string, bool> _express;
    private readonly GameCatcher _catcher;
    private readonly Random _rng = new();
    private readonly List<Visual> _visuals = [];
    private readonly Dictionary<string, SwapSurface> _emoji = [];

    private float _clock, _limit;
    private Func<float, bool>? _tick;
    private Vector2 _pointer;
    private readonly List<(double T, Vector2 P)> _drag = [];

    public GameKind? Kind { get; private set; }
    public bool Running => Kind is not null;
    public int Score { get; private set; }
    public string State { get; private set; } = "idle";

    private readonly Windows.System.DispatcherQueueTimer _watch;

    public GamePlayer(Windows.System.DispatcherQueue ui, CompositionHost host, SaharVisual v, MotionController motion, Action<string> say,
        Action<string, bool> express, bool capturable)
    {
        (_host, _v, _motion, _say, _express) = (host, v, motion, say, express);
        // Something else took her over (hidden for fullscreen, her goodbye, a drag): the game ends quietly.
        _watch = ui.CreateTimer();
        _watch.Interval = TimeSpan.FromMilliseconds(500);
        _watch.Tick += (_, _) => Safe(() => { if (Running && _motion.Current != MotionKind.Game) { Log.Write("game: interrupted"); Stop(sayScore: false); } });
        _watch.Start();
        _catcher = new GameCatcher(capturable)
        {
            Down = p => Safe(() => Down(p)),
            Moved = p => Safe(() => { _pointer = p; if (_ballHeld) _drag.Add((_clock, p)); }),
            Up = p => Safe(() => Up(p)),
            Quit = () => Safe(() => { if (Kind == GameKind.HideSeek) SeekQuit(); else Stop(); }),
            Lost = () => Safe(() => Stop()),
        };
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"game error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
    }

    // The game's screen is fixed when it starts: hiding half off an edge never flips it to a neighbour.
    private MonitorInfo? _gameMonitor;
    private MonitorInfo Monitor => _gameMonitor ?? Monitors.At(new Vector2(_motion.Body.X + _motion.Body.Width / 2, _motion.Body.Y + _motion.Body.Height / 2));
    private static RectF ToF(RectI r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>A game started (true) or ended (false): her own input window steps aside so the game gets every click.</summary>
    public event Action<bool>? RunningChanged;
    private float Dpi => Monitor.DpiScale;
    private RectF Work => Monitor.WorkF;
    private Vector2 Size => _motion.GameBodySize;
    private Vector2 Her => new(_motion.Body.X, _motion.Body.Y);
    private RectF HerRect => _motion.Body;

    /// <param name="seconds">Test mode: a shorter game.</param>
    public void Start(GameKind kind, float? seconds = null)
    {
        if (Running) Stop(sayScore: false);
        _gameMonitor = null;
        _gameMonitor = Monitor;
        Kind = kind;
        RunningChanged?.Invoke(true);
        Score = 0;
        _clock = 0;
        var p = Native.CursorPos();
        _pointer = new Vector2(p.X, p.Y);
        Log.Write($"game: {kind}");
        var m = Monitor.Bounds;
        _catcher.Show(new Vector2((m.Left + m.Right) / 2f, (m.Top + m.Bottom) / 2f));
        _v.Need("game", Work);   // her game is drawn anywhere on its screen's work area
        switch (kind)
        {
            case GameKind.Chase: _limit = seconds ?? Chase.Seconds; StartChase(); break;
            case GameKind.HideSeek: _limit = seconds ?? 20 * 60f; StartHide(); break;
            case GameKind.Ball: _limit = seconds ?? Ball.Seconds; StartBall(); break;
            default: _limit = seconds ?? StarRain.Seconds; StartStars(); break;
        }
        _motion.StartGame(dt =>
        {
            _clock += dt;
            var c = Native.CursorPos();
            if (!_ballHeld) _pointer = new Vector2(c.X, c.Y);
            bool go;
            try { go = _tick?.Invoke(dt) ?? false; }
            catch (Exception e) { Log.Write($"game error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); go = false; }   // never a frozen game holding the mouse
            if (_clock >= _limit) go = false;
            if (!go) { _tick = null; Finish(); }
            return go;
        });
    }

    /// <summary>Ends the game now (Esc, right-click, another window, or the pipe).</summary>
    public void Stop(bool sayScore = true)
    {
        if (!Running) return;
        _tick = null;
        Finish(sayScore);
    }

    private void Finish(bool sayScore = true)
    {
        if (Kind is not { } kind) return;
        Kind = null;
        State = "over";
        _catcher.Hide();
        _v.Need("game", null);
        try { ClearVisuals(); }
        catch (Exception e) { Log.Write($"game cleanup error: {e.GetType().Name}"); }
        _ballHeld = false;
        if (kind == GameKind.HideSeek)
        {
            try { EndSeek(); }
            catch (Exception e) { Log.Write($"game cleanup error: {e.GetType().Name}"); }
        }
        _motion.EndGame();
        _gameMonitor = null;
        RunningChanged?.Invoke(false);
        Log.Write($"game: {kind} over, score {Score}");
        // Hide-and-seek gives its result in its own «منعيدها؟» card; a game stopped early says nothing more.
        if (sayScore && kind != GameKind.HideSeek) _say(Sahar.Core.Settings.Address.ToYou(GameText.Result(kind, Score)));
    }

    private void Down(Vector2 p)
    {
        _pointer = p;
        if (Kind == GameKind.Ball && _ballState == BallState.Ready && Vector2.Distance(p, _ball.Position + BallSize / 2) < 60 * Dpi)
        {
            _ballHeld = true;
            _drag.Clear();
            _drag.Add((_clock, p));
        }
    }

    private void Up(Vector2 p)
    {
        _pointer = p;
        switch (Kind)
        {
            case GameKind.Chase: ChaseClick(p); break;
            case GameKind.HideSeek: HideClick(p); break;
            case GameKind.Ball when _ballHeld:
                _drag.Add((_clock, p));
                Throw(Ball.Flick(_drag));
                break;
        }
    }

    /// <summary>Test mode: a click at a screen point, as the catcher would see it.</summary>
    public void Click(Vector2 p) { Down(p); Up(p); }

    /// <summary>Test mode: a flick of the ball from <paramref name="from"/> to <paramref name="to"/> in 0.1 s.</summary>
    public void Flick(Vector2 from, Vector2 to)
    {
        if (Kind != GameKind.Ball || _ballState != BallState.Ready) return;
        _ballHeld = true;
        _drag.Clear();
        _drag.Add((_clock, from));
        _drag.Add((_clock + 0.1, to));
        Throw((to - from) / 0.1f);
    }

    public string Describe()
    {
        var h = HerRect;
        string extra = Kind switch
        {
            GameKind.Ball => $" ball={_ballState} ballX={_ball.Position.X:0} ballY={_ball.Position.Y:0}",
            GameKind.Stars => $" stars={_rain?.Stars.Count ?? 0} spawned={_rain?.Spawned ?? 0}",
            GameKind.HideSeek => DescribeSeek(),
            _ => "",
        };
        return FormattableString.Invariant(
            $"running={(Running ? "true" : "false")} kind={Kind?.ToString() ?? "none"} score={Score} state={State} x={h.X:0} y={h.Y:0} w={h.Width:0} h={h.Height:0}{extra}");
    }

    // ---------- helpers ----------

    /// <summary>A smooth move toward a spot over <paramref name="seconds"/>; returns true while moving.</summary>
    private Vector2 _from, _to;
    private float _moveT = 1, _moveSecs = 0.25f;

    private void MoveTo(Vector2 to, float seconds)
    {
        _from = Her;
        _to = to;
        _moveT = 0;
        _moveSecs = seconds;
    }

    private bool Moving(float dt)
    {
        if (_moveT >= 1) return false;
        _moveT = MathF.Min(1, _moveT + dt / _moveSecs);
        float e = 1 - (1 - _moveT) * (1 - _moveT);
        _motion.GameMoveTo(Vector2.Lerp(_from, _to, e));
        return _moveT < 1;
    }

    private SwapSurface Emoji(string glyph, float px)
    {
        string key = $"{glyph}@{px:0}";
        if (_emoji.TryGetValue(key, out var s)) return s;
        int size = (int)MathF.Ceiling(px * 1.3f);
        s = new SwapSurface(_host, new SizeI(size, size));
        s.DrawCurrent(dc =>
        {
            dc.Clear();
            dc.Text(glyph, "Segoe UI Emoji", px, 400, new D2DRect { Left = 0, Top = 0, Right = size, Bottom = size },
                dc.SolidBrush(new D2DColor(Rgba.Parse("#1E2350"))));
        });
        _emoji[key] = s;
        return s;
    }

    private SpriteVisual Sprite(string glyph, float px)
    {
        var s = Emoji(glyph, px);
        var v = _host.Compositor.CreateSpriteVisual();
        v.Size = new Vector2(s.Size.Width, s.Size.Height);
        v.Brush = s.Brushes[s.Current];
        _host.Root.Children.InsertAtTop(v);
        _visuals.Add(v);
        return v;
    }

    /// <summary>Puts a sprite's centre at a screen point (overlay coordinates are relative to the overlay's origin).</summary>
    private void Place(Visual v, Vector2 screenCentre) => v.Offset = new Vector3(screenCentre - _v.Origin - v.Size / 2, 0);

    private void Remove(Visual v)
    {
        _host.Root.Children.Remove(v);
        _visuals.Remove(v);
        v.Dispose();
    }

    private void ClearVisuals()
    {
        foreach (var v in _visuals.ToList()) Remove(v);
        _starSprites.Clear();
        _ballSprite = null;
    }

    // ---------- 🏃 الحقني ----------

    private void StartChase()
    {
        State = "chasing";
        _express("poked", true);
        _say(Sahar.Core.Settings.Address.ToYou("الحقني إذا بتقدر! 😆"));
        var stamina = new ChaseStamina(_rng);   // she tires as the game goes on: catchable in about 10–20 s
        _tick = dt =>
        {
            if (Moving(dt)) return true;
            if (stamina.Flee(_clock, Her, _pointer, Size, Work, Dpi) is { } jump) MoveTo(jump.To, jump.Seconds);
            return true;
        };
    }

    private void ChaseClick(Vector2 p)
    {
        if (!Chase.Hit(HerRect, p)) return;
        Score++;
        _express("poked", true);
        _motion.Squish();
        var w = Work;
        MoveTo(new Vector2(w.X + (float)_rng.NextDouble() * (w.Width - Size.X), w.Y + (float)_rng.NextDouble() * (w.Height - Size.Y)), 0.4f);
    }

    // ---------- «الغميضة» (0.16: she really hides behind his windows) ----------

    private enum SeekPhase { Counting, Zipping, Hidden, Out, Ask }

    private SeekScene? _scene;
    private SeekScene Scene => _scene ??= new SeekScene(_host, _v);
    private SeekPhase _phase;
    private float _phaseT, _hiddenT, _checkT, _zipSecs, _wiggleT = -1, _wiggleDeg;
    private double _nextGiggle;
    private int _round;
    private readonly List<bool> _results = [];
    private readonly List<HideSpot> _used = [];
    private HideSpot? _spot;
    private Vector2 _reveal, _zipFrom, _zipCtrl, _zipTo;
    private bool _hinted, _zipShrink, _waitLogged;
    private readonly WhisperGate _gate = new();
    private (Eyes Eyes, Mouth Mouth) _faceBefore;
    private string _said = "";

    /// <summary>Takes her speech bubble away (the count is wordless; the result card replaces the last line).</summary>
    public Action? Hush { get; set; }

    /// <summary>Test mode: the kind of spot she prefers when one is free (the ui harness shows each kind).</summary>
    public SpotKind? SeekPrefer { get; set; }

    private int Found => _results.Count(x => x);

    private void StartHide()
    {
        Sahar.Core.Settings.Appearance? look = null;
        try { look = Sahar.Core.Settings.SettingsStore.Load(Sahar.Core.Settings.SettingsStore.DefaultPath).Appearance; } catch { /* her defaults */ }
        Scene.Dark = look switch { Sahar.Core.Settings.Appearance.Dark => true, Sahar.Core.Settings.Appearance.Light => false, _ => Theme.AppsDark() };
        Scene.Reduced = ReducedMotion.AnimationsOff();
        Scene.Dpi = Dpi;
        _faceBefore = _v.FaceShown;
        _round = 0;
        _results.Clear();
        _used.Clear();
        _said = "";
        StartRound();
        _tick = SeekTick;
    }

    private void StartRound()
    {
        _round++;
        _phase = SeekPhase.Counting;
        _phaseT = 0;
        _spot = null;
        _reveal = Vector2.Zero;
        _waitLogged = false;
        Score = Found;
        State = $"counting-{_round}";
        Scene.Clip(null);
        Scene.CloseAsk();
        Hush?.Invoke();
        // The cover pose, with no words: paws over her eyes, a blush, «٣ ٢ ١» over her head, the pips under her.
        _v.SetFace(Eyes.Closed, Mouth.Smile, ExpressionPlayer.Fade);
        _v.BlushLayer.Container.Opacity = 1f;
        Scene.Paws(true);
        var h = HerRect;
        var head = new Vector2(h.X + h.Width / 2, h.Y - h.Height * 0.45f - 22 * Dpi);
        if (head.Y < Work.Y + 22 * Dpi)   // no room over her head (she is at the top): beside it, on the screen's side
            head = new Vector2(h.Right + 28 * Dpi < Work.Right ? h.Right + 28 * Dpi : h.X - 28 * Dpi, h.Y + 12 * Dpi);
        Scene.Countdown(head);
        Scene.Pips(new Vector2(h.X + h.Width / 2, h.Bottom + 12 * Dpi), _round, _results);
    }

    private bool SeekTick(float dt)
    {
        _phaseT += dt;
        Scene.Step(dt);
        switch (_phase)
        {
            case SeekPhase.Counting:
                if (_phaseT >= 3 * SeekScene.CountSeconds + 0.15f) Hide();
                break;
            case SeekPhase.Zipping:
                Zip();
                break;
            case SeekPhase.Hidden:
                Hidden(dt);
                break;
            case SeekPhase.Out:
                Moving(dt);
                if (_phaseT >= 2f)
                {
                    if (_round >= HideSeek.Rounds) AskAgain();
                    else StartRound();
                }
                break;
            case SeekPhase.Ask:
                Moving(dt);
                if (_phaseT >= 20f) return false;   // no answer: the game ends quietly
                break;
        }
        Wiggle(dt);
        return true;
    }

    private void Hide()
    {
        var world = SeekScene.World(Monitor, Size);
        var spots = HideSeek.Spots(world, _pointer, _rng, _used);
        if (SeekPrefer is { } prefer) spots = [.. spots.Where(x => x.Kind == prefer), .. spots.Where(x => x.Kind != prefer)];
        if (spots.Count == 0)
        {
            // Nowhere far enough from the pointer (or a full-screen app): she keeps her eyes covered and looks again.
            if (!_waitLogged) Log.Write("game: hide waits (no spot)");
            _waitLogged = true;
            _phaseT = 3 * SeekScene.CountSeconds - 0.35f;
            return;
        }
        var spot = spots[0];
        _spot = spot;
        _used.Add(spot);
        Log.Write($"game: hide spot {spot.Kind} (of {spots.Count})");   // kinds and counts only: never a window's title
        Scene.Paws(false);
        Scene.Pips(null, _round, _results);
        _v.BlushLayer.Container.Opacity = SaharVisual.BlushRest;
        State = $"hiding-{_round}";
        StartZip(spot.TopLeft, trail: true);   // under reduced motion she lands (and is "hidden") at once
    }

    /// <summary>She shrinks with a squash and zips along a curve to <paramref name="to"/> (reduced motion: she is there).</summary>
    private void StartZip(Vector2 to, bool trail)
    {
        _phase = SeekPhase.Zipping;
        _phaseT = 0;
        Scene.Clip(null);
        _zipFrom = Her;
        _zipTo = to;
        _zipCtrl = SeekScene.Curve(_zipFrom, _zipTo, _rng);
        _zipShrink = trail;
        _zipSecs = Scene.Reduced ? 0 : trail ? 0.75f : 0.35f;
        var root = _v.Root;
        root.StopAnimation("Scale");
        root.CenterPoint = new Vector3((SaharVisual.BodyUnits.X + SaharVisual.BodyUnits.Width / 2) * _v.Scale,
            (SaharVisual.BodyUnits.Y + SaharVisual.BodyUnits.Height / 2) * _v.Scale, 0);
        if (trail && !Scene.Reduced) Scene.Trail(_zipFrom + Size / 2, _zipCtrl + Size / 2, _zipTo + Size / 2, _zipSecs);
        Zip();
    }

    private void Zip()
    {
        float u = _zipSecs <= 0 ? 1 : MathF.Min(1, _phaseT / _zipSecs);
        float e = u < 0.5f ? 2 * u * u : 1 - MathF.Pow(-2 * u + 2, 2) / 2;
        _motion.GameMoveTo(SeekScene.Bezier(_zipFrom, _zipCtrl, _zipTo, e));
        if (_zipShrink && _zipSecs > 0)
        {
            // Squash (0–15 %), small in flight, back to size as she lands (85–100 %).
            float k = u < 0.15f ? 1 - 0.45f * u / 0.15f : u > 0.85f ? 0.55f + 0.45f * (u - 0.85f) / 0.15f : 0.55f;
            float squash = u < 0.15f ? 0.15f * MathF.Sin(MathF.PI * u / 0.15f) : 0;
            _v.Root.Scale = new Vector3(k * (1 + squash), k * (1 - squash), 1);
        }
        if (_spot is { } s && u > 0.85f) Scene.Clip(SeekScene.Keep(s));   // her landing is already behind the edge
        if (u < 1) return;
        _v.Root.Scale = Vector3.One;
        if (_spot is { } spot) Land(spot, fresh: _zipShrink);
    }

    /// <param name="fresh">A new hide (false: she slid on from a spot that went; the round's clock and cues go on).</param>
    private void Land(HideSpot s, bool fresh)
    {
        _phase = SeekPhase.Hidden;
        _phaseT = 0;
        _checkT = 0;
        _reveal = Vector2.Zero;
        if (fresh)
        {
            _hiddenT = 0;
            _hinted = false;
            _gate.Reset();
            _nextGiggle = HideSeek.NextGiggle(_rng);
        }
        Scene.Clip(SeekScene.Keep(s));
        // The peek pose: one eye round the edge.
        var o = Outward(s);
        _v.SetFace(Eyes.Look, Mouth.Smile, ExpressionPlayer.Fade);
        _v.FaceGroup.Offset = s.Kind == SpotKind.Taskbar ? Vector3.Zero : new Vector3(MathF.Sign(o.X) * 3 * _v.Scale, 0, 0);
        State = $"hidden-{_round}";
    }

    /// <summary>The way out of hiding: past the window's edge, up from the bar, or in from the screen's edge.</summary>
    private static Vector2 Outward(HideSpot s) => s.Kind switch
    {
        SpotKind.Window => new Vector2(s.Side == 0 ? -1 : 1, 0),
        SpotKind.Taskbar => new Vector2(0, -1),
        _ => s.Side switch { 0 => new Vector2(1, 0), 1 => new Vector2(0, 1), 2 => new Vector2(-1, 0), _ => new Vector2(0, -1) },
    };

    /// <summary>What shows of her with her top-left at <paramref name="at"/> (screen px).</summary>
    private RectF VisibleAt(HideSpot s, Vector2 at)
    {
        var body = new RectF(at.X, at.Y, Size.X, Size.Y);
        return s.Kind switch
        {
            SpotKind.Window => body.Intersect(SeekScene.Keep(s)!.Value).Intersect(Work),
            SpotKind.Taskbar => new RectF(at.X + Size.X * 0.3f, at.Y - Size.Y * 0.38f, Size.X * 0.4f, MathF.Max(0, s.Behind.Y - (at.Y - Size.Y * 0.38f))),
            _ => body.Intersect(Work),
        };
    }

    private HideSpot Shown => _spot is { } s ? s with { Visible = VisibleAt(s, s.TopLeft + _reveal) } : default;

    private void Hidden(float dt)
    {
        if (_spot is not { } spot) return;
        _hiddenT += dt;
        // The window she is behind moved, closed or got covered: she slides to the nearest edge.
        _checkT += dt;
        if (_checkT >= 0.25f)
        {
            _checkT = 0;
            var world = SeekScene.World(Monitor, Size);
            if (!HideSeek.StillThere(spot, world))
            {
                var next = HideSeek.Fallback(world, _pointer, Her, _used);
                if (next is null) { Log.Write("game: spot gone, nowhere to go"); PopOut(found: false); return; }
                Log.Write($"game: spot gone ({spot.Kind}), sliding to an edge");
                _spot = next;
                _used.Add(next.Value);
                _v.FaceGroup.Offset = Vector3.Zero;
                State = $"moving-{_round}";
                StartZip(next.Value.TopLeft, trail: false);
                return;
            }
        }
        var shown = Shown;
        float dist = HideSeek.DistanceDip(shown, _pointer, Dpi);
        // Within 120 DIP she trembles from the giggle (±1.5 px).
        var shake = !Scene.Reduced && HideSeek.Trembles(dist) ? new Vector2(MathF.Sin(_hiddenT * MathF.Tau * 14) * 1.5f * Dpi, 0) : Vector2.Zero;
        _motion.GameMoveTo(spot.TopLeft + _reveal + shake);
        Scene.Clip(SeekScene.Keep(spot));
        // A giggle every 8 ± 2 s, with a leaf wiggle.
        if (_hiddenT >= _nextGiggle)
        {
            _nextGiggle = _hiddenT + HideSeek.NextGiggle(_rng);
            var v = shown.Visible;
            var at = spot.Kind == SpotKind.Taskbar ? new Vector2(v.Right + 16 * Dpi, v.Y + 8 * Dpi) : new Vector2(v.X + v.Width / 2, v.Y - 14 * Dpi);
            Scene.Giggle(new Vector2(Math.Clamp(at.X, Work.X + 20 * Dpi, Work.Right - 20 * Dpi), Math.Clamp(at.Y, Work.Y + 12 * Dpi, Work.Bottom - 12 * Dpi)));
            StartWiggle(10);
        }
        // After 20 s: a stronger wiggle, and a bit more of her shows.
        if (!_hinted && HideSeek.Hint(_hiddenT))
        {
            _hinted = true;
            _reveal = Outward(spot) * (spot.Kind == SpotKind.Taskbar ? Size.Y : Size.X) * 0.12f;
            StartWiggle(18);
        }
        // Warmer or colder, when the pointer rests.
        if (_gate.Update(_hiddenT, _pointer, Dpi)) Scene.Whisper(HideSeek.Warmth(dist), _pointer, Work);
        if (HideSeek.GiveUp(_hiddenT)) PopOut(found: false);
    }

    private void StartWiggle(float deg) => (_wiggleT, _wiggleDeg) = (0, deg);

    /// <summary>The leaf: a decaying wiggle after a giggle; behind the taskbar it also sways (1.6 s).</summary>
    private void Wiggle(float dt)
    {
        if (Scene.Reduced) return;
        float deg = 0;
        if (_wiggleT >= 0)
        {
            _wiggleT += dt;
            if (_wiggleT > 0.7f) _wiggleT = -1;
            else deg += _wiggleDeg * (1 - _wiggleT / 0.7f) * MathF.Sin(_wiggleT * MathF.Tau * 6);
        }
        if (_phase == SeekPhase.Hidden && _spot is { Kind: SpotKind.Taskbar }) deg += 6 * MathF.Sin(_hiddenT * MathF.Tau / 1.6f);
        _v.Leaf.Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, deg * MathF.PI / 180);
    }

    private void HideClick(Vector2 p)
    {
        switch (_phase)
        {
            case SeekPhase.Hidden when HideSeek.Hit(Shown, p, Dpi):
                PopOut(found: true);
                break;
            case SeekPhase.Ask when Scene.AgainButton.Contains(p.X, p.Y):
                Log.Write("game: hide again");
                Scene.CloseAsk();
                _round = 0;
                _results.Clear();
                _used.Clear();
                StartRound();
                break;
            case SeekPhase.Ask when Scene.DoneButton.Contains(p.X, p.Y):
                Stop(sayScore: false);
                break;
        }
    }

    /// <summary>Esc or a right-click: while she hides it gives up the round (a second one quits); otherwise it quits.</summary>
    private void SeekQuit()
    {
        if (_phase is SeekPhase.Hidden or SeekPhase.Zipping && _spot is not null) PopOut(found: false);
        else Stop(sayScore: false);
    }

    private void PopOut(bool found)
    {
        if (_spot is not { } spot) return;
        var shown = Shown;
        var from = spot.TopLeft + _reveal;
        _phase = SeekPhase.Out;
        _phaseT = 0;
        _results.Add(found);
        Score = Found;
        Scene.Clip(null);
        _v.Root.Scale = Vector3.One;
        _v.FaceGroup.Offset = Vector3.Zero;
        // Out past the edge, fully seen, inside the work area (room above for her leaf).
        var o = Outward(spot);
        var to = from + o * new Vector2(Size.X * (spot.Kind == SpotKind.Window ? 1 : 0.85f) + 12 * Dpi, Size.Y + 24 * Dpi);
        if (spot.Kind == SpotKind.Window) to.Y = from.Y;
        if (spot.Kind == SpotKind.Taskbar) to = new Vector2(from.X, spot.Behind.Y - Size.Y - 24 * Dpi);
        to = new Vector2(Math.Clamp(to.X, Work.X, Work.Right - Size.X), Math.Clamp(to.Y, Work.Y + Size.Y * 0.45f, Work.Bottom - Size.Y));
        MoveTo(to, Scene.Reduced ? 0.01f : 0.35f);
        if (!Scene.Reduced) _motion.Squish();   // the squash-and-stretch hop
        var centre = to + Size / 2;
        if (found)
        {
            _v.SetFace(Eyes.Happy, Mouth.Grin, ExpressionPlayer.Fade);
            _v.BlushLayer.Container.Opacity = 1f;
            Scene.Burst(centre, Size.X / 2);
            Say(HideSeekText.Found(_hiddenT));
            State = $"found-{_round}";
        }
        else
        {
            _v.SetFace(Eyes.Wink, Mouth.Tongue, ExpressionPlayer.Fade);   // the tease
            Scene.Spot(new RectF(from.X, from.Y - (spot.Kind == SpotKind.Taskbar ? Size.Y * 0.38f : 0), Size.X, Size.Y)
                .Intersect(spot.Kind == SpotKind.Taskbar ? new RectF(Work.X, Work.Y, Work.Width, spot.Behind.Y - Work.Y + 1) : Work));
            Say(HideSeekText.GaveUp);
            State = $"gaveup-{_round}";
        }
        Scene.Pips(new Vector2(centre.X, to.Y + Size.Y + 12 * Dpi), _round, _results);
        Log.Write($"game: hide round {_round} {(found ? "found" : "gave up")} ({shown.Kind})");
        _spot = null;
    }

    private void AskAgain()
    {
        _phase = SeekPhase.Ask;
        _phaseT = 0;
        State = "again";
        _v.SetFace(Eyes.Happy, Mouth.Smile, ExpressionPlayer.Fade);
        _v.BlushLayer.Container.Opacity = SaharVisual.BlushRest;
        var line = Sahar.Core.Settings.Address.ToYou(HideSeekText.End(Found));
        _said = line;
        Hush?.Invoke();
        // She floats to the middle of the screen, with room above her for the card.
        var w = Work;
        var to = new Vector2(w.X + w.Width / 2 - Size.X / 2, w.Y + w.Height * 0.58f - Size.Y / 2);
        MoveTo(to, Scene.Reduced ? 0.01f : 0.5f);
        var her = new RectF(to.X, to.Y, Size.X, Size.Y);
        Scene.Ask(line, her, w);
        Scene.Pips(new Vector2(her.X + her.Width / 2, her.Bottom + 12 * Dpi), HideSeek.Rounds + 1, _results);
        Log.Write($"game: hide over, found {Found} of {HideSeek.Rounds}");
    }

    private void Say(string line)
    {
        _said = Sahar.Core.Settings.Address.ToYou(line);
        _say(_said);
    }

    /// <summary>The game is over: every piece goes, and her face, blush and leaf are hers again.</summary>
    private void EndSeek()
    {
        _scene?.Clear();
        _v.Leaf.Orientation = Quaternion.Identity;
        _v.BlushLayer.Container.Opacity = SaharVisual.BlushRest;
        _v.FaceGroup.Offset = Vector3.Zero;
        _v.SetFace(_faceBefore.Eyes, _faceBefore.Mouth, ExpressionPlayer.Fade);
        _spot = null;
    }

    private string DescribeSeek()
    {
        var s = Shown;
        string spot = _spot is { } sp
            ? FormattableString.Invariant($" spot={sp.Kind} side={sp.Side} vx={s.Visible.X:0} vy={s.Visible.Y:0} vw={s.Visible.Width:0} vh={s.Visible.Height:0} bx={sp.Behind.X:0} by={sp.Behind.Y:0} bw={sp.Behind.Width:0} bh={sp.Behind.Height:0}")
            : " spot=none";
        string ask = _scene?.AskShown == true
            ? FormattableString.Invariant($" againX={Scene.AgainButton.X + Scene.AgainButton.Width / 2:0} againY={Scene.AgainButton.Y + Scene.AgainButton.Height / 2:0} doneX={Scene.DoneButton.X + Scene.DoneButton.Width / 2:0} doneY={Scene.DoneButton.Y + Scene.DoneButton.Height / 2:0}")
            : "";
        return FormattableString.Invariant(
            $" round={_round} found={Found} phase={_phase}{spot} clip={(_scene?.ClipOn == true ? "on" : "off")} whisper={_scene?.LastWhisper?.ToString() ?? "none"} hidden={_hiddenT:0.0}{ask} said64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_said))}");
    }

    // ---------- ⚽ الطابة ----------

    private enum BallState { Ready, Flying, Fetching, Returning }
    private BallState _ballState;
    private Body2D _ball;
    private bool _ballHeld;
    private SpriteVisual? _ballSprite;
    private Vector2 BallSize => new(30 * Dpi, 30 * Dpi);

    private void StartBall()
    {
        State = "ball";
        _ballSprite = Sprite("⚽", 26 * Dpi);
        DropBallNear(_pointer);
        _say(Sahar.Core.Settings.Address.ToYou("اشلف الطابة وأنا بجيبلك ياها! ⚽"));
        _tick = dt =>
        {
            Moving(dt);
            switch (_ballState)
            {
                case BallState.Ready when _ballHeld:
                    _ball = new Body2D(_pointer - BallSize / 2, Vector2.Zero);
                    break;
                case BallState.Ready:
                case BallState.Flying:
                    var (next, rest) = Ball.Step(_ball, dt, Work, BallSize);
                    _ball = next;
                    if (_ballState == BallState.Flying && rest)
                    {
                        _ballState = BallState.Fetching;
                        MoveTo(_ball.Position + BallSize / 2 - new Vector2(Size.X / 2, Size.Y * 0.9f), 0.9f);
                    }
                    break;
                case BallState.Fetching when _moveT >= 1:
                    // Picked up: she carries it back to you.
                    _ballState = BallState.Returning;
                    _express("poked", true);
                    var near = _pointer + new Vector2(_pointer.X > Work.X + Work.Width / 2 ? -140 : 60, -80) * Dpi;
                    MoveTo(new Vector2(Math.Clamp(near.X, Work.X, Work.Right - Size.X), Math.Clamp(near.Y, Work.Y, Work.Bottom - Size.Y)), 1.1f);
                    break;
                case BallState.Returning:
                    _ball = new Body2D(Her + new Vector2(Size.X / 2, -4 * Dpi) - BallSize / 2, Vector2.Zero);
                    if (_moveT >= 1)
                    {
                        Score++;
                        State = $"fetched-{Score}";
                        _ballState = BallState.Ready;   // she lets it drop near you
                        if (Score >= Ball.Fetches) return false;
                    }
                    break;
            }
            if (_ballSprite != null) Place(_ballSprite, _ball.Position + BallSize / 2);
            return true;
        };
    }

    private void DropBallNear(Vector2 p)
    {
        _ball = new Body2D(new Vector2(Math.Clamp(p.X, Work.X, Work.Right - BallSize.X), Math.Clamp(p.Y - 120 * Dpi, Work.Y, Work.Bottom - BallSize.Y)), Vector2.Zero);
        _ballState = BallState.Ready;
    }

    private void Throw(Vector2 velocity)
    {
        _ballHeld = false;
        if (velocity.Length() < 150) velocity = new Vector2(_rng.Next(2) == 0 ? -600 : 600, -500);   // a gentle drop still goes somewhere
        if (velocity.Length() > 3500) velocity = Vector2.Normalize(velocity) * 3500;
        _ball = _ball with { Velocity = velocity };
        _ballState = BallState.Flying;
        State = "flying";
    }

    // ---------- ⭐ لقط النجوم ----------

    private StarRain? _rain;
    private readonly Dictionary<int, SpriteVisual> _starSprites = [];

    private void StartStars()
    {
        State = "stars";
        var w = Work;
        _rain = new StarRain(w, _rng, Dpi);
        _say("يلا نلقط النجوم! ⭐");
        _tick = dt =>
        {
            // She follows the pointer left and right along the bottom of the screen.
            float y = w.Bottom - Size.Y - 12 * Dpi;
            float x = Math.Clamp(_pointer.X - Size.X / 2, w.X, w.Right - Size.X);
            var her = Her;
            _motion.GameMoveTo(new Vector2(her.X + (x - her.X) * MathF.Min(1, dt * 9), y));
            _rain.Step(dt);
            int got = _rain.Catch(HerRect);
            if (got > 0) { Score += got; _motion.Squish(); }
            var alive = _rain.Stars.Select(s => s.Id).ToHashSet();
            foreach (var id in _starSprites.Keys.Where(id => !alive.Contains(id)).ToList()) { Remove(_starSprites[id]); _starSprites.Remove(id); }
            foreach (var s in _rain.Stars)
            {
                if (!_starSprites.TryGetValue(s.Id, out var sprite)) _starSprites[s.Id] = sprite = Sprite("⭐", 22 * Dpi);
                Place(sprite, s.Position);
            }
            return true;
        };
    }

    public void Dispose()
    {
        _watch.Stop();
        ClearVisuals();
        _scene?.Dispose();
        foreach (var s in _emoji.Values) s.Dispose();
        _emoji.Clear();
    }
}
