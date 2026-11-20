using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>The llama.cpp runtime pins (tools/mind/pins.json, embedded). <see cref="RuntimePins_AreSet"/> is
/// EXPECTED-PENDING until CI's fetch step has cross-checked GitHub's published digests and the values are committed.</summary>
public sealed class RuntimePinsTests
{
    [Fact]
    public void Pins_FourWindowsAssets_FromTheOfficialRelease()
    {
        var p = RuntimePins.Load();
        Assert.Equal("b12207", p.Build);
        Assert.Equal(["vulkan", "cpu", "cuda", "cudart"], p.Assets.Select(a => a.Variant));
        foreach (var a in p.Assets)
        {
            Assert.StartsWith("https://github.com/ggml-org/llama.cpp/releases/download/b12207/", RuntimePins.Url(p, a).ToString());
            Assert.True(a.Bytes > 1_000_000);
            Assert.True(a.Sha256 is null || System.Text.RegularExpressions.Regex.IsMatch(a.Sha256, "^[0-9a-f]{64}$"));
        }
        Assert.Equal(37_844_756, p.Asset("vulkan")!.Bytes);
    }

    [Fact]
    public void CiModel_IsPinned() =>
        Assert.Equal("0ef57d2c838458a1952664260dcba38e5bdda37494f3af732f06e4add24068e3", RuntimePins.Load().CiModel.Sha256);

    [Fact]
    public void RuntimePins_AreSet() =>
        Assert.All(RuntimePins.Load().Assets, a => Assert.False(a.Sha256 is null, $"{a.Name}: SHA-256 not pinned yet (expected-pending)"));
}

/// <summary>0.14.1: mind-eval.yml downloads the real models by tools/mind/pins.json's evalModels; they must be the
/// catalog's own pins (same commit, file, size and SHA-256), or the eval would test another file than he gets.</summary>
public sealed class EvalModelPinsTests
{
    [Fact]
    public void EvalModels_MirrorTheCatalog()
    {
        using var s = typeof(RuntimePins).Assembly.GetManifestResourceStream("Sahar.mind-pins.json")!;
        using var doc = System.Text.Json.JsonDocument.Parse(s);
        var rows = doc.RootElement.GetProperty("evalModels").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("id").GetString() == Sahar.Core.Mind.Models.ModelCatalog.DefaultId);
        foreach (var r in rows)
        {
            var m = Sahar.Core.Mind.Models.ModelCatalog.Find(r.GetProperty("id").GetString()!);
            Assert.NotNull(m);
            Assert.Equal(m!.Repo, r.GetProperty("repo").GetString());
            Assert.Equal(m.Commit, r.GetProperty("commit").GetString());
            Assert.Equal(m.File, r.GetProperty("file").GetString());
            Assert.Equal(m.Bytes, r.GetProperty("bytes").GetInt64());
            Assert.Equal(m.Sha256, r.GetProperty("sha256").GetString());
        }
    }
}
