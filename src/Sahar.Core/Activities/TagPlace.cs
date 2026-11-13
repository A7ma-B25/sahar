using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Activities;

public enum TagSide { Below, Left, Right }

/// <summary>
/// Where her tag hangs (spec 0.13 §1.1), in screen pixels: under her when the work area has room for the thread and the
/// tag, otherwise at the side with more room; never above her (her bubble lives there). Whole pixels, so its text never
/// shimmers.
/// </summary>
public static class TagPlace
{
    public const float HeightDip = 40, MaxWidthDip = 220, ThreadDip = 16, BeadDip = 9, KnotDip = 5, SideGapDip = 10, Radius = 14;
    /// <summary>Room for the tag's shadow (the menu's e1: 24 DIP blur, 8 down).</summary>
    public const float ShadowSideDip = 16, ShadowBelowDip = 24;

    /// <summary>The thread's length (ActTag: 16 + 9 per bead, a little more for the knot).</summary>
    public static float Thread(int beads, bool knot) => ThreadDip + BeadDip * Math.Clamp(beads, 0, 3) + (knot ? KnotDip : 0);

    public static RectF Place(RectF body, RectF work, Vector2 tag, float dpi, out TagSide side, float threadDip = ThreadDip)
    {
        float w = MathF.Round(tag.X), h = MathF.Round(tag.Y);
        float cx = body.X + body.Width / 2, cy = body.Y + body.Height / 2;
        float x, y;
        if (work.Bottom - body.Bottom >= threadDip * dpi + h)
        {
            side = TagSide.Below;
            x = cx - w / 2;
            y = body.Bottom + threadDip * dpi;
        }
        else
        {
            bool right = work.Right - body.Right >= body.X - work.X;
            side = right ? TagSide.Right : TagSide.Left;
            x = right ? body.Right + SideGapDip * dpi : body.X - SideGapDip * dpi - w;
            y = MathF.Max(cy - h / 2, body.Y);
        }
        x = Math.Clamp(x, work.X, MathF.Max(work.X, work.Right - w));
        y = Math.Clamp(y, MathF.Max(work.Y, body.Y), MathF.Max(work.Y, work.Bottom - h));
        return new RectF(MathF.Round(x), MathF.Round(y), w, h);
    }

    /// <summary>The tag plus its shadow (what the overlay must hold).</summary>
    public static RectF WithShadow(RectF tag, float dpi) =>
        new(tag.X - ShadowSideDip * dpi, tag.Y - ShadowSideDip * dpi, tag.Width + 2 * ShadowSideDip * dpi, tag.Height + (ShadowSideDip + ShadowBelowDip) * dpi);

    /// <summary>True when the tag reaches past her overlay's follow rect: only then is it a need of its own.</summary>
    public static bool OutsideFollow(RectF tag, RectI follow) =>
        tag.X < follow.Left || tag.Y < follow.Top || tag.Right > follow.Right || tag.Bottom > follow.Bottom;
}
