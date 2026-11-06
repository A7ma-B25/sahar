using Sahar.Core.Settings;

namespace Sahar.Core.Life;

/// <summary>Something Sahar can choose to do on her own.</summary>
public enum LifeAction
{
    None, Wander, PeekTaskbar, PerchWindow, CelebrateLoop, SleepInCorner,
    Flight, PlayWithMoon, Curious, Dance, Breathe, OrbitCursor, Moment, FollowCursor, HideBehindWindow, HopIcons,
}

/// <summary>How long until her next own action, by activity level (spec §6).</summary>
public static class LifeTiming
{
    /// <param name="routineFactor">1–2: hours you usually work stretch the gap (she is calmer then).</param>
    public static TimeSpan NextGap(ActivityLevel level, Random rng, float routineFactor = 1f)
    {
        var (min, max) = level switch
        {
            ActivityLevel.Calm => (300.0, 600.0),
            ActivityLevel.Lively => (30.0, 90.0),
            _ => (60.0, 180.0),
        };
        double seconds = min + rng.NextDouble() * (max - min);
        return TimeSpan.FromSeconds(seconds * Math.Clamp(routineFactor, 1f, 2f));
    }
}
