using Sahar.Core.Geometry;

namespace Sahar.Core.Art;

/// <summary>Maps the rig's design units (300×300 canvas, body 160 wide) to DIPs and pixels.</summary>
public static class RigScale
{
    public const float CanvasUnits = 300f;
    public const float BodyUnits = 160f;

    /// <summary>DIPs per design unit for a body <paramref name="bodyDip"/> DIPs wide.</summary>
    public static float DipPerUnit(float bodyDip) => bodyDip / BodyUnits;

    /// <summary>Pixel size of the square canvas surface at a DPI scale (1.0 = 96 DPI).</summary>
    public static SizeI RasterSize(float bodyDip, float dpiScale)
    {
        int px = (int)MathF.Ceiling(CanvasUnits * bodyDip / BodyUnits * dpiScale - 1e-4f);
        return new SizeI(px, px);
    }
}
