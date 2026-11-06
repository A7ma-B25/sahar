using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Motion;

/// <summary>Picks where Sahar drifts next, staying clear of the given rects.</summary>
public sealed class Wander(int seed)
{
    public const int Tries = 50;
    private readonly Random _rng = new(seed);

    public Vector2 NextTarget(RectF workArea, Vector2 size, IReadOnlyList<RectF> avoid)
    {
        float spanX = MathF.Max(0, workArea.Width - size.X), spanY = MathF.Max(0, workArea.Height - size.Y);
        Vector2 p = default;
        for (int i = 0; i < Tries; i++)
        {
            p = new Vector2(workArea.X + (float)_rng.NextDouble() * spanX, workArea.Y + (float)_rng.NextDouble() * spanY);
            var r = new RectF(p.X, p.Y, size.X, size.Y);
            bool hit = false;
            foreach (var a in avoid) if (a.Intersects(r)) { hit = true; break; }
            if (!hit) return p;
        }
        return p;
    }
}
