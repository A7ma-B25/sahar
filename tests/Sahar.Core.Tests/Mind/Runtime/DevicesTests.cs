using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>0.14.1 fix 7: on his Optimus laptop llama.cpp's Vulkan build uses every dedicated AND integrated GPU by
/// default (ggml-vulkan.cpp), so her layers were split onto the Intel iGPU. She names the discrete one
/// (<c>--device VulkanN</c>) from <c>llama-server --list-devices</c>, matched to the DXGI adapter the tier chose.</summary>
public sealed class DevicesTests
{
    private const string Optimus = """
        ggml_vulkan: Found 2 Vulkan devices:
        Available devices:
          Vulkan0: Intel(R) UHD Graphics (16263 MiB, 15800 MiB free)
          Vulkan1: NVIDIA GeForce RTX 4050 Laptop GPU (6141 MiB, 5870 MiB free)
        """;
    private static readonly Gpu Rtx = new(Gpu.Nvidia, 6_141, false, "NVIDIA GeForce RTX 4050 Laptop GPU");

    [Fact]
    public void Parse_TheListDevicesLines()
    {
        var d = Devices.Parse(Optimus);
        Assert.Equal(["Vulkan0", "Vulkan1"], d.Select(x => x.Id));
        Assert.Equal("NVIDIA GeForce RTX 4050 Laptop GPU", d[1].Name);
        Assert.Equal(6141, d[1].TotalMB);
        Assert.Equal(5870, d[1].FreeMB);
        Assert.Empty(Devices.Parse("Available devices:\n  (none)\n"));
    }

    [Fact]
    public void Pick_Optimus_TheNvidia_NotTheIntel() => Assert.Equal("Vulkan1", Devices.Pick(Devices.Parse(Optimus), Rtx)!.Id);

    [Fact]
    public void Pick_TheRtxAlone() =>
        Assert.Equal("Vulkan0", Devices.Pick(Devices.Parse("  Vulkan0: NVIDIA GeForce RTX 4050 Laptop GPU (6141 MiB, 5870 MiB free)"), Rtx)!.Id);

    [Fact]
    public void Pick_NoDxgiName_StillTheDiscreteOne()
    {
        var list = Devices.Parse("  Vulkan0: AMD Radeon(TM) Graphics (8000 MiB, 7000 MiB free)\n  Vulkan1: AMD Radeon RX 7600 (8176 MiB, 8000 MiB free)");
        Assert.Equal("Vulkan1", Devices.Pick(list, new Gpu(Gpu.Amd, 8_176, false))!.Id);
        Assert.Equal("Vulkan1", Devices.Pick(Devices.Parse(Optimus), null)!.Id);
    }

    [Fact]
    public void Pick_OnlyAnIntegratedGpu_NoDevice() =>
        Assert.Null(Devices.Pick(Devices.Parse("  Vulkan0: Intel(R) Iris(R) Xe Graphics (8000 MiB, 7900 MiB free)"), null));

    [Fact]
    public void Args_NameTheDevice_AndOffloadAll()
    {
        var plan = Tiers.Plan(new HardwareProbe(32_510, [new Gpu(Gpu.Intel, 128, false, "Intel(R) UHD Graphics"), Rtx], true, 200_000));
        var a = ServerArgs.For(plan, "m", 1, "k", 6, device: "Vulkan1");
        Assert.Equal("Vulkan1", a[a.ToList().IndexOf("--device") + 1]);
        Assert.Equal("999", a[a.ToList().IndexOf("-ngl") + 1]);
        Assert.DoesNotContain("--device", ServerArgs.For(plan with { Backend = Backend.Cpu, Offload = Offload.None }, "m", 1, "k", 6, device: "Vulkan1"));
    }

    [Fact]
    public void Tier_HisLaptop_Gpu6_RunsE4B_AllLayers()
    {
        var plan = Tiers.Plan(new HardwareProbe(32_510, [new Gpu(Gpu.Intel, 128, false), Rtx], true, 200_000));
        Assert.Equal(Tier.Gpu6, plan.Tier);
        Assert.Equal(ModelCatalog.DefaultId, plan.ModelId);
        Assert.Equal("gemma-4-e4b-q4km", plan.ModelId);
        Assert.Equal(Offload.All, plan.Offload);
    }

    [Fact]
    public void Vram_TheFreeDrop_OnThatDevice() =>
        Assert.Equal(3_100, Devices.UsedMB(Devices.Parse(Optimus), Devices.Parse(Optimus.Replace("5870 MiB free", "2770 MiB free")), "Vulkan1"));
}
