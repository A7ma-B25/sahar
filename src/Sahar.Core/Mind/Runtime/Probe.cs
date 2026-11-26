namespace Sahar.Core.Mind.Runtime;

/// <summary>
/// Review I3: a measurement that asks Windows (the PDH GPU counter) runs off the caller's thread and is waited for at
/// most <c>wait</c>: a stuck or failing probe gives 0 (the caller falls back), never a late ready or an exception.
/// </summary>
public static class Probe
{
    public static async Task<int> WithinAsync(Func<int> probe, TimeSpan wait)
    {
        var run = Task.Run(probe);
        try
        {
            var done = await Task.WhenAny(run, Task.Delay(wait)).ConfigureAwait(false);
            return done == run && run.IsCompletedSuccessfully ? run.Result : 0;
        }
        catch (Exception) { return 0; }
        finally { _ = run.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); }   // a late failure is observed, never thrown
    }

    public static int Within(Func<int> probe, TimeSpan wait)
    {
        var run = Task.Run(probe);
        try { return run.Wait(wait) && run.IsCompletedSuccessfully ? run.Result : 0; }
        catch (Exception) { return 0; }
        finally { _ = run.ContinueWith(t => _ = t.Exception, TaskScheduler.Default); }
    }
}
