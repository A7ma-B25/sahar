namespace Sahar.Core.Life;

/// <summary>
/// Motion 18 "أول ما تشغّل الجهاز": she flies in and says hello when she starts within 5 minutes of your sign-in
/// (sign-in, not uptime: Windows' fast startup keeps uptime running across a shutdown). Tests ask for it.
/// </summary>
public static class Hello
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public static bool ShouldGreet(DateTimeOffset now, DateTimeOffset? signedInAt, bool testMode, bool simulateBoot)
    {
        if (testMode) return simulateBoot;
        return signedInAt is { } at && now - at >= TimeSpan.Zero && now - at < Window;
    }
}
