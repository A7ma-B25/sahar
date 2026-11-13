using System.Collections.Concurrent;

namespace Sahar.Settings;

/// <summary>Awaits come back to the window's thread: each continuation is queued and the window is poked with
/// <c>WM_APP + 1</c>; its window procedure runs <see cref="Drain"/> (WebView2 lives on that STA thread).</summary>
internal sealed class UiSyncContext : SynchronizationContext
{
    public const uint WM_RUN = Native.WM_APP + 1;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public nint Hwnd { get; set; }

    public bool OnUiThread => Environment.CurrentManagedThreadId == _thread;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Hwnd != 0) Native.PostMessageW(Hwnd, WM_RUN, 0, 0);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (OnUiThread) { d(state); return; }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ => { try { d(state); } catch (Exception e) { error = e; } finally { done.Set(); } }, null);
        done.Wait();
        if (error is not null) throw error;
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs everything queued (on the UI thread, from the window procedure). A failing continuation is logged.</summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var item))
        {
            try { item.Callback(item.State); }
            catch (Exception e) { SettingsLog.Write($"error: {e.GetType().Name}: {e.Message}"); }
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and returns its result (from any thread).</summary>
    public Task<T> Run<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { tcs.SetResult(await action()); }
            catch (Exception e) { tcs.SetException(e); }
        }, null);
        return tcs.Task;
    }
}
