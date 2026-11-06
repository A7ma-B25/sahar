namespace Sahar.Engine;

/// <param name="TestMode">Enables debug pipe commands (CI and screenshots).</param>
/// <param name="Capturable">Leaves the windows visible to screen capture (CI and screenshots only).</param>
internal sealed record EngineOptions(bool TestMode, bool Capturable)
{
    public static EngineOptions Parse(string[] args) =>
        new(args.Contains("--test-mode"), args.Contains("--capturable"));
}
