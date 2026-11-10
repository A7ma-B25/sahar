namespace Sahar.Core.Art;

/// <summary>WCAG contrast and simple sRGB mixing (plan 0.10b: no OKLab; the reviewed tables are literal hex).</summary>
public static class ColorMath
{
    /// <summary>The WCAG 2 contrast ratio (1–21) of two opaque colours.</summary>
    public static double Contrast(Rgba a, Rgba b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Luminance(Rgba c)
    {
        static double Ch(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    /// <summary><paramref name="fg"/> (with its alpha) over an opaque <paramref name="bg"/>.</summary>
    public static Rgba Over(Rgba fg, Rgba bg)
    {
        float a = fg.A / 255f;
        byte M(byte f, byte b) => (byte)MathF.Round(f * a + b * (1 - a));
        return new Rgba(M(fg.R, bg.R), M(fg.G, bg.G), M(fg.B, bg.B));
    }

    public static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        byte M(byte x, byte y) => (byte)MathF.Round(x + (y - x) * t);
        return new Rgba(M(a.R, b.R), M(a.G, b.G), M(a.B, b.B), M(a.A, b.A));
    }
}
