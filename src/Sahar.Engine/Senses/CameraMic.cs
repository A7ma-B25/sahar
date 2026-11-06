using Microsoft.Win32;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// Is any app using the camera or the microphone right now (spec §8.1, §8.2)? Windows records it per app under
/// CapabilityAccessManager\ConsentStore: a LastUsedTimeStop of 0 means "still in use". Checked every 5 s; only
/// those numbers are read (app names stay in the registry).
/// </summary>
internal sealed class CameraMic
{
    private const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private bool? _simulated;

    public bool InUse { get; private set; }
    public event Action<bool>? Changed;

    public CameraMic(DispatcherQueue ui)
    {
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(5);
        t.Tick += (_, _) =>
        {
            try { Set(_simulated ?? Read()); }
            catch (Exception e) { Log.Write($"camera/mic error: {e.GetType().Name}"); }
        };
        t.Start();
    }

    /// <summary>Test mode: pretend the camera is (or isn't) in use.</summary>
    public void Simulate(bool on)
    {
        _simulated = on;
        Set(on);
    }

    private void Set(bool inUse)
    {
        if (inUse == InUse) return;
        InUse = inUse;
        Log.Write(inUse ? "camera/mic: in use, quiet" : "camera/mic: free");
        Changed?.Invoke(inUse);
    }

    private static bool Read() => CapabilityUse.InUse(Stops("webcam").Concat(Stops("microphone")));

    private static IEnumerable<long> Stops(string capability)
    {
        var list = new List<long>();
        using var root = Registry.CurrentUser.OpenSubKey(Store + capability);
        if (root is null) return list;
        Collect(root, list);
        using var nonPackaged = root.OpenSubKey("NonPackaged");
        if (nonPackaged is not null) Collect(nonPackaged, list);
        return list;
    }

    private static void Collect(RegistryKey parent, List<long> into)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using var app = parent.OpenSubKey(name);
            if (app?.GetValue("LastUsedTimeStop") is long stop && app.GetValue("LastUsedTimeStart") is long start && start > 0) into.Add(stop);
        }
    }
}
