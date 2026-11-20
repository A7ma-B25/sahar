using System.Globalization;
using System.Text.RegularExpressions;
using Sahar.Core.Mind.Models;

namespace Sahar.Core.Mind.Runtime;

/// <summary>One GPU as <c>llama-server --list-devices</c> prints it: "  Vulkan1: NVIDIA GeForce RTX 4050 Laptop GPU
/// (6141 MiB, 5870 MiB free)". Hardware names only (never personal).</summary>
public sealed record LlamaDevice(string Id, string Name, long TotalMB, long FreeMB);

/// <summary>
/// 0.14.1 fix 7: which GPU she runs on. llama.cpp's Vulkan build uses every dedicated AND integrated GPU by default
/// (ggml-vulkan.cpp, "Default to using all dedicated GPUs" — integrated included), so on an Optimus laptop her layers
/// were split onto the Intel iGPU. She names one device: the DXGI adapter the tier chose (by name), else the discrete
/// one with the most memory; a PC with only an integrated GPU gets none (the tier keeps such a PC on the CPU anyway).
/// Pure and table-tested.
/// </summary>
public static partial class Devices
{
    [GeneratedRegex(@"^\s*((?:Vulkan|CUDA|ROCm|SYCL)\d+):\s*(.+?)\s*\((\d+)\s*MiB,\s*(\d+)\s*MiB free\)\s*$", RegexOptions.Multiline)]
    private static partial Regex Line();

    public static IReadOnlyList<LlamaDevice> Parse(string output) =>
        Line().Matches(output ?? "").Select(m => new LlamaDevice(m.Groups[1].Value, m.Groups[2].Value.Trim(),
            long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), long.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture))).ToList();

    public static LlamaDevice? Pick(IReadOnlyList<LlamaDevice> list, Gpu? discrete)
    {
        if (list.Count == 0) return null;
        if (discrete is { Name.Length: > 0 } g)
        {
            var want = Norm(g.Name);
            var named = list.FirstOrDefault(d => Norm(d.Name) == want) ?? list.FirstOrDefault(d => Norm(d.Name).Contains(want) || want.Contains(Norm(d.Name)));
            if (named is not null) return named;
        }
        return list.Where(d => DiscreteName(d.Name)).OrderByDescending(d => d.TotalMB).FirstOrDefault();
    }

    /// <summary>NVIDIA and AMD discrete GPUs by name (an AMD iGPU says "Radeon … Graphics"; Intel is integrated here,
    /// as <see cref="Gpu.Discrete"/> counts it).</summary>
    public static bool DiscreteName(string name)
    {
        var n = name.ToUpperInvariant();
        if (n.Contains("NVIDIA") || n.Contains("GEFORCE") || n.Contains("QUADRO") || n.Contains("RTX")) return true;
        return n.Contains("RADEON") && !n.Contains("GRAPHICS");
    }

    /// <summary>What her model took on that device: its free memory before the load minus after (0 when unknown).</summary>
    public static int UsedMB(IReadOnlyList<LlamaDevice> before, IReadOnlyList<LlamaDevice> after, string? id)
    {
        if (id is null || before.FirstOrDefault(d => d.Id == id) is not { } b || after.FirstOrDefault(d => d.Id == id) is not { } a) return 0;
        return (int)Math.Max(0, b.FreeMB - a.FreeMB);
    }

    private static string Norm(string s) => string.Join(' ', s.ToUpperInvariant().Replace("(R)", "").Replace("(TM)", "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
