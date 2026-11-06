namespace Sahar.Core.Life;

/// <summary>
/// What she has learned you like: one weight per own action. A poke after an action raises it, grabbing her
/// away lowers it; once a day every weight drifts back toward 1 so old habits fade.
/// </summary>
public sealed class Preferences
{
    public const float Min = 0.25f, Max = 3f, LikedFactor = 1.25f, DislikedFactor = 0.7f, DecayShare = 0.05f;

    private readonly Dictionary<LifeAction, float> _weights = [];

    public float Weight(LifeAction a) => _weights.TryGetValue(a, out var w) ? w : 1f;

    public void Liked(LifeAction a) => Set(a, Weight(a) * LikedFactor);

    public void Disliked(LifeAction a) => Set(a, Weight(a) * DislikedFactor);

    public void Decay()
    {
        foreach (var a in _weights.Keys.ToList()) _weights[a] += (1f - _weights[a]) * DecayShare;
    }

    /// <summary>Catches up on days she was not running (at most 120, after which every weight is within 0.5 % of 1).</summary>
    public void DecayDays(int days)
    {
        for (int i = 0; i < Math.Clamp(days, 0, 120); i++) Decay();
    }

    private void Set(LifeAction a, float w) => _weights[a] = Math.Clamp(w, Min, Max);

    public Dictionary<string, float> ToState() => _weights.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);

    /// <summary>Unknown names are ignored and values clamped, so an edited or old file cannot break her.</summary>
    public static Preferences FromState(IReadOnlyDictionary<string, float>? state)
    {
        var p = new Preferences();
        if (state == null) return p;
        foreach (var (name, w) in state)
            if (Enum.TryParse<LifeAction>(name, out var a) && a != LifeAction.None && Enum.IsDefined(a) && float.IsFinite(w))
                p.Set(a, w);
        return p;
    }
}
