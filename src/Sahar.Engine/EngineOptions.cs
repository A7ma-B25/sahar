namespace Sahar.Engine;

/// <param name="TestMode">Enables debug pipe commands (CI and screenshots).</param>
/// <param name="Capturable">Leaves the windows visible to screen capture (CI and screenshots only).</param>
/// <param name="SimulateBoot">Test mode only: start as if the PC had just started (her hello, motion 18).</param>
/// <param name="DownloadsDir">Test mode only: watch this folder instead of Downloads.</param>
/// <param name="WeatherStubDir">Test mode only: read geocode.json / forecast.json from here instead of Open-Meteo.</param>
/// <param name="QuranBase">Test mode only: stream surahs from this base URL instead of the CDN.</param>
internal sealed record EngineOptions(bool TestMode, bool Capturable, bool SimulateBoot = false, string? DownloadsDir = null, string? WeatherStubDir = null,
    string? QuranBase = null)
{
    public static EngineOptions Parse(string[] args)
    {
        bool test = args.Contains("--test-mode");
        string? After(string flag) { int i = Array.IndexOf(args, flag); return test && i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        return new(test, args.Contains("--capturable"), test && args.Contains("--simulate-boot"), After("--downloads"), After("--weather-stub"),
            After("--quran-base"));
    }
}
