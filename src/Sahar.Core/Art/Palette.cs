using Sahar.Core.Json;

namespace Sahar.Core.Art;

public enum Mood { Calm, Happy, Focus, Excited, Sleepy, Mystic, Alert, Dim, Error, Online, Hot, Cold, Love, Night, Sick, Angry }

/// <summary>Body gradient (top, mid, bottom), glow, leaf bud and blush colours of one mood.</summary>
public sealed record MoodColors(Rgba Top, Rgba Mid, Rgba Bottom, Rgba Glow, Rgba Bud, Rgba Blush);

/// <summary>Sahar's mood colours, from assets/palette.json (exported from the design).</summary>
public static class Palette
{
    private static readonly Dictionary<Mood, MoodColors> Moods = Load();

    public static MoodColors Get(Mood mood) => Moods[mood];

    private static Dictionary<Mood, MoodColors> Load()
    {
        var raw = Assets.Read("palette.json", CoreJson.Default.DictionaryStringMoodColorsDto);
        return raw.ToDictionary(
            kv => Enum.Parse<Mood>(kv.Key),
            kv => new MoodColors(Rgba.Parse(kv.Value.Top), Rgba.Parse(kv.Value.Mid), Rgba.Parse(kv.Value.Bottom),
                                 Rgba.Parse(kv.Value.Glow), Rgba.Parse(kv.Value.Bud), Rgba.Parse(kv.Value.Blush)));
    }
}

public sealed record MoodColorsDto(string Top, string Mid, string Bottom, string Glow, string Bud, string Blush);
