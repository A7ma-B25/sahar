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

    public static PanelPlace Place(RectF body, RectF work, SizeF panel, float dpi)
    {
        float w = MathF.Min(panel.Width, work.Width), h = MathF.Min(panel.Height, work.Height), gap = GapDip * dpi;
        float roomLeft = body.X - work.X, roomRight = work.Right - body.Right;
        bool left = roomRight < roomLeft;
        float x = left ? body.X - gap - w : body.Right + gap;
        float y = body.Bottom - h;
        x = Math.Clamp(x, work.X, MathF.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, MathF.Max(work.Y, work.Bottom - h));
        float clasp = Math.Clamp(body.Y + body.Height * 0.45f, y + 24 * dpi, y + h - 24 * dpi);
        return new PanelPlace(new RectF(x, y, w, h), left, clasp);
    }
}
