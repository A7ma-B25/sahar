using Sahar.Core.Art;

namespace Sahar.Core.Design;

/// <summary>One theme's Porcelain tokens (spec §7; UI study §5.1).</summary>
public sealed record ThemeTokens(Rgba Bg, Rgba Surface, Rgba Menu, Rgba Sunken, Rgba Hover, Rgba Pressed, Rgba Ink, Rgba Text2, Rgba Text3,
    Rgba Boundary, Rgba Accent, Rgba OnAccent, Rgba AccentSoft, Rgba AccentInk, Rgba Moon, Rgba Leaf, Rgba Error, Rgba Warn, Rgba StarDot);

/// <summary>
/// The menu card's colours: opaque and matte, only Sahar carries colour (spec §7). The header tints per mood are a
/// reviewed literal table (spec §0.3; proposed by <c>tools/art/header_tints.py</c>, sheet docs/design/menu/header-tints.png).
/// </summary>
public static class Tokens
{
    private static Rgba H(string hex) => Rgba.Parse(hex);

    public static ThemeTokens Light { get; } = new(H("#F5F4F8"), H("#FFFFFF"), H("#FFFFFF"), H("#ECEAF2"), H("#F1EFF6"), H("#E8E5F0"),
        H("#24212B"), H("#5C5867"), H("#6B6776"), H("#8F8A9B"), H("#5B47C4"), H("#FFFFFF"), H("#ECE7FF"), H("#4636A6"), H("#7A5A1C"),
        H("#2F7A4A"), H("#C2363B"), H("#9A5418"), H("#B9A4F0"));

    public static ThemeTokens Dark { get; } = new(H("#18161C"), H("#221F27"), H("#2A2630"), H("#1C1A20"), H("#353140"), H("#3D3848"),
        H("#EFECF4"), H("#B8B3C2"), H("#A29DAD"), H("#7D7889"), H("#B6A9FF"), H("#1C1733"), H("#3A3354"), H("#D2C9FF"), H("#F2C86A"),
        H("#8ED3A0"), H("#FF8A8D"), H("#F2A06E"), H("#E9D9A6"));

    public static ThemeTokens For(bool dark) => dark ? Dark : Light;

    /// <summary>The header's background for her mood (light, dark).</summary>
    public static IReadOnlyDictionary<Mood, (Rgba Light, Rgba Dark)> HeaderTints { get; } = new Dictionary<Mood, (Rgba, Rgba)>
    {
        [Mood.Calm] = (H("#EEE8FF"), H("#3A3450")),
        [Mood.Happy] = (H("#FFF0EA"), H("#4C4045")),
        [Mood.Focus] = (H("#EAF2FF"), H("#3F404F")),
        [Mood.Excited] = (H("#FFF6DE"), H("#4C423E")),
        [Mood.Sleepy] = (H("#DCD8EF"), H("#3A3646")),
        [Mood.Mystic] = (H("#DEDAF2"), H("#2D273E")),
        [Mood.Alert] = (H("#FFF0E6"), H("#4C4043")),
        [Mood.Dim] = (H("#DED8EF"), H("#3F3A49")),
        [Mood.Error] = (H("#FFEDEE"), H("#4A3D46")),
        [Mood.Online] = (H("#E9FBF8"), H("#3E444C")),
        [Mood.Hot] = (H("#FFE6DA"), H("#4C3C3F")),
        [Mood.Cold] = (H("#EEF7FF"), H("#424350")),
        [Mood.Love] = (H("#FFEFF4"), H("#4C3F4A")),
        [Mood.Night] = (H("#DED8F6"), H("#352F47")),
        [Mood.Sick] = (H("#EAF7E4"), H("#3E4341")),
        [Mood.Angry] = (H("#FFE3E0"), H("#4A3940")),
    };

    public static Rgba HeaderTint(Mood m, bool dark) => dark ? HeaderTints[m].Dark : HeaderTints[m].Light;

    /// <summary>Every text pair the card and the settings pages use (fg token, bg token): each must read ≥ 4.5 : 1.</summary>
    public static IReadOnlyList<(string Fg, string Bg)> TextPairs { get; } = BuildPairs();

    private static List<(string, string)> BuildPairs()
    {
        var list = new List<(string, string)>();
        foreach (var fg in new[] { "ink", "text-2" })
            foreach (var bg in new[] { "bg", "surface", "menu", "sunken", "hover", "pressed" }) list.Add((fg, bg));
        foreach (var fg in new[] { "text-3", "accent", "moon" })
            foreach (var bg in new[] { "bg", "surface", "menu", "sunken", "hover" }) list.Add((fg, bg));
        foreach (var fg in new[] { "leaf", "error", "warn" })
            foreach (var bg in new[] { "bg", "surface", "menu" }) list.Add((fg, bg));
        list.Add(("on-accent", "accent"));
        list.Add(("accent-ink", "accent-soft"));
        return list;
    }

    /// <summary>A token by its spec name ("text-2", "accent-soft", …).</summary>
    public static Rgba Get(ThemeTokens t, string name) => name switch
    {
        "bg" => t.Bg, "surface" => t.Surface, "menu" => t.Menu, "sunken" => t.Sunken, "hover" => t.Hover, "pressed" => t.Pressed,
        "ink" => t.Ink, "text-2" => t.Text2, "text-3" => t.Text3, "boundary" => t.Boundary, "accent" => t.Accent,
        "on-accent" => t.OnAccent, "accent-soft" => t.AccentSoft, "accent-ink" => t.AccentInk, "moon" => t.Moon, "leaf" => t.Leaf,
        "error" => t.Error, "warn" => t.Warn, "star-dot" => t.StarDot,
        _ => throw new ArgumentException($"unknown token '{name}'"),
    };
}
