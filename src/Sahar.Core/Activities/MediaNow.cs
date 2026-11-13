using Sahar.Core.Art;

namespace Sahar.Core.Activities;

/// <summary>One Windows media session as the picker sees it (the app id stays in memory only).</summary>
public sealed record SessionInfo(string App, bool Playing, bool Video, bool Browser, DateTimeOffset LastPlaying);

/// <summary>How long paused music keeps its tag (setting «لما توقف، خلّيها معي»).</summary>
public enum PausedKeep { TenMinutes, Hour, Always }

/// <summary>Music rules (spec 0.13 §2 Music): which session, how long a paused one stays, where the song is, and the
/// colour of her halo from the cover.</summary>
public static class MediaNow
{
    /// <summary>The most recently playing session that is not a video; with none playing, the last paused one; -1 for none.</summary>
    public static int Pick(IReadOnlyList<SessionInfo> sessions)
    {
        int best = -1;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.Video) continue;
            if (best < 0) { best = i; continue; }
            var b = sessions[best];
            if ((s.Playing && !b.Playing) || (s.Playing == b.Playing && s.LastPlaying > b.LastPlaying)) best = i;
        }
        return best;
    }

    /// <summary>A player's name from its media app id ("Spotify.exe", "…_8wekyb3d8bbwe!App").</summary>
    public static string AppLabel(string aumid)
    {
        if (aumid.Length == 0) return "";
        var s = aumid.Contains('!') ? aumid[(aumid.IndexOf('!') + 1)..] : aumid;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        s = s.Split('.')[^1];
        return s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static bool KeepPaused(DateTimeOffset pausedAt, DateTimeOffset now, PausedKeep keep) => keep switch
    {
        PausedKeep.Always => true,
        PausedKeep.Hour => now - pausedAt < TimeSpan.FromHours(1),
        _ => now - pausedAt < TimeSpan.FromMinutes(10),
    };

    /// <summary>The position now: extrapolated from the last update while playing, within 0…end.</summary>
    public static TimeSpan Position(TimeSpan position, DateTimeOffset updated, DateTimeOffset now, bool playing, TimeSpan end)
    {
        var p = playing && now > updated ? position + (now - updated) : position;
        if (end > TimeSpan.Zero && p > end) p = end;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p;
    }

    /// <summary>The cover's vivid median colour (per channel, over pixels with some colour in them; all pixels when the
    /// cover is grey). <paramref name="bgra"/> is 8-bit BGRA.</summary>
    public static Rgba CoverColour(ReadOnlySpan<byte> bgra) => CoverColourOrNull(bgra) ?? Rgba.Parse("#808080");

    public static Rgba? CoverColourOrNull(ReadOnlySpan<byte> bgra)
    {
        int n = bgra.Length / 4;
        if (n == 0) return null;
        var vivid = new List<(byte R, byte G, byte B)>();
        var all = new List<(byte R, byte G, byte B)>(n);
        for (int i = 0; i < n; i++)
        {
            byte b = bgra[4 * i], g = bgra[4 * i + 1], r = bgra[4 * i + 2];
            all.Add((r, g, b));
            var (_, s, l) = Hsl(new Rgba(r, g, b, 255));
            if (s >= 0.25 && l is >= 0.15 and <= 0.9) vivid.Add((r, g, b));
        }
        var set = vivid.Count >= Math.Max(1, n / 20) ? vivid : all;
        static byte Median(IEnumerable<byte> v) { var a = v.Order().ToList(); return a[a.Count / 2]; }
        return new Rgba(Median(set.Select(p => p.R)), Median(set.Select(p => p.G)), Median(set.Select(p => p.B)), 255);
    }

    /// <summary>Her halo from the cover: the same hue, lightened (lightness 0.73–0.84) and softened (saturation ≤ 0.6).</summary>
    public static Rgba HaloTint(Rgba cover)
    {
        var (h, s, l) = Hsl(cover);
        return FromHsl(h, Math.Min(s, 0.6), Math.Clamp(l, 0.73, 0.84));
    }

    public static (double H, double S, double L) Hsl(Rgba c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgba FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return new Rgba(B(r + m), B(g + m), B(b + m), 255);
    }
}
