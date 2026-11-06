using System.Globalization;

namespace Sahar.Core.Art;

/// <summary>An 8-bit sRGB colour with alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Parses "#RRGGBB" or "#RRGGBBAA".</summary>
    public static Rgba Parse(string hex)
    {
        if (hex is not { Length: 7 or 9 } || hex[0] != '#') throw new FormatException($"Bad colour '{hex}'");
        byte H(int i) => byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(H(1), H(3), H(5), hex.Length == 9 ? H(7) : (byte)255);
    }

    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
