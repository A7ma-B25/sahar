using System.Diagnostics;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Motion;

/// <summary>
/// A high-resolution waitable timer the message loop waits on. It runs only while something CPU-driven is
/// moving and is cancelled when everything rests, so the engine sleeps between motions.
/// </summary>
internal sealed unsafe class Ticker : IDisposable
{
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2;
    private const uint TIMER_ALL_ACCESS = 0x1F0003;

    private readonly Stopwatch _clock = new();
    private int _periodMs;

    public nint Handle { get; }
    public bool Running => _periodMs > 0;
    public event Action<float>? Tick;

    public Ticker()
    {
        Handle = Native.CreateWaitableTimerEx(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (Handle == 0)
        {
            Log.Write("high-resolution timer unavailable, using a normal one");
            Handle = Native.CreateWaitableTimerEx(0, null, 0, TIMER_ALL_ACCESS);
        }
    }

    /// <summary>Runs every <paramref name="periodMs"/> ms (16 ≈ 60 Hz) until <see cref="Stop"/>.</summary>
    public void Start(int periodMs = 16)
    {
        if (_periodMs == periodMs) return;
        _periodMs = periodMs;
        long due = -periodMs * 10_000L;   // relative, 100 ns units
        Native.SetWaitableTimer(Handle, &due, periodMs, 0, 0, 0);
        _clock.Restart();
    }

    public void Stop()
    {
        if (_periodMs == 0) return;
        _periodMs = 0;
        Native.CancelWaitableTimer(Handle);
    }

    /// <summary>Called by the message loop when the timer fires.</summary>
    public void Fire()
    {
        float dt = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        Tick?.Invoke(Math.Clamp(dt, 0.001f, 0.1f));
    }

    public void Dispose() => Native.CloseHandle(Handle);
}
