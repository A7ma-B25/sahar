using System.Numerics;
using Sahar.Core.Art;
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
            Quit = () => Safe(() => Stop()),
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
            case GameKind.HideSeek: _limit = seconds ?? 3 * 60f; StartHide(); break;
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
        _motion.EndGame();
        _gameMonitor = null;
        RunningChanged?.Invoke(false);
        Log.Write($"game: {kind} over, score {Score}");
        if (sayScore) _say(Sahar.Core.Settings.Address.ToYou(GameText.Result(kind, Score)));
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
            GameKind.HideSeek => $" round={_round}",
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

    // ---------- 🙈 غمّضلي ----------

    private int _round;
    private float _roundClock;
    private readonly List<float> _found = [];
    private bool _hidden, _hinted;

    private void StartHide()
    {
        _round = 0;
        _found.Clear();
        NextRound();
        _tick = dt =>
        {
            Moving(dt);
            _roundClock += dt;
            if (_round > HideSeek.Rounds) return false;   // the last round is over: no extra hide
            if (!_hidden)
            {
                if (_roundClock >= 1.5f) Hide();
                return true;
            }
            if (!_hinted && HideSeek.Hint(_roundClock)) { _hinted = true; _motion.Squish(); _express("notification", true); }
            if (HideSeek.GiveUp(_roundClock)) { PopOut(found: false); }
            return _round <= HideSeek.Rounds;
        };
    }

    private void NextRound()
    {
        _round++;
        _roundClock = 0;
        _hidden = false;
        _hinted = false;
        if (_round > HideSeek.Rounds) return;
        State = $"counting-{_round}";
        _say(Sahar.Core.Settings.Address.ToYou("غمّض عيونك… 🙈"));
    }

    private void Hide()
    {
        var m = Monitor;
        var (_, barEdge, _) = Taskbar.Get(m.Handle);
        int taskbar = barEdge switch { Edge.Left => 0, Edge.Top => 1, Edge.Right => 2, _ => 3 };
        var edges = HideSeek.OpenEdges(ToF(m.Bounds), Monitors.All().Select(x => ToF(x.Bounds)).ToList(), taskbar);
        if (edges.Count == 0) edges = [taskbar == 1 ? 3 : 1];   // boxed in by screens: a peek over the top (or bottom)
        var spot = HideSeek.Spot(ToF(m.Bounds), Size, edges[_rng.Next(edges.Count)], (float)_rng.NextDouble());
        _motion.GameMoveTo(spot);
        _hidden = true;
        _roundClock = 0;
        State = $"hidden-{_round}";
    }

    private void HideClick(Vector2 p)
    {
        if (!_hidden || !Chase.Hit(HerRect.Intersect(ToF(Monitor.Bounds)), p)) return;
        PopOut(found: true);
    }

    private void PopOut(bool found)
    {
        _hidden = false;
        if (found) _found.Add(_roundClock);
        Score = _found.Count == 0 ? 0 : Math.Max(1, (int)MathF.Ceiling(_found.Average()));   // a find under a second is still "1 s"
        var w = Work;
        MoveTo(new Vector2(w.X + w.Width / 2 - Size.X / 2, w.Y + w.Height / 2 - Size.Y / 2), 0.5f);
        _say(Sahar.Core.Settings.Address.ToYou(found ? "لقيتني! 😆" : "هون كنت! 😜"));
        _express(found ? "poked" : "celebrate", true);
        _roundClock = -2f;   // a short pause before the next round's counting
        State = found ? $"found-{_round}" : $"gaveup-{_round}";
        if (_round >= HideSeek.Rounds) { _round++; return; }
        NextRoundSoon();
    }

    private void NextRoundSoon()
    {
        // The tick counts _roundClock up from −2: the next round starts at 0.
        var tick = _tick;
        _tick = dt =>
        {
            Moving(dt);
            _roundClock += dt;
            if (_roundClock < 0) return true;
            _tick = tick;
            NextRound();
            return true;
        };
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
        foreach (var s in _emoji.Values) s.Dispose();
        _emoji.Clear();
    }
}
