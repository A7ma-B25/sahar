namespace Sahar.Core.Senses;

/// <summary>Spec §8.1 screenshot: Print Screen, or Win+Shift+S. Only these keys are looked at; nothing is kept.</summary>
public sealed class ScreenshotKeys
{
    private const int VkPrintScreen = 0x2C, VkShift = 0x10, VkLShift = 0xA0, VkRShift = 0xA1, VkLWin = 0x5B, VkRWin = 0x5C, VkS = 0x53;
    private bool _win, _shift, _sDown;

    public bool Update(int vk, bool down)
    {
        switch (vk)
        {
            case VkLWin or VkRWin: _win = down; return false;
            case VkShift or VkLShift or VkRShift: _shift = down; return false;
            case VkPrintScreen: return down;
            case VkS:
                bool fire = down && !_sDown && _win && _shift;
                _sDown = down;
                return fire;
            default: return false;
        }
    }
}

/// <summary>Spec §8.1 downloads: a browser's temporary file turning into the real one.</summary>
public static class DownloadRules
{
    private static readonly string[] TempEndings = [".crdownload", ".part", ".partial", ".download", ".tmp"];

    public static bool IsTemp(string name) =>
        name.StartsWith("~$", StringComparison.Ordinal) || TempEndings.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool FinishedByRename(string oldName, string newName) => IsTemp(oldName) && !IsTemp(newName);
}

/// <summary>Spec §5 #21: the CPU above 85 % for two minutes (once; again after two calm minutes under 60 %).</summary>
public sealed class CpuHeat
{
    public static readonly TimeSpan Hold = TimeSpan.FromMinutes(2);
    private DateTimeOffset? _hotSince, _calmSince;
    private bool _fired;

    public bool Update(DateTimeOffset now, float busyPercent)
    {
        if (busyPercent > 85)
        {
            _calmSince = null;
            _hotSince ??= now;
            if (!_fired && now - _hotSince >= Hold) { _fired = true; return true; }
            return false;
        }
        _hotSince = null;
        if (busyPercent < 60)
        {
            _calmSince ??= now;
            if (_fired && now - _calmSince >= Hold) _fired = false;
        }
        else _calmSince = null;
        return false;
    }
}

/// <summary>Spec §8.1: an app is using the camera or microphone right now (its LastUsedTimeStop is 0).</summary>
public static class CapabilityUse
{
    public static bool InUse(IEnumerable<long> lastUsedTimeStop) => lastUsedTimeStop.Any(t => t == 0);
}

/// <summary>Spec §5 #7: rain coming within the next two hours (Open-Meteo, WMO weather codes).</summary>
public static class WeatherRules
{
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    public static bool RainSoon(IReadOnlyList<int> weatherCodes, IReadOnlyList<int> precipitationProbability) =>
        weatherCodes.Take(2).Any(c => c is >= 51 and <= 67 or >= 71 and <= 77 or >= 80 and <= 86 or >= 95 and <= 99) ||
        precipitationProbability.Take(2).Any(p => p >= 60);

    public static bool ShouldFetch(DateTimeOffset now, DateTimeOffset? last) => last is not { } l || now - l >= Every;
}
