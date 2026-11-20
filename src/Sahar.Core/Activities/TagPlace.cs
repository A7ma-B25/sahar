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

    /// <summary>Total (0.14.1): any input gives a finite tag, inside the work area whenever it fits there. With no room
    /// under or beside her (perched on the taskbar, flying low, a small work area) it rises to the work area's bottom
    /// instead of throwing (his log: ~1 273 Clamp errors an hour).</summary>
    public static RectF Place(RectF body, RectF work, Vector2 tag, float dpi, out TagSide side, float threadDip = ThreadDip)
    {
        static float F(float v, float or = 0) => float.IsFinite(v) ? v : or;
        body = new RectF(F(body.X), F(body.Y), MathF.Max(0, F(body.Width)), MathF.Max(0, F(body.Height)));
        work = new RectF(F(work.X), F(work.Y), MathF.Max(0, F(work.Width)), MathF.Max(0, F(work.Height)));
        dpi = F(dpi, 1) is var d && d > 0 ? d : 1;
        threadDip = MathF.Max(0, F(threadDip, ThreadDip));
        float w = MathF.Round(MathF.Max(0, F(tag.X))), h = MathF.Round(MathF.Max(0, F(tag.Y)));
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
        float xHi = MathF.Max(work.X, work.Right - w), yHi = MathF.Max(work.Y, work.Bottom - h);
        x = Math.Clamp(x, work.X, xHi);
        y = Math.Clamp(y, MathF.Min(MathF.Max(work.Y, body.Y), yHi), yHi);
        return new RectF(MathF.Round(x), MathF.Round(y), w, h);
    }

    /// <summary>The tag plus its shadow (what the overlay must hold).</summary>
    public static RectF WithShadow(RectF tag, float dpi) =>
        new(tag.X - ShadowSideDip * dpi, tag.Y - ShadowSideDip * dpi, tag.Width + 2 * ShadowSideDip * dpi, tag.Height + (ShadowSideDip + ShadowBelowDip) * dpi);

    /// <summary>True when the tag reaches past her overlay's follow rect: only then is it a need of its own.</summary>
    public static bool OutsideFollow(RectF tag, RectI follow) =>
        tag.X < follow.Left || tag.Y < follow.Top || tag.Right > follow.Right || tag.Bottom > follow.Bottom;
}
