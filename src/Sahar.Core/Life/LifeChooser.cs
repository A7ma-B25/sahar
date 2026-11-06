namespace Sahar.Core.Life;

/// <summary>What is going on around her when she decides what to do.</summary>
/// <param name="Busy">She is being dragged, thrown or is mid-motion.</param>
/// <param name="Hour">Local hour 0–23.</param>
/// <param name="HasPerchableWindow">The front window is one she can sit on.</param>
public sealed record LifeContext(bool Typing, bool VideoPlaying, bool MusicPlaying, bool UserAway, bool Hidden, bool Busy,
    int Hour, bool HasPerchableWindow);

/// <summary>Picks her next own action: weighted by the moment and by what you have shown you like.</summary>
public static class LifeChooser
{
    private static readonly LifeAction[] Candidates =
        [LifeAction.Wander, LifeAction.PeekTaskbar, LifeAction.PerchWindow, LifeAction.CelebrateLoop, LifeAction.SleepInCorner];

    public static LifeAction Choose(LifeContext c, Func<LifeAction, float> learnedWeight, Random rng)
    {
        // She never covers what matters: nothing while you type, watch, are away, or while she is hidden or busy.
        if (c.Typing || c.VideoPlaying || c.UserAway || c.Hidden || c.Busy) return LifeAction.None;

        bool lateNight = c.Hour is >= 0 and <= 4;
        Span<float> w = stackalloc float[Candidates.Length];
        float total = 0;
        for (int i = 0; i < Candidates.Length; i++)
        {
            float b = Candidates[i] switch
            {
                LifeAction.Wander => 4,
                LifeAction.PeekTaskbar => 2,
                LifeAction.PerchWindow => c.HasPerchableWindow ? 2 : 0,
                LifeAction.CelebrateLoop => lateNight ? 0 : c.MusicPlaying ? 3 : 1,
                LifeAction.SleepInCorner => lateNight ? 2 : 0,
                _ => 0,
            };
            w[i] = b * MathF.Max(0, learnedWeight(Candidates[i]));
            total += w[i];
        }
        if (total <= 0) return LifeAction.None;
        float pick = (float)rng.NextDouble() * total;
        int last = 0;
        for (int i = 0; i < Candidates.Length; i++)
        {
            if (w[i] <= 0) continue;
            last = i;
            pick -= w[i];
            if (pick < 0) return Candidates[i];
        }
        return Candidates[last];   // rounding: the last action that could be chosen
    }
}
