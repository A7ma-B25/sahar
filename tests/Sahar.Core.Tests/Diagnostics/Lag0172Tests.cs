using Sahar.Core.Activities;
using Sahar.Core.Diagnostics;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Diagnostics;

/// <summary>0.17.2 (owner: other apps lag, File Explorer worst; hiding did not help, quitting did): the copy poll's
/// visible-only rule, the sensors that stop with their toggle and with «خبّيني ساعة», a lighter llama-server, and the
/// numbers-only perf.txt.</summary>
public sealed class Lag0172Tests
{
    private static readonly DateTimeOffset T = new(2026, 11, 28, 3, 0, 0, TimeSpan.FromHours(3));

    // ---- the copy dialog: only a visible one, and Explorer's reused hidden window is not a latch ----

    [Fact]
    public void Copy_HiddenDialog_IsNotAccepted()
    {
        var w = new CopyWatch();
        Assert.False(w.Accept(42, visible: false));   // Explorer keeps a hidden OperationStatusWindow: «copy: started» forever
        Assert.Equal(0, w.Window);
        Assert.True(w.Accept(42, visible: true));
    }

    [Fact]
    public void Copy_SameWindowReusedAfterItHid_IsAcceptedAgain()
    {
        var w = new CopyWatch();
        Assert.True(w.Accept(42, visible: true));
        Assert.Equal(CopyStep.Done, w.Tick(true, "100% complete", T).Step);
        Assert.False(w.Accept(42, visible: true));    // still showing 100 %: not again
        Assert.False(w.Accept(42, visible: false));   // it hid (Explorer keeps it for the next copy)…
        Assert.True(w.Accept(42, visible: true));     // …and the next copy in the same window is seen
    }

    [Fact]
    public void Copy_DialogHidingIsTheEnd()
    {
        var w = new CopyWatch();
        w.Accept(7, visible: true);
        Assert.Equal(CopyStep.Progress, w.Tick(true, "40% complete", T).Step);
        Assert.Contains(w.Tick(false, "", T.AddSeconds(2)).Step, new[] { CopyStep.Done, CopyStep.Closed });   // the caller passes exists && visible
        Assert.Equal(0, w.Window);
    }

    [Fact]
    public void Copy_PollEvery2s() => Assert.Equal(TimeSpan.FromSeconds(2), CopyWatch.PollEvery);

    // ---- sensors: each follows its toggle, and all rest while he hid her ----

    [Fact]
    public void Sensors_FollowTheirToggles()
    {
        var s = new SaharSettings { ActCopy = false, ActMusic = true, ActSystem = false, ActMicCam = true };
        var p = SensorPlan.For(s, askedHidden: false);
        Assert.False(p.Copy);
        Assert.True(p.Music);
        Assert.False(p.System);
        Assert.True(p.CameraMic);
        Assert.True(p.Fullscreen);
        Assert.False(p.UnloadMind);
    }

    [Fact]
    public void Sensors_AllRest_WhileHeHidHer_AndTheMindUnloads()
    {
        var p = SensorPlan.For(new SaharSettings(), askedHidden: true);
        Assert.False(p.Copy || p.Music || p.System || p.CameraMic || p.Fullscreen);
        Assert.True(p.UnloadMind);
        var back = SensorPlan.For(new SaharSettings(), askedHidden: false);
        Assert.True(back.Copy && back.Music && back.System && back.CameraMic && back.Fullscreen);
    }

    // ---- llama-server: few threads with the GPU, no busy-wait, low priority ----

    private static readonly HardwareProbe HisPc = new(32_510, [new Gpu(Gpu.Nvidia, 6_012, false)], true, 200_000);
    private static string Val(IReadOnlyList<string> a, string flag) => a[a.ToList().IndexOf(flag) + 1];

    [Fact]
    public void Server_AllOnGpu_FourThreads_NoPoll_LowPriority()
    {
        var a = ServerArgs.For(Tiers.Plan(HisPc), "m", 1, "k", threads: 10);
        Assert.Equal("4", Val(a, "-t"));
        Assert.Equal("4", Val(a, "-tb"));
        Assert.Equal("0", Val(a, "--poll"));
        Assert.Equal("-1", Val(a, "--prio"));
    }

    [Fact]
    public void Server_Cpu_KeepsItsThreads_NoPoll_LowPriority()
    {
        var a = ServerArgs.For(Tiers.Plan(HisPc, useGpu: false), "m", 1, "k", threads: 6);
        Assert.Equal("6", Val(a, "-t"));
        Assert.Equal("6", Val(a, "-tb"));
        Assert.Equal("0", Val(a, "--poll"));
        Assert.Equal("-1", Val(a, "--prio"));
    }

    // ---- perf.txt ----

    [Fact]
    public void Perf_TimesCountsAndStalls()
    {
        var p = new PerfCounters(T);
        p.Time("copy.poll", 0.4);
        p.Time("copy.poll", 1.6);
        p.Count("hook.foreground", 30);
        for (int i = 1; i <= 100; i++) p.Stall(i);
        var text = p.Text(T.AddMinutes(10), [new("llama-server threads", "6")]);
        Assert.Contains("copy.poll calls=2 perMin=0.2 totalMs=2.0 maxMs=1.6", text);
        Assert.Contains("hook.foreground calls=30 perMin=3.0", text);
        Assert.Contains("ui stalls: samples=100 p95=95ms max=100ms", text);
        Assert.Contains("llama-server threads: 6", text);
    }

    [Fact]
    public void Perf_InTheBundle_Scrubbed()
    {
        var entries = DiagBundle.Entries(new DiagSources { Perf = @"ui stalls: samples=1 p95=3ms max=3ms C:\Users\ammar\x" });
        var perf = Assert.Single(entries, e => e.Name == "perf.txt");
        Assert.DoesNotContain("ammar", perf.Text);
    }
}
