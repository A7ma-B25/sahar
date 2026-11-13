using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Models;

/// <summary>The models page's list (owner: any model): the recommended pins, add by Hugging Face link/name or a local
/// .gguf, one active, benchmarks, and switching never touches anything personal.</summary>
public sealed class ModelRegistryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-models-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Catalog_PinsAreCompleteAndApache()
    {
        Assert.Contains(ModelCatalog.Recommended, m => m.Id == ModelCatalog.DefaultId && m.Role == "default");
        Assert.Contains(ModelCatalog.Recommended, m => m.Role == "challenger" && m.Name.StartsWith("Jais 3"));
        Assert.Contains(ModelCatalog.Recommended, m => m.Role == "light");
        foreach (var m in ModelCatalog.Recommended)
        {
            Assert.Matches("^[0-9a-f]{64}$", m.Sha256!);
            Assert.Matches("^[0-9a-f]{40}$", m.Commit!);
            Assert.EndsWith(".gguf", m.File);
            Assert.True(m.Bytes > 1_000_000_000);
            Assert.Equal("Apache-2.0", m.License);
            Assert.StartsWith("https://huggingface.co/" + m.Repo + "/resolve/" + m.Commit + "/", ModelCatalog.Url(m).ToString());
        }
        var e4b = ModelCatalog.Find(ModelCatalog.DefaultId)!;
        Assert.Equal(4_977_171_584, e4b.Bytes);
        Assert.Equal("85a896a047553e842f25297ee5b031d64ff30147d9c4af17b1e4b394cd1fab87", e4b.Sha256);
    }

    [Theory]
    [InlineData("https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF", "unsloth", "gemma-4-E4B-it-GGUF", null, null, null)]
    [InlineData("https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/blob/main/gemma-4-E4B-it-Q4_K_M.gguf", "unsloth", "gemma-4-E4B-it-GGUF", "main", "gemma-4-E4B-it-Q4_K_M.gguf", null)]
    [InlineData("https://huggingface.co/IFM/Jais-3-7B-GGUF/resolve/e171351744258b67bb6eb445e76d9d122bf542c0/Jais-3-7B-Q4_K_M.gguf?download=true", "IFM", "Jais-3-7B-GGUF", "e171351744258b67bb6eb445e76d9d122bf542c0", "Jais-3-7B-Q4_K_M.gguf", null)]
    [InlineData("hf.co/bartowski/Qwen_Qwen3.5-4B-GGUF", "bartowski", "Qwen_Qwen3.5-4B-GGUF", null, null, null)]
    [InlineData("  bartowski/Qwen_Qwen3.5-4B-GGUF  ", "bartowski", "Qwen_Qwen3.5-4B-GGUF", null, null, null)]
    [InlineData("bartowski/Qwen_Qwen3.5-4B-GGUF:Q5_K_M", "bartowski", "Qwen_Qwen3.5-4B-GGUF", null, null, "Q5_K_M")]
    [InlineData("bartowski/Qwen_Qwen3.5-4B-GGUF/Qwen_Qwen3.5-4B-Q4_K_M.gguf", "bartowski", "Qwen_Qwen3.5-4B-GGUF", null, "Qwen_Qwen3.5-4B-Q4_K_M.gguf", null)]
    public void Hf_ParsesLinksAndNames(string text, string owner, string repo, string? rev, string? file, string? quant)
    {
        var r = HfRef.Parse(text)!;
        Assert.Equal((owner, repo, rev, file, quant), (r.Owner, r.Repo, r.Revision, r.File, r.Quant));
    }

    [Theory]
    [InlineData("https://evil.example/unsloth/gemma")]
    [InlineData("http://huggingface.co/unsloth/gemma")]
    [InlineData("https://huggingface.co.evil.example/a/b")]
    [InlineData("unsloth")]
    [InlineData("../../etc/passwd")]
    [InlineData("a/b/../../c.gguf")]
    [InlineData("a b/c")]
    [InlineData("https://huggingface.co/datasets/x/y")]
    [InlineData("")]
    public void Hf_RejectsOtherHostsAndJunk(string text) => Assert.Null(HfRef.Parse(text));

    [Theory]
    [InlineData(@"C:\Users\him\Downloads\model.gguf", true)]
    [InlineData(@"D:\models\Llama-3.2-3B-Q4_K_M.GGUF", true)]
    [InlineData("\"D:\\models\\x.gguf\"", true)]
    [InlineData(@"C:\Users\him\notes.txt", false)]
    [InlineData("unsloth/gemma-4-E4B-it-GGUF", false)]
    public void LocalPath_OnlyGguf(string text, bool ok) => Assert.Equal(ok, ModelRegistry.LocalGguf(text) is not null);

    [Fact]
    public void Registry_StartsWithRecommended_NoneActive()
    {
        var reg = new ModelRegistry(_dir);
        Assert.Equal(ModelCatalog.Recommended.Count, reg.List().Count);
        Assert.Null(reg.Active);
    }

    [Fact]
    public void Registry_UseKeepsOneActive_AndSurvivesReload()
    {
        var reg = new ModelRegistry(_dir);
        reg.MarkInstalled(ModelCatalog.DefaultId, Path.Combine(_dir, "a.gguf"));
        reg.MarkInstalled("gemma-4-e2b-q4km", Path.Combine(_dir, "b.gguf"));
        Assert.True(reg.Use(ModelCatalog.DefaultId));
        Assert.True(reg.Use("gemma-4-e2b-q4km"));
        var again = new ModelRegistry(_dir);
        Assert.Single(again.List(), m => m.Active);
        Assert.Equal("gemma-4-e2b-q4km", again.Active!.Id);
    }

    [Fact]
    public void Registry_UseNeedsInstalled() => Assert.False(new ModelRegistry(_dir).Use(ModelCatalog.DefaultId));

    [Fact]
    public void Registry_AddHf_AndLocal_ThenRemove()
    {
        var reg = new ModelRegistry(_dir);
        var hf = reg.AddHf("bartowski", "Qwen_Qwen3.5-4B-GGUF", new string('a', 40), "Qwen_Qwen3.5-4B-Q4_K_M.gguf", 3_010_000_000, new string('b', 64), "apache-2.0");
        Assert.Equal("hf:bartowski/Qwen_Qwen3.5-4B-GGUF/Qwen_Qwen3.5-4B-Q4_K_M.gguf", hf.Id);
        Assert.Equal("custom", hf.Role);
        Assert.Equal("Q4_K_M", hf.Quant);
        var local = reg.AddLocal(@"D:\models\Llama-3.2-3B-Q4_K_M.gguf", new string('c', 64), 2_020_000_000);
        Assert.StartsWith("file:", local.Id);
        Assert.True(local.Installed);
        Assert.Equal(ModelOrigin.Local, local.Origin);
        Assert.Equal(hf.Id, reg.AddHf("bartowski", "Qwen_Qwen3.5-4B-GGUF", new string('a', 40), "Qwen_Qwen3.5-4B-Q4_K_M.gguf", 3_010_000_000, new string('b', 64), "apache-2.0").Id);   // once
        Assert.True(reg.Remove(hf.Id));
        Assert.False(reg.Remove(ModelCatalog.DefaultId));   // recommended stay listed; their file can be deleted
        Assert.Equal(ModelCatalog.Recommended.Count + 1, new ModelRegistry(_dir).List().Count);
    }

    [Fact]
    public void Registry_BenchSetsAbility_ChatOnlyBadge()
    {
        var reg = new ModelRegistry(_dir);
        var local = reg.AddLocal(@"D:\m\x.gguf", new string('c', 64), 1);
        reg.SetBench(local.Id, new BenchResult(local.Id, 1, 1, 40, 900, 2400, 900, 11, 30, 4, 30, DateTimeOffset.UnixEpoch));
        Assert.Equal(ModelAbility.ChatOnly, new ModelRegistry(_dir).Find(local.Id)!.Ability);
    }

    [Fact]
    public void Registry_SwitchTouchesNothingButModelsJson()
    {
        var reg = new ModelRegistry(_dir);
        reg.MarkInstalled(ModelCatalog.DefaultId, Path.Combine(_dir, "a.gguf"));
        reg.Use(ModelCatalog.DefaultId);
        Assert.Equal(["models.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Registry_BrokenFile_StartsFresh_KeepsCopy()
    {
        File.WriteAllText(Path.Combine(_dir, "models.json"), "{ nope");
        var reg = new ModelRegistry(_dir);
        Assert.Equal(ModelCatalog.Recommended.Count, reg.List().Count);
        Assert.True(File.Exists(Path.Combine(_dir, "models.json.broken")));
    }
}
