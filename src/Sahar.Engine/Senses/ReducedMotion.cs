namespace Sahar.Engine.Senses;

/// <summary>Windows "Animation effects" (Settings → Accessibility → Visual effects): off means reduced motion (spec §2).</summary>
internal static unsafe class ReducedMotion
{
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    public static bool AnimationsOff()
    {
        int on = 1;
        return Native.SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, &on, 0) != 0 && on == 0;
    }
}
