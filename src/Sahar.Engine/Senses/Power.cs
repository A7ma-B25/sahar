using Sahar.Core.Brain;

namespace Sahar.Engine.Senses;

/// <summary>Battery: low below 20 % while not charging, and the moment the charger is plugged in.</summary>
internal sealed unsafe class Power
{
    public const int LowPercent = 20;
    private const byte NoBattery = 128;

    private readonly Action<EventKind> _post;
    private bool? _charging;
    private bool _lowSent;

    public Power(Action<EventKind> post) => _post = post;

    /// <summary>0.13: the battery for her card and moments — percent, charging, low now, plugged in now.</summary>
    public event Action<int, bool, bool, bool>? Changed;

    /// <summary>Call at start and on WM_POWERBROADCAST.</summary>
    public void Check()
    {
        SYSTEM_POWER_STATUS s;
        if (Native.GetSystemPowerStatus(&s) == 0 || (s.BatteryFlag & NoBattery) != 0 || s.BatteryLifePercent == 255) return;
        bool charging = s.ACLineStatus == 1;
        bool plugged = _charging == false && charging;
        if (plugged) _post(EventKind.BatteryCharging);
        _charging = charging;
        bool low = !charging && s.BatteryLifePercent < LowPercent;
        bool lowNow = low && !_lowSent;
        if (lowNow) _post(EventKind.BatteryLow);
        _lowSent = low;
        Changed?.Invoke(s.BatteryLifePercent, charging, lowNow, plugged);
    }
}
