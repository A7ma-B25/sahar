using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Models;

/// <summary>A display adapter as DXGI reports it (vendor id, dedicated video memory, software adapter).</summary>
public sealed record Gpu(int Vendor, long DedicatedMB, bool Software, string Name = "")
{
    public const int Nvidia = 0x10DE, Amd = 0x1002, Intel = 0x8086;
    /// <summary>A discrete GPU worth offloading to: NVIDIA or AMD with its own memory (Intel iGPUs report ~128 MB).</summary>
    public bool Discrete => !Software && Vendor is Nvidia or Amd && DedicatedMB >= 1024;
}

/// <summary>The probe's numbers (the Mind fills them: GlobalMemoryStatusEx, DXGI EnumAdapters1, Avx2, free disk).</summary>
public sealed record HardwareProbe(long RamMB, IReadOnlyList<Gpu> Gpus, bool Avx2, long FreeDiskMB);

/// <summary>How many layers go to the GPU: none, as many as fit (<c>--fit on</c>), or all (<c>-ngl 999</c>).</summary>
public enum Offload { None, Fit, All }

public sealed record TierPlan(Tier Tier, string? ModelId, int Context, Offload Offload, Backend Backend);

/// <summary>
/// Spec §4.3, pure and table-tested. Reported sizes run a little under the label (an "8 GB" PC shows ~7.9 GiB, a
/// "6 GB" laptop GPU ~5.9 GiB), so the thresholds sit just under the labels.
/// </summary>
public static class Tiers
{
    public const long MinRamMB = 7_500, StandardRamMB = 15_000, MinDiskMB = 6_144;
    public const long Gpu6MB = 5_800, Gpu8MB = 7_800, FitGpuMB = 3_800;

    public static Tier Pick(HardwareProbe p)
    {
        if (p.RamMB < MinRamMB || !p.Avx2 || p.FreeDiskMB < MinDiskMB) return Tier.None;
        long gpu = Best(p)?.DedicatedMB ?? 0;
        if (gpu >= Gpu8MB && p.RamMB >= StandardRamMB) return Tier.Gpu8;
        if (gpu >= Gpu6MB && p.RamMB >= StandardRamMB) return Tier.Gpu6;
        return p.RamMB >= StandardRamMB ? Tier.StandardCpu : Tier.Light;
    }

    /// <summary>The discrete GPU with the most memory, or null.</summary>
    public static Gpu? Best(HardwareProbe p) => p.Gpus.Where(g => g.Discrete).OrderByDescending(g => g.DedicatedMB).FirstOrDefault();

    /// <summary>What the tier runs. <paramref name="useGpu"/> off («استعملي كرت الشاشة») and a backend he chose win.</summary>
    public static TierPlan Plan(HardwareProbe p, bool useGpu = true, Backend backend = Backend.Auto)
    {
        var tier = Pick(p);
        bool gpu = useGpu && Best(p) is { } g && g.DedicatedMB >= FitGpuMB;
        var plan = tier switch
        {
            Tier.None => new TierPlan(tier, null, 0, Offload.None, Backend.Cpu),
            Tier.Light => new TierPlan(tier, "gemma-4-e2b-q4km", 4096, Offload.None, Backend.Cpu),
            Tier.StandardCpu => new TierPlan(tier, ModelCatalog.DefaultId, 8192, gpu ? Offload.Fit : Offload.None, gpu ? Backend.Vulkan : Backend.Cpu),
            Tier.Gpu6 => new TierPlan(tier, ModelCatalog.DefaultId, 8192, gpu ? Offload.All : Offload.None, gpu ? Backend.Vulkan : Backend.Cpu),
            _ => new TierPlan(tier, "gemma-4-e4b-q5km", 16384, gpu ? Offload.All : Offload.None, gpu ? Backend.Vulkan : Backend.Cpu),
        };
        if (tier != Tier.None && backend != Backend.Auto && (gpu || backend == Backend.Cpu))
            plan = plan with { Backend = backend, Offload = backend == Backend.Cpu ? Offload.None : plan.Offload };
        return plan;
    }
}
