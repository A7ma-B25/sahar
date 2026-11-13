using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Models;

/// <summary>Spec §4.3: the tier from the hardware probe, and what each tier runs.</summary>
public sealed class TiersTests
{
    private static HardwareProbe Pc(long ramMB, bool avx2 = true, long diskMB = 200_000, params Gpu[] gpus) => new(ramMB, gpus, avx2, diskMB);
    private static readonly Gpu Rtx4050Laptop = new(Gpu.Nvidia, 6_012, false, "NVIDIA GeForce RTX 4050 Laptop GPU");
    private static readonly Gpu IntelIgpu = new(Gpu.Intel, 128, false, "Intel(R) UHD Graphics");
    private static readonly Gpu BasicRender = new(0x1414, 0, true, "Microsoft Basic Render Driver");

    [Fact]
    public void Pick_HisPc_Gpu6()
    {
        var pc = Pc(32_510, gpus: [IntelIgpu, Rtx4050Laptop]);
        Assert.Equal(Tier.Gpu6, Tiers.Pick(pc));
        var plan = Tiers.Plan(pc);
        Assert.Equal(ModelCatalog.DefaultId, plan.ModelId);
        Assert.Equal("gemma-4-e4b-q4km", plan.ModelId);
        Assert.Equal(Offload.All, plan.Offload);
        Assert.Equal(Backend.Vulkan, plan.Backend);
        Assert.Equal(8192, plan.Context);
    }

    [Fact]
    public void Pick_8GbIgpu_Light()
    {
        var pc = Pc(7_890, gpus: [IntelIgpu]);
        Assert.Equal(Tier.Light, Tiers.Pick(pc));
        var plan = Tiers.Plan(pc);
        Assert.Equal("gemma-4-e2b-q4km", plan.ModelId);
        Assert.Equal(4096, plan.Context);
        Assert.Equal(Backend.Cpu, plan.Backend);
        Assert.Equal(Offload.None, plan.Offload);
    }

    [Fact]
    public void Pick_4GbDgpu_StandardCpuFit()
    {
        var pc = Pc(16_100, gpus: [new Gpu(Gpu.Amd, 4_080, false)]);
        Assert.Equal(Tier.StandardCpu, Tiers.Pick(pc));
        var plan = Tiers.Plan(pc);
        Assert.Equal("gemma-4-e4b-q4km", plan.ModelId);
        Assert.Equal(Offload.Fit, plan.Offload);
        Assert.Equal(Backend.Vulkan, plan.Backend);
    }

    [Fact]
    public void Pick_16GbNoGpu_StandardCpuOnCpu()
    {
        var plan = Tiers.Plan(Pc(16_100, gpus: [IntelIgpu]));
        Assert.Equal(Tier.StandardCpu, plan.Tier);
        Assert.Equal(Backend.Cpu, plan.Backend);
        Assert.Equal(Offload.None, plan.Offload);
    }

    [Fact]
    public void Pick_8GbGpu_Gpu8_Q5()
    {
        var plan = Tiers.Plan(Pc(32_000, gpus: [new Gpu(Gpu.Nvidia, 8_188, false)]));
        Assert.Equal(Tier.Gpu8, plan.Tier);
        Assert.Equal("gemma-4-e4b-q5km", plan.ModelId);
        Assert.Equal(16384, plan.Context);
    }

    [Fact]
    public void Pick_6GbGpuButLittleRam_NotGpu6() => Assert.Equal(Tier.Light, Tiers.Pick(Pc(12_000, gpus: [Rtx4050Laptop])));

    [Fact]
    public void Pick_NoAvx2_None() => Assert.Equal(Tier.None, Tiers.Pick(Pc(32_000, avx2: false, gpus: [Rtx4050Laptop])));

    [Fact]
    public void Pick_SmallDisk_None() => Assert.Equal(Tier.None, Tiers.Pick(Pc(32_000, diskMB: 5_000, gpus: [Rtx4050Laptop])));

    [Fact]
    public void Pick_LittleRam_None() => Assert.Equal(Tier.None, Tiers.Pick(Pc(4_000)));

    [Fact]
    public void Pick_SoftwareAdapterIgnored() =>
        Assert.Equal(Tier.StandardCpu, Tiers.Pick(Pc(16_100, gpus: [BasicRender with { DedicatedMB = 8_000 }])));

    [Fact]
    public void Plan_None_HasNoModel() => Assert.Null(Tiers.Plan(Pc(4_000)).ModelId);

    [Fact]
    public void Plan_UserTurnedGpuOff_RunsOnCpu()
    {
        var plan = Tiers.Plan(Pc(32_510, gpus: [Rtx4050Laptop]), useGpu: false);
        Assert.Equal(Backend.Cpu, plan.Backend);
        Assert.Equal(Offload.None, plan.Offload);
    }

    [Fact]
    public void Plan_BackendChoiceWins()
    {
        var plan = Tiers.Plan(Pc(32_510, gpus: [Rtx4050Laptop]), backend: Backend.Cuda);
        Assert.Equal(Backend.Cuda, plan.Backend);
    }
}
