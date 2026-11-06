namespace Sahar.Core.Brain;

/// <summary>Things that happen. Wire names are the strings sent over the pipe.</summary>
public enum EventKind
{
    Music, ClaudeWorking, ClaudePermission, ClaudeDone, TimerDone, Celebrate, WeatherRain,
    BatteryLow, BatteryCharging, DownloadDone, UserAway, UserBack, Notification, Dnd,
    NetOffline, NetOnline, Screenshot, ClipboardCopied, Reminder, Error, CpuHot, Break, LateNight,
    // End events: they only end a sticky event.
    MusicStop, DndOff,
}

public static class EventNames
{
    private static readonly (EventKind Kind, string Wire)[] Map =
    [
        (EventKind.Music, "music"), (EventKind.ClaudeWorking, "claude.working"),
        (EventKind.ClaudePermission, "claude.permission"), (EventKind.ClaudeDone, "claude.done"),
        (EventKind.TimerDone, "timer.done"), (EventKind.Celebrate, "celebrate"),
        (EventKind.WeatherRain, "weather.rain"), (EventKind.BatteryLow, "battery.low"),
        (EventKind.BatteryCharging, "battery.charging"), (EventKind.DownloadDone, "download.done"),
        (EventKind.UserAway, "user.away"), (EventKind.UserBack, "user.back"),
        (EventKind.Notification, "notification"), (EventKind.Dnd, "dnd"),
        (EventKind.NetOffline, "net.offline"), (EventKind.NetOnline, "net.online"),
        (EventKind.Screenshot, "screenshot"), (EventKind.ClipboardCopied, "clipboard.copied"),
        (EventKind.Reminder, "reminder"), (EventKind.Error, "error"), (EventKind.CpuHot, "cpu.hot"),
        (EventKind.Break, "break"), (EventKind.LateNight, "late-night"),
        (EventKind.MusicStop, "music.stop"), (EventKind.DndOff, "dnd.off"),
    ];

    public static string ToWire(EventKind k)
    {
        foreach (var (kind, wire) in Map) if (kind == k) return wire;
        throw new ArgumentOutOfRangeException(nameof(k), k, null);
    }

    public static EventKind Parse(string wire)
    {
        foreach (var (kind, w) in Map) if (string.Equals(w, wire, StringComparison.Ordinal)) return kind;
        throw new FormatException($"Unknown event '{wire}'.");
    }

    public static bool IsEndEvent(EventKind k) => k is EventKind.MusicStop or EventKind.DndOff;

    /// <summary>Sticky kinds stay until their end event arrives.</summary>
    public static bool IsSticky(EventKind k) =>
        k is EventKind.ClaudeWorking or EventKind.Music or EventKind.Dnd or EventKind.UserAway;

    /// <summary>The sticky kind this event ends, if any.</summary>
    public static EventKind? Ends(EventKind k) => k switch
    {
        EventKind.ClaudeDone => EventKind.ClaudeWorking,
        EventKind.MusicStop => EventKind.Music,
        EventKind.DndOff => EventKind.Dnd,
        EventKind.UserBack => EventKind.UserAway,
        _ => null,
    };
}

public static class EventPriorities
{
    public static int Of(EventKind k) => k switch
    {
        EventKind.ClaudePermission or EventKind.TimerDone or EventKind.Reminder => 100,
        EventKind.ClaudeDone or EventKind.Error or EventKind.BatteryLow or EventKind.Notification => 80,
        EventKind.DownloadDone or EventKind.NetOffline or EventKind.NetOnline or EventKind.CpuHot
            or EventKind.Screenshot or EventKind.ClipboardCopied or EventKind.Celebrate or EventKind.Break => 60,
        EventKind.UserAway => 20,
        _ => 40,
    };
}

[Flags]
public enum QuietReason { None = 0, Fullscreen = 1, CameraOrMic = 2, DoNotDisturb = 4, Quran = 8, Adhan = 16, QuietHours = 32 }
