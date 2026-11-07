using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Motion;

namespace Sahar.Core.Games;

/// <summary>The games "العبي" offers (0.9). Positions are top-left corners in screen pixels; distances scale by DPI.</summary>
public enum GameKind { Chase, HideSeek, Ball, Stars }

/// <summary>🏃 الحقني: she jumps away when the pointer comes near; a click on her catches her.</summary>
public static class Chase
{
    public const float NearDip = 140f, JumpDip = 230f, Seconds = 45f;

    /// <summary>Where she jumps to, or null when the pointer is not near.</summary>
    public static Vector2? Flee(Vector2 her, Vector2 pointer, Vector2 body, RectF work, float dpi, Random rng)
    {
        var centre = her + body / 2;
        var away = centre - pointer;
        if (away.Length() > NearDip * dpi) return null;
        var dir = away.Length() < 1 ? Vector2.UnitX : Vector2.Normalize(away);
        // A little sideways wobble so she isn't predictable.
        float a = (float)(rng.NextDouble() - 0.5) * 0.9f;
        dir = Vector2.Normalize(new Vector2(dir.X * MathF.Cos(a) - dir.Y * MathF.Sin(a), dir.X * MathF.Sin(a) + dir.Y * MathF.Cos(a)));
        var to = Clamp(her + dir * JumpDip * dpi, body, work);
        if (Vector2.Distance(to, her) >= 80 * dpi) return to;
        // Cornered: a dash across to the far side of the screen.
        var far = new Vector2(her.X + body.X / 2 < work.X + work.Width / 2 ? work.Right - body.X : work.X,
            her.Y + body.Y / 2 < work.Y + work.Height / 2 ? work.Bottom - body.Y : work.Y);
        return Clamp(Vector2.Lerp(her, far, 0.75f), body, work);
    }

    public static bool Hit(RectF her, Vector2 click) => her.Contains(click.X, click.Y);

    private static Vector2 Clamp(Vector2 p, Vector2 body, RectF work) =>
        new(Math.Clamp(p.X, work.X, MathF.Max(work.X, work.Right - body.X)), Math.Clamp(p.Y, work.Y, MathF.Max(work.Y, work.Bottom - body.Y)));
}

/// <summary>🙈 غمّضلي: she hides at a screen edge with about a quarter of her showing; a hint at 20 s; she gives up at 60 s.</summary>
public static class HideSeek
{
    public const int Rounds = 3;
    public const double HintSeconds = 20, GiveUpSeconds = 60;
    private const float Shown = 0.27f;

    /// <param name="edge">0 left, 1 top, 2 right, 3 bottom.</param>
    /// <param name="along">0–1 along that edge.</param>
    public static Vector2 Spot(RectF work, Vector2 body, int edge, float along)
    {
        along = Math.Clamp(along, 0.08f, 0.92f);
        float x = work.X + (work.Width - body.X) * along, y = work.Y + (work.Height - body.Y) * along;
        return (edge & 3) switch
        {
            0 => new Vector2(work.X - body.X * (1 - Shown), y),
            1 => new Vector2(x, work.Y - body.Y * (1 - Shown)),
            2 => new Vector2(work.Right - body.X * Shown, y),
            _ => new Vector2(x, work.Bottom - body.Y * Shown),
        };
    }

    /// <summary>Edges she can hide past: no other screen beyond them (her hidden part would show there) and not the
    /// taskbar's edge (0 left, 1 top, 2 right, 3 bottom; −1 no taskbar on this screen).</summary>
    public static List<int> OpenEdges(RectF monitor, IReadOnlyList<RectF> all, int taskbarEdge)
    {
        var open = new List<int>();
        for (int edge = 0; edge < 4; edge++)
        {
            if (edge == taskbarEdge) continue;
            float cx = monitor.X + monitor.Width / 2, cy = monitor.Y + monitor.Height / 2;
            var (px, py) = edge switch
            {
                0 => (monitor.X - 20, cy), 1 => (cx, monitor.Y - 20), 2 => (monitor.Right + 20, cy), _ => (cx, monitor.Bottom + 20),
            };
            if (!all.Any(m => m.Contains(px, py))) open.Add(edge);
        }
        return open;
    }

    public static bool Hint(double seconds) => seconds >= HintSeconds;
    public static bool GiveUp(double seconds) => seconds >= GiveUpSeconds;
}

/// <summary>⚽ الطابة: a ball with gravity that bounces off the screen's edges and rolls to a stop.</summary>
public static class Ball
{
    public const float Gravity = 1800f, Bounce = 0.6f, Roll = 1.6f, RestSpeed = 30f;
    public const int Fetches = 5;
    public const float Seconds = 75f;

    public static (Body2D next, bool resting) Step(Body2D b, float dt, RectF bounds, Vector2 size)
    {
        var v = b.Velocity + new Vector2(0, Gravity * dt);
        var p = b.Position + v * dt;
        float maxX = bounds.Right - size.X, maxY = bounds.Bottom - size.Y;
        if (p.X < bounds.X) { p.X = bounds.X; v.X = MathF.Abs(v.X) * Bounce; }
        if (p.X > maxX) { p.X = maxX; v.X = -MathF.Abs(v.X) * Bounce; }
        if (p.Y < bounds.Y) { p.Y = bounds.Y; v.Y = MathF.Abs(v.Y) * Bounce; }
        bool floor = false;
        if (p.Y >= maxY)
        {
            p.Y = maxY;
            floor = true;
            v.Y = -MathF.Abs(v.Y) * Bounce;
            if (MathF.Abs(v.Y) < 120) v.Y = 0;          // stops bouncing
            v.X *= MathF.Exp(-Roll * dt);               // rolls to a stop
        }
        bool resting = floor && v.Y == 0 && MathF.Abs(v.X) < RestSpeed;
        return (new Body2D(p, resting ? Vector2.Zero : v), resting);
    }

    /// <summary>The flick's velocity from the last 0.1 s of the pointer (px/s).</summary>
    public static Vector2 Flick(IReadOnlyList<(double T, Vector2 P)> samples)
    {
        if (samples.Count < 2) return Vector2.Zero;
        var (t1, p1) = samples[^1];
        var first = samples.FirstOrDefault(s => t1 - s.T <= 0.1 + 1e-9);
        var (t0, p0) = first;
        if (t1 - t0 < 1e-3) (t0, p0) = samples[^2];
        return t1 - t0 < 1e-3 ? Vector2.Zero : (p1 - p0) / (float)(t1 - t0);
    }
}

public sealed record FallingStar(int Id, Vector2 Position, float Speed);

/// <summary>⭐ لقط النجوم: stars fall from the top; she catches the ones that touch her body.</summary>
public sealed class StarRain(RectF work, Random rng, float speedScale = 1f)
{
    public const float Seconds = 40f, EverySeconds = 0.8f;
    private readonly List<FallingStar> _stars = [];
    private float _sinceSpawn = EverySeconds;

    public IReadOnlyList<FallingStar> Stars => _stars;
    public int Spawned { get; private set; }
    public int Caught { get; private set; }
    public int Missed { get; private set; }

    public void Step(float dt)
    {
        _sinceSpawn += dt;
        if (_sinceSpawn >= EverySeconds)
        {
            _sinceSpawn = 0;
            float x = work.X + 30 + (float)rng.NextDouble() * (work.Width - 60);
            _stars.Add(new FallingStar(Spawned, new Vector2(x, work.Y - 30), (160 + (float)rng.NextDouble() * 140) * speedScale));
            Spawned++;
        }
        for (int i = _stars.Count - 1; i >= 0; i--)
        {
            var s = _stars[i] with { Position = _stars[i].Position + new Vector2(0, _stars[i].Speed * dt) };
            if (s.Position.Y >= work.Bottom) { _stars.RemoveAt(i); Missed++; }
            else _stars[i] = s;
        }
    }

    /// <summary>Stars touching her (a little margin): removed and counted.</summary>
    public int Catch(RectF her)
    {
        var reach = new RectF(her.X - 12, her.Y - 12, her.Width + 24, her.Height + 24);
        int n = _stars.RemoveAll(s => reach.Contains(s.Position.X, s.Position.Y));
        Caught += n;
        return n;
    }
}

/// <summary>What she says at the end of a game (written to a man; Address turns it for a woman).</summary>
public static class GameText
{
    public static string Result(GameKind kind, int score) => kind switch
    {
        GameKind.Chase => score switch { 0 => "ولا مرة مسكتني 😜", 1 => "مسكتني مرة وحدة! 🏆", 2 => "مسكتني مرتين! 🏆", _ => $"مسكتني {score} مرات! 🏆" },
        GameKind.HideSeek => score <= 0 ? "ما لقيتني 😜" : $"لقيتني بـ {score} ثانية بالمعدل 🏆",
        GameKind.Ball => score switch { 0 => "ما جبنا الطابة ولا مرة ⚽", 1 => "جبتلك الطابة مرة وحدة ⚽", 2 => "جبتلك الطابة مرتين ⚽", _ => $"جبتلك الطابة {score} مرات ⚽" },
        _ => score == 0 ? "ولا نجمة 😅" : score == 1 ? "لقطنا نجمة وحدة ⭐" : $"لقطنا {score} نجمة ⭐",
    };
}
