using Sahar.Core.Geometry;

namespace Sahar.Core.Surfaces;

/// <summary>Where a panel goes: its rectangle, whether it is on her left, and the clasp's height (px).</summary>
public readonly record struct PanelPlace(RectF Rect, bool OnHerLeft, float ClaspY);

/// <summary>
/// Pickers, input cards, the tray menu's twin and «الدرج» open beside her like the menu card (spec §4.3): the roomier
/// side, 12 DIP from her body, bottom-aligned with her, the gold clasp at her face; always inside her monitor's work
/// area (a panel taller than it is cut to it). All px.
/// </summary>
public static class PanelPlacement
{
    public const float GapDip = 12;

    /// <summary>The chat window keeps clear of her bubbles (they are up to 264 DIP wide, centred on her).</summary>
    public const float ChatGapDip = 130;

    /// <param name="avoid">The open chat window: a panel goes to her other side rather than over it (when it fits there).</param>
    public static PanelPlace Place(RectF body, RectF work, SizeF panel, float dpi, float gapDip = GapDip, RectF? avoid = null)
    {
        float roomLeft = body.X - work.X, roomRight = work.Right - body.Right;
        var first = Side(body, work, panel, dpi, gapDip, roomRight < roomLeft);
        if (avoid is not { } chat || !first.Rect.Intersects(chat)) return first;
        var other = Side(body, work, panel, dpi, gapDip, !first.OnHerLeft);
        return other.Rect.Intersects(chat) ? first : other;
    }

    private static PanelPlace Side(RectF body, RectF work, SizeF panel, float dpi, float gapDip, bool left)
    {
        float w = MathF.Min(panel.Width, work.Width), h = MathF.Min(panel.Height, work.Height), gap = gapDip * dpi;
        float x = left ? body.X - gap - w : body.Right + gap;
        float y = body.Bottom - h;
        x = Math.Clamp(x, work.X, MathF.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, MathF.Max(work.Y, work.Bottom - h));
        float clasp = Math.Clamp(body.Y + body.Height * 0.45f, y + 24 * dpi, y + h - 24 * dpi);
        return new PanelPlace(new RectF(x, y, w, h), left, clasp);
    }

    /// <summary>An ask / choose bubble: centred above her head (its tail points at her leaf), inside the work area.</summary>
    public static RectF Above(RectF body, RectF work, SizeF card, float dpi)
    {
        float w = MathF.Min(card.Width, work.Width), h = MathF.Min(card.Height, work.Height);
        float x = body.X + body.Width / 2 - w / 2, y = body.Y - 12 * dpi - h;
        if (y < work.Y) y = body.Bottom + 12 * dpi;   // no room above her (she sits at the top): below her instead
        x = Math.Clamp(x, work.X, MathF.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, MathF.Max(work.Y, work.Bottom - h));
        return new RectF(x, y, w, h);
    }

    /// <summary>The tray menu: just above the point it was opened at (the tray icon), centred on it, inside the work area.</summary>
    public static RectF Anchor(float px, float py, RectF work, SizeF card, float dpi)
    {
        float w = MathF.Min(card.Width, work.Width), h = MathF.Min(card.Height, work.Height);
        float x = px - w / 2, y = py - 8 * dpi - h;
        x = Math.Clamp(x, work.X, MathF.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, MathF.Max(work.Y, work.Bottom - h));
        return new RectF(x, y, w, h);
    }
}
