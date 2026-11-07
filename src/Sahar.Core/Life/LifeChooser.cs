namespace Sahar.Core.Life;

/// <summary>What is going on around her when she decides what to do.</summary>
/// <param name="Busy">She is being dragged, thrown or is mid-motion.</param>
/// <param name="Hour">Local hour 0–23.</param>
/// <param name="HasPerchableWindow">The front window is one she can sit on.</param>
/// <param name="VideoPlaying">Something may be a video in front of you: only small things (a face moment, a breath).</param>
/// <param name="CursorResting">The mouse has rested near her for a couple of seconds.</param>
public sealed record LifeContext(bool Typing, bool VideoPlaying, bool MusicPlaying, bool UserAway, bool Hidden, bool Busy,
    int Hour, bool HasPerchableWindow, bool CursorResting = false);

/// <summary>Picks her next own action: weighted by the moment and by what you have shown you like.</summary>
public static class LifeChooser
{
    private static readonly LifeAction[] Candidates = [.. Enum.GetValues<LifeAction>().Where(a => a != LifeAction.None)];

    public static LifeAction Choose(LifeContext c, Func<LifeAction, float> learnedWeight, Random rng)
    {
        // She never covers what matters: nothing while you type, are away, or while she is hidden or busy;
        // only small things while something that may be a video is in front of you.
        if (c.Typing || c.UserAway || c.Hidden || c.Busy) return LifeAction.None;

        bool lateNight = c.Hour is >= 0 and <= 4;
        Span<float> w = stackalloc float[Candidates.Length];
        float total = 0;
        for (int i = 0; i < Candidates.Length; i++)
        {
            var a = Candidates[i];
            float b = a switch
            {
                LifeAction.Moment => 4,
                LifeAction.Breathe => 1,
                _ when c.VideoPlaying => 0,
                LifeAction.Flight => 3,
                LifeAction.Wander => 2,
                LifeAction.PeekTaskbar => 2,
                LifeAction.PerchWindow => c.HasPerchableWindow ? 2 : 0,
                LifeAction.PlayWithMoon => 2,
                LifeAction.Curious => 2,
                LifeAction.OrbitCursor => c.CursorResting ? 2 : 0,
                LifeAction.FollowCursor => 2,
                LifeAction.HopIcons => 2,
                LifeAction.HideBehindWindow => c.HasPerchableWindow ? 1 : 0,
                LifeAction.Dance => c.MusicPlaying ? 5 : 0,
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
