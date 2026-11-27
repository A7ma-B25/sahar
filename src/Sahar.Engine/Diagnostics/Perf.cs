using System.Diagnostics;
using Sahar.Core.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Diagnostics;

/// <summary>
/// 0.17.2 (owner: other apps lag): her own numbers for «ابعت سجلّي» — a watchdog on a background thread pings her UI
/// thread every 250 ms and records how late it ran; each sensor's poll and each hook's events are timed or counted by
/// name. perf.txt (numbers only) is written beside her logs every 10 minutes and when the bundle is made.
/// </summary>
internal static class Perf
{
    public static readonly PerfCounters Counters = new(DateTimeOffset.Now);
    private static Thread? _watchdog;
    private static Timer? _writer;

    /// <summary>Times <paramref name="a"/> under <paramref name="name"/> (Stopwatch ticks; cheap).</summary>
    public static void Measure(string name, Action a)
    {
        long t0 = Stopwatch.GetTimestamp();
        try { a(); }
        finally { Counters.Time(name, Stopwatch.GetElapsedTime(t0).TotalMilliseconds); }
    }

    public static void Count(string name, int n = 1) => Counters.Count(name, n);

    public static void Start(DispatcherQueue ui)
    {
        if (_watchdog is not null) return;
        _watchdog = new Thread(() => Watch(ui)) { IsBackground = true, Name = "sahar-perf-watchdog", Priority = ThreadPriority.BelowNormal };
        _watchdog.Start();
        _writer = new Timer(_ => Write(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    private static void Watch(DispatcherQueue ui)
    {
        using var ran = new ManualResetEventSlim();
        while (true)
        {
            Thread.Sleep(250);
            ran.Reset();
            long t0 = Stopwatch.GetTimestamp();
            if (!ui.TryEnqueue(DispatcherQueuePriority.Normal, () => { Counters.Stall(Stopwatch.GetElapsedTime(t0).TotalMilliseconds); ran.Set(); })) return;
            ran.Wait(TimeSpan.FromSeconds(30));   // one ping at a time: a long stall is one sample, its full length
        }
    }

    /// <summary>perf.txt beside her logs (numbers only).</summary>
    public static void Write()
    {
        try
        {
            File.WriteAllText(Path.Combine(Log.Folder, PerfCounters.File), Counters.Text(DateTimeOffset.Now, Extra()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static IEnumerable<KeyValuePair<string, string>> Extra()
    {
        var list = new List<KeyValuePair<string, string>>();
        try
        {
            using var me = Process.GetCurrentProcess();
            list.Add(new("engine threads", me.Threads.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            list.Add(new("engine cpu seconds", me.TotalProcessorTime.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception) { }
        foreach (var name in new[] { "llama-server", "Sahar.Mind", "Sahar.Settings" })
        {
            try
            {
                var ps = Process.GetProcessesByName(name);
                int threads = 0;
                double cpu = 0;
                foreach (var p in ps)
                {
                    try { threads += p.Threads.Count; cpu += p.TotalProcessorTime.TotalSeconds; } catch (Exception) { }
                    p.Dispose();
                }
                list.Add(new($"{name} processes", ps.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                if (ps.Length > 0)
                {
                    list.Add(new($"{name} threads", threads.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    list.Add(new($"{name} cpu seconds", cpu.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            catch (Exception) { }
        }
        return list;
    }
}
