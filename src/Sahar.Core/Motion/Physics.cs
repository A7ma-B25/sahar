using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Motion;

/// <summary>Position is the body's top-left corner, in DIPs.</summary>
public record struct Body2D(Vector2 Position, Vector2 Velocity);

public static class Physics
{
    public const float Friction = 2.2f;
    public const float Restitution = 0.55f;
    public const float RestSpeed = 20f;

    public static (Body2D next, bool resting) Step(Body2D b, float dt, RectF bounds, Vector2 size)
    {
        var v = b.Velocity * MathF.Exp(-Friction * dt);
        var p = b.Position + v * dt;
        float maxX = bounds.X + bounds.Width - size.X, maxY = bounds.Y + bounds.Height - size.Y;
        if (p.X < bounds.X && v.X < 0 || p.X > maxX && v.X > 0) v.X = -v.X * Restitution;
        if (p.Y < bounds.Y && v.Y < 0 || p.Y > maxY && v.Y > 0) v.Y = -v.Y * Restitution;
        var next = Clamp(new Body2D(p, v), bounds, size);
        var resting = next.Velocity.Length() < RestSpeed;
        return (resting ? next with { Velocity = Vector2.Zero } : next, resting);
    }

    public static Body2D Clamp(Body2D b, RectF bounds, Vector2 size)
    {
        float maxX = MathF.Max(bounds.X, bounds.X + bounds.Width - size.X);
        float maxY = MathF.Max(bounds.Y, bounds.Y + bounds.Height - size.Y);
        return b with { Position = new Vector2(Math.Clamp(b.Position.X, bounds.X, maxX), Math.Clamp(b.Position.Y, bounds.Y, maxY)) };
    }
}
