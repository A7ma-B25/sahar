using System.Diagnostics;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>Review I3: the VRAM probe (Windows' PDH) ran synchronously before her brain said ready; a stuck counter
/// must cost at most its wait, and a failing one nothing but a 0.</summary>
public sealed class ProbeTests
{
    [Fact]
    public async Task Fast_GivesItsValue() => Assert.Equal(5300, await Probe.WithinAsync(() => 5300, TimeSpan.FromSeconds(2)));

    [Fact]
    public async Task Stuck_GivesZero_WithinTheWait()
    {
        var sw = Stopwatch.StartNew();
        Assert.Equal(0, await Probe.WithinAsync(() => { Thread.Sleep(5000); return 1; }, TimeSpan.FromMilliseconds(300)));
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Throwing_GivesZero() =>
        Assert.Equal(0, await Probe.WithinAsync(() => throw new InvalidOperationException("pdh"), TimeSpan.FromSeconds(1)));

    [Fact]
    public void Sync_Stuck_GivesZero() => Assert.Equal(0, Probe.Within(() => { Thread.Sleep(3000); return 1; }, TimeSpan.FromMilliseconds(200)));
}
