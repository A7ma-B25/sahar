using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>0.15.0 T9: the 0.14.x probe logged vram=0MB on his RTX 4050. Windows' own counter
/// "GPU Process Memory(pid_N*)\Dedicated Usage" says what llama-server holds on each adapter (bytes, one instance per
/// adapter and memory segment): her MB is the sum of llama-server's instances, and only of its own.</summary>
public sealed class GpuMemoryTests
{
    [Fact]
    public void CounterPath_IsTheEnglishPdhPath_ForThatPid()
    {
        Assert.Equal(@"\GPU Process Memory(pid_4321*)\Dedicated Usage", GpuMemory.CounterPath(4321));
        Assert.Equal(@"\GPU Process Memory(*)\Dedicated Usage", GpuMemory.AllPath);
    }

    [Theory]
    [InlineData("pid_4321_luid_0x00000000_0x0000F0E6_phys_0", 4321)]
    [InlineData("PID_4321_LUID_0x00000000_0x0000F0E6_PHYS_1", 4321)]
    [InlineData("pid_7_luid_0x00000000_0x00011B2C_phys_0", 7)]
    [InlineData("pid_4321", 4321)]
    public void Pid_FromTheInstanceName(string instance, int pid) => Assert.Equal(pid, GpuMemory.Pid(instance));

    [Theory]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("luid_0x00000000_0x0000F0E6_phys_0")]
    [InlineData("pid__luid")]
    [InlineData("pid_12x_luid")]
    [InlineData("pid_99999999999_luid")]
    public void Pid_NoneForOtherNames(string instance) => Assert.Null(GpuMemory.Pid(instance));

    [Fact]
    public void SumMB_AddsEveryAdapterOfThatProcess()
    {
        (string, long)[] items =
        [
            ("pid_4321_luid_0x00000000_0x0000F0E6_phys_0", 3_000L << 20),   // the RTX
            ("pid_4321_luid_0x00000000_0x00011B2C_phys_0", 128L << 20),     // the Intel iGPU's share
        ];
        Assert.Equal(3_128, GpuMemory.SumMB(4321, items));
    }

    [Fact]
    public void SumMB_IgnoresOtherPidsThatTheWildcardAlsoMatches()
    {
        // "pid_12*" matches pid_123 and pid_1200 too: only pid 12 itself counts.
        (string, long)[] items =
        [
            ("pid_12_luid_0x00000000_0x0000F0E6_phys_0", 500L << 20),
            ("pid_123_luid_0x00000000_0x0000F0E6_phys_0", 900L << 20),
            ("pid_1200_luid_0x00000000_0x0000F0E6_phys_0", 700L << 20),
            ("_Total", 9_000L << 20),
        ];
        Assert.Equal(500, GpuMemory.SumMB(12, items));
    }

    [Fact]
    public void SumMB_RoundsToTheNearestMB_AndSkipsNegativeOrEmpty()
    {
        (string, long)[] items =
        [
            ("pid_5_luid_a_phys_0", (10L << 20) + (600L << 10)),   // 10.59 MB
            ("pid_5_luid_b_phys_0", -1),
            ("pid_5_luid_c_phys_0", 0),
        ];
        Assert.Equal(11, GpuMemory.SumMB(5, items));
        Assert.Equal(0, GpuMemory.SumMB(5, []));
        Assert.Equal(0, GpuMemory.SumMB(0, items));
    }

    [Theory]
    [InlineData(3_100, 2_900, 2_800, 3_100, "pdh")]
    [InlineData(0, 2_900, 2_800, 2_900, "buffers")]
    [InlineData(0, 0, 2_800, 2_800, "free-delta")]
    [InlineData(0, 0, 0, 0, "none")]
    public void Best_PrefersWindowsCounter(int pdh, int buffers, int delta, int want, string source)
    {
        var (mb, from) = GpuMemory.Best(pdh, buffers, delta);
        Assert.Equal(want, mb);
        Assert.Equal(source, from);
    }
}
