using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>Spec §5 #21: the CPU above 85 % for two minutes (sampled every 10 s with GetSystemTimes).</summary>
internal sealed unsafe class CpuSense
{
    private readonly CpuHeat _heat = new();
    private readonly Action<EventKind> _post;
    private long _idle, _kernel, _user;
    private DateTimeOffset _clock = DateTimeOffset.Now;

    public CpuSense(DispatcherQueue ui, Action<EventKind> post)
    {
        _post = post;
        Sample();
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(10);
        t.Tick += (_, _) =>
        {
            try { if (Sample() is { } busy) Feed(DateTimeOffset.Now, busy); }
            catch (Exception e) { Log.Write($"cpu error: {e.GetType().Name}"); }
        };
        t.Start();
    }

    private float? Sample()
    {
        long idle, kernel, user;
        if (Native.GetSystemTimes(&idle, &kernel, &user) == 0) return null;
        long di = idle - _idle, total = kernel - _kernel + (user - _user);   // kernel time includes idle time
        (_idle, _kernel, _user) = (idle, kernel, user);
        return total <= 0 ? null : 100f * (total - di) / total;
    }

    public void Feed(DateTimeOffset now, float busy)
    {
        if (!_heat.Update(now, busy)) return;
        Log.Write("cpu: hot for two minutes");
        _post(EventKind.CpuHot);
    }

    /// <summary>Test mode: samples on a fake clock, 10 s apart.</summary>
    public void Simulate(float busy, int count)
    {
        for (int i = 0; i < count; i++) { _clock += TimeSpan.FromSeconds(10); Feed(_clock, busy); }
    }
}
