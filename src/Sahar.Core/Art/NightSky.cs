using System.Numerics;

namespace Sahar.Core.Art;

/// <summary>The night wash's three stops for one mood: inner and mid from the reviewed table, outer = body bottom.</summary>
public sealed record WashStops(Rgba Inner, Rgba Mid, Rgba Outer);

/// <summary>The night inside her (spec L2), per mood, read from the rig's one hex table (owner ruling 2026-11-10:
/// Calm is the spec exactly; the other moods are reviewed on the 16-mood sheet; no colour maths here).</summary>
public static class NightSky
{
    /// <summary>The wash centre in rig units.</summary>
    public static readonly Vector2 Centre = new(120, 196);

    private static readonly Dictionary<Mood, WashStops> Stops = Load();

    public static WashStops For(Mood m) => Stops[m];

    private static Dictionary<Mood, WashStops> Load() =>
        Rig.Load().Sky.WashStops.ToDictionary(kv => Enum.Parse<Mood>(kv.Key),
            kv => new WashStops(Rgba.Parse(kv.Value[0]), Rgba.Parse(kv.Value[1]), Rgba.Parse(kv.Value[2])));
}
