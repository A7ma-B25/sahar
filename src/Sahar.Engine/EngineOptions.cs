namespace Sahar.Engine;

/// <param name="TestMode">Enables debug pipe commands (CI and screenshots).</param>
/// <param name="Capturable">Leaves the windows visible to screen capture (CI and screenshots only).</param>
/// <param name="SimulateBoot">Test mode only: start as if the PC had just started (her hello, motion 18).</param>
internal sealed record EngineOptions(bool TestMode, bool Capturable, bool SimulateBoot = false)
{
    public static EngineOptions Parse(string[] args) =>
        new(args.Contains("--test-mode"), args.Contains("--capturable"), args.Contains("--test-mode") && args.Contains("--simulate-boot"));
}
