using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Models;

/// <summary>0.18 (owner: "he only downloads the file"): the big local brain — its pin, llama.cpp's own fit for a model
/// bigger than the GPU, the RAM guard, the longer load deadline and chat budget, and finding the file he downloaded.</summary>
public sealed class Big0180Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-big-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly Gpu Rtx4050Laptop = new(Gpu.Nvidia, 6_012, false, "NVIDIA GeForce RTX 4050 Laptop GPU");
    private static readonly HardwareProbe HisPc = new(32_510, [new Gpu(Gpu.Intel, 128, false, "Intel"), Rtx4050Laptop], true, 200_000);
    private static ModelEntry Big => ModelCatalog.Find(ModelCatalog.BigId)!;

    private static string? Val(IReadOnlyList<string> a, string flag) => a.ToList().IndexOf(flag) is var i && i >= 0 && i + 1 < a.Count ? a[i + 1] : null;

    [Fact]
    public void Pin_IsTheOfficialGgmlOrgFile()
    {
        var b = Big;
        Assert.Equal("qwen3.6-35b-a3b-q4km", ModelCatalog.BigId);
        Assert.Equal("big", b.Role);
        Assert.Equal("ggml-org/Qwen3.6-35B-A3B-GGUF", b.Repo);
        Assert.Equal("52f9f9b8052fb9c8b69bba4a95eda0f319ac3d2a", b.Commit);
        Assert.Equal("Qwen3.6-35B-A3B-Q4_K_M.gguf", b.File);
        Assert.Equal(20_542_732_576, b.Bytes);
        Assert.Equal("f4ffe03c9139e6a905a5d346f77d9fabdd179367c3280dfd005d1e75ca5ed8c8", b.Sha256);
        Assert.Equal("Apache-2.0", b.License);
        Assert.NotEqual(ModelCatalog.DefaultId, ModelCatalog.BigId);
    }

    [Fact]
    public void ForModel_BiggerThanGpu_LetsLlamaCppFit()
    {
        var plan = Tiers.ForModel(Tiers.Plan(HisPc), HisPc, Big.Bytes);
        Assert.Equal(Offload.Auto, plan.Offload);
        Assert.Equal(Backend.Vulkan, plan.Backend);
        var a = ServerArgs.For(plan, "m", 1, "k", threads: 10);
        Assert.DoesNotContain("-ngl", a);
        Assert.Equal("on", Val(a, "--fit"));
        Assert.Equal("1024", Val(a, "--fit-target"));
        Assert.Equal("10", Val(a, "-t"));            // experts compute on the CPU: not capped at 4
        Assert.Equal("0", Val(a, "--poll"));
        Assert.Equal("-1", Val(a, "--prio"));
        Assert.Equal("8192", Val(a, "-c"));
    }

    [Fact]
    public void ForModel_SmallModel_Unchanged()
    {
        var plan = Tiers.Plan(HisPc);
        Assert.Equal(plan, Tiers.ForModel(plan, HisPc, 4_977_171_584));
        Assert.Equal("999", Val(ServerArgs.For(plan, "m", 1, "k", 10), "-ngl"));
    }

    [Fact]
    public void ForModel_CpuOnly_StaysCpu()
    {
        var plan = Tiers.Plan(HisPc, useGpu: false);
        var big = Tiers.ForModel(plan, HisPc, Big.Bytes);
        Assert.Equal(Offload.None, big.Offload);
        Assert.Equal("0", Val(ServerArgs.For(big, "m", 1, "k", 10), "-ngl"));
    }

    [Theory]
    [InlineData(32_510, false)]   // his 32 GB laptop
    [InlineData(23_000, true)]    // 24 GB reported a bit under: 20.5 GB + 4 GB does not fit
    [InlineData(16_000, true)]
    public void TooBig_RamGuard(long ramMB, bool tooBig) =>
        Assert.Equal(tooBig, Tiers.TooBig(HisPc with { RamMB = ramMB }, Big.Bytes));

    [Theory]
    [InlineData(4_977_171_584L, 120)]
    [InlineData(20_542_732_576L, 265)]
    [InlineData(69_376_637_536L, 300)]
    public void LoadDeadline_ScalesWithSize(long bytes, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ServerArgs.LoadDeadline(bytes));

    [Theory]
    [InlineData(4_977_171_584L, 20)]
    [InlineData(20_542_732_576L, 45)]
    public void ChatBudget_LongerForTheBigModel(long bytes, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), ServerArgs.ChatBudget(bytes));

    [Theory]
    [InlineData("Qwen3.6-35B-A3B-Q4_K_M.gguf", 20_542_732_576L, true)]
    [InlineData("Qwen3.6-35B-A3B-Q4_K_M (1).gguf", 20_542_732_576L, true)]
    [InlineData("qwen3.6-35b-a3b-q4_k_m.GGUF", 20_542_732_576L, true)]
    [InlineData("Qwen3.6-35B-A3B-Q4_K_M.gguf", 20_542_732_575L, false)]   // still downloading / cut
    [InlineData("Qwen3.6-35B-A3B-Q4_K_M.gguf.crdownload", 20_542_732_576L, false)]
    [InlineData("Qwen3.6-35B-A3B-Q8_0.gguf", 20_542_732_576L, false)]
    public void Finder_MatchesTheDownloadedFile(string name, long size, bool match) =>
        Assert.Equal(match ? ModelCatalog.BigId : null, BigFinder.Match(name, size)?.Id);

    [Fact]
    public void Finder_Scan_FindsOnlyAWholeFile()
    {
        var pin = ModelCatalog.Recommended[0] with { File = "tiny-Q4_K_M.gguf", Bytes = 16 };
        File.WriteAllBytes(Path.Combine(_dir, "tiny-Q4_K_M.gguf.part"), new byte[16]);
        File.WriteAllBytes(Path.Combine(_dir, "other.gguf"), new byte[16]);
        Assert.Empty(BigFinder.Scan([_dir], [pin]));
        File.WriteAllBytes(Path.Combine(_dir, "tiny-Q4_K_M (2).gguf"), new byte[16]);
        var hit = Assert.Single(BigFinder.Scan([_dir, Path.Combine(_dir, "missing")], [pin]));
        Assert.Equal(pin.Id, hit.Pin.Id);
        Assert.EndsWith("tiny-Q4_K_M (2).gguf", hit.Path);
    }

    [Fact]
    public void Registry_HandPickedPinFile_InstallsThePin()
    {
        var reg = new ModelRegistry(_dir);
        var path = Path.Combine(_dir, "whatever-name.gguf");
        var e = reg.InstallOrAdd(path, Big.Sha256!, Big.Bytes);
        Assert.Equal(ModelCatalog.BigId, e.Id);
        Assert.True(reg.Find(ModelCatalog.BigId)!.Installed);
        Assert.Equal(path, reg.Find(ModelCatalog.BigId)!.LocalPath);
        Assert.DoesNotContain(reg.List(), m => m.Id.StartsWith("file:"));

        var other = reg.InstallOrAdd(Path.Combine(_dir, "x.gguf"), new string('a', 64), 123);
        Assert.StartsWith("file:", other.Id);
    }

    [Fact]
    public void RuntimeText_BigLines()
    {
        Assert.Contains("التنزيلات", RuntimeText.FoundBig);
        Assert.Contains("الصغير", RuntimeText.BigFailed);
        Assert.Contains("رام", RuntimeText.TooBig);
    }
}
