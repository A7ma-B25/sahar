using System.Diagnostics;
using System.Globalization;

namespace Spike;

/// <summary>
/// A small, ordinary window that shows the numbers the spike exists for, and writes them to a CSV.
/// It is deliberately left visible to screenshots so the user can send it; the blob is not.
/// Closing it quits.
/// </summary>
internal sealed class Hud : IWindowHandler
{
    public nint Hwnd { get; }
    private readonly nint _text;
    private readonly string _mode;
    private readonly string _csv;
    private readonly long _start = Stopwatch.GetTimestamp();

    private long _lastCpu; // 100 ns units
    private long _lastTicks;
    private readonly Queue<double> _idleCpu = new();  // last 30 one-second idle samples
    private double _peakMovingCpu;
    private int _framesThisSecond;
    private int _lastFps;
    private int _bestFps;
    private string _state = "idle";

    public Action<string>? OnTick;

    public Hud(string mode)
    {
        _mode = mode;
        Hwnd = Win.Create(this, Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW,
            Native.WS_CAPTION | Native.WS_SYSMENU | Native.WS_VISIBLE, 24, 24, 460, 250, $"Spike HUD - mode {mode}");
        _text = Win.CreateStatic(Hwnd, 12, 10, 430, 200);
        _lastCpu = Native.ProcessStats().cpu;
        _lastTicks = Stopwatch.GetTimestamp();

        Directory.CreateDirectory(Log.Folder);
        _csv = Path.Combine(Log.Folder, $"metrics-{mode}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        File.WriteAllText(_csv, "seconds,mode,state,cpu_percent,working_set_mb,private_mb,fps\n");
        Native.SetTimer(Hwnd, 1, 1000, 0);
        Update();
    }

    public void CountFrame() => _framesThisSecond++;

    public void SetState(string state) => _state = state;

    /// <summary>Why the blob is hidden, shown under the state.</summary>
    public string? HiddenReason;

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_TIMER when wParam == 1:
                Update();
                OnTick?.Invoke(_state);
                return 0;
            case Native.WM_CLOSE:
                Native.PostQuitMessage(0);
                return 0;
        }
        return null;
    }

    private void Update()
    {
        // Cheap per-process counters: Process.Refresh() would snapshot every process on the
        // machine each second and dominate the very numbers it reports.
        var (cpu, ws, priv) = Native.ProcessStats();
        long now = Stopwatch.GetTimestamp();
        double wall = Stopwatch.GetElapsedTime(_lastTicks, now).TotalSeconds;
        double cpuMs = (cpu - _lastCpu) / 10_000.0;
        double cpuPct = wall > 0 ? cpuMs / 1000 / wall / Environment.ProcessorCount * 100 : 0;
        // Also as "ms of CPU per 10 s", the unit the plan's target uses.
        double cpuMsPer10s = wall > 0 ? cpuMs / wall * 10 : 0;
        _lastCpu = cpu;
        _lastTicks = now;

        _lastFps = _framesThisSecond;
        _framesThisSecond = 0;
        if (_state == "moving") _bestFps = Math.Max(_bestFps, _lastFps);

        // Skip the first seconds: start-up work is not idle cost.
        if (_state == "idle" && Stopwatch.GetElapsedTime(_start, now).TotalSeconds > 5)
        {
            _idleCpu.Enqueue(cpuMsPer10s);
            while (_idleCpu.Count > 30) _idleCpu.Dequeue();
        }
        else if (_state == "moving")
        {
            _peakMovingCpu = Math.Max(_peakMovingCpu, cpuPct);
        }

        double idleAvg = _idleCpu.Count > 0 ? _idleCpu.Average() : double.NaN;
        double up = Stopwatch.GetElapsedTime(_start, now).TotalSeconds;

        string text =
            $"Mode: {_mode}\n" +
            $"State: {_state}{(HiddenReason != null ? $" ({HiddenReason})" : "")}\n" +
            $"RAM: {ws:F1} MB working set, {priv:F1} MB private\n" +
            $"CPU now: {cpuPct:F2} %  ({cpuMsPer10s:F1} ms per 10 s)\n" +
            $"CPU idle avg (last {_idleCpu.Count}s): {(double.IsNaN(idleAvg) ? "-" : idleAvg.ToString("F1"))} ms per 10 s\n" +
            $"CPU peak while moving: {_peakMovingCpu:F1} %\n" +
            $"FPS now: {_lastFps}   best while moving: {_bestFps}\n" +
            $"Up: {up:F0} s\n\n" +
            "Drag the blob, throw it, click it. Close this window to quit.";
        Native.SetText(_text, text);

        try
        {
            File.AppendAllText(_csv, string.Create(CultureInfo.InvariantCulture,
                $"{up:F0},{_mode},{_state},{cpuPct:F3},{ws:F1},{priv:F1},{_lastFps}\n"));
        }
        catch
        {
            // Metrics are best effort.
        }
    }
}
