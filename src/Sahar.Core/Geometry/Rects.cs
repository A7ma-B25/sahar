namespace Sahar.Core.Geometry;

/// <summary>A rectangle in Win32 physical pixels (right and bottom exclusive).</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>A rectangle in device-independent pixels.</summary>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(RectF o) => o.X >= X && o.Y >= Y && o.Right <= Right && o.Bottom <= Bottom;

    public bool Intersects(RectF o) => o.X < Right && o.Right > X && o.Y < Bottom && o.Bottom > Y;

    /// <summary>The overlap (empty when they don't meet).</summary>
    public RectF Intersect(RectF o)
    {
        float x = MathF.Max(X, o.X), y = MathF.Max(Y, o.Y);
        float r = MathF.Min(Right, o.Right), b = MathF.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? new RectF(x, y, 0, 0) : new RectF(x, y, r - x, b - y);
    }

    public bool Contains(float px, float py) => px >= X && px < Right && py >= Y && py < Bottom;

    public static RectF FromRectI(RectI r, float scale) =>
        new(r.Left / scale, r.Top / scale, r.Width / scale, r.Height / scale);
}

public readonly record struct SizeI(int Width, int Height);
