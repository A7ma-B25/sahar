using Sahar.Core.Settings;

namespace Sahar.Core.Activities;

/// <summary>
/// 0.17.2 (owner: other apps lagged even while she was hidden for an hour): which of her sensors run. Each activity
/// toggle starts and stops its own sensor (not only its tag); «خبّيني ساعة» rests them all, skips the fullscreen check,
/// and unloads her brain (llama-server stops) until she is back. Pure.
/// </summary>
public sealed record SensorPlan(bool Copy, bool Music, bool System, bool CameraMic, bool Fullscreen, bool UnloadMind)
{
    public static SensorPlan For(SaharSettings s, bool askedHidden) => askedHidden
        ? new(false, false, false, false, false, true)
        : new(s.ActCopy, s.ActMusic, s.ActSystem, s.ActMicCam, true, false);
}
