using System.Net;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Models;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Models;

/// <summary>The models page's requests (mind-*), through the settings bridge.</summary>
[Collection("MindBridgeStatics")]
public sealed class MindBridgeRuntimeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-mb-").FullName;
    private readonly Engine _engine = new();

    public MindBridgeRuntimeTests()
    {
        MindBridges.ModelsFolder = () => Path.Combine(_dir, "models");
        MindBridges.Http = () => new Hf();
    }

    public void Dispose()
    {
        MindBridges.ModelsFolder = () => Sahar.Core.Mind.MindPaths.Models;
        MindBridges.Http = () => new SocketsHttpHandler();
        MindBridges.PickGguf = null;
        Directory.Delete(_dir, true);
    }

    private async Task<JsonElement> Ask(string json) =>
        JsonDocument.Parse(await new SettingsBridge(Path.Combine(_dir, "settings.json"), _engine, new NoLocator(), () => DateTimeOffset.UnixEpoch).HandleAsync(json)).RootElement.Clone();

    [Fact]
    public async Task Models_ListsTheRecommended()
    {
        var r = await Ask("""{"id":1,"type":"mind-models"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(ModelCatalog.Recommended.Count, r.GetProperty("models").GetProperty("models").GetArrayLength());
        Assert.True(r.GetProperty("cudaBytes").GetInt64() > 600_000_000);
    }

    [Fact]
    public async Task Add_Hf_PinsIt()
    {
        var r = await Ask("""{"id":2,"type":"mind-model","op":"add","query":"https://huggingface.co/x/y-GGUF"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("hf:x/y-GGUF/y-Q4_K_M.gguf", r.GetProperty("added").GetString());
        var e = new ModelRegistry(Path.Combine(_dir, "models")).Find("hf:x/y-GGUF/y-Q4_K_M.gguf")!;
        Assert.Equal(new string('a', 64), e.Sha256);
        Assert.Equal(new string('1', 40), e.Commit);
    }

    [Fact]
    public async Task Add_Junk_SaysWhatItTakes()
    {
        var r = await Ask("""{"id":3,"type":"mind-model","op":"add","query":"hello there"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(MindBridges.BadQuery, r.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Add_LocalGguf_HashedAndInstalled()
    {
        var file = Path.Combine(_dir, "My-Model-Q4_K_M.gguf");
        File.WriteAllBytes(file, [1, 2, 3]);
        var r = await Ask(JsonSerializer.Serialize(new { id = 4, type = "mind-model", op = "add", query = file }));
        Assert.True(r.GetProperty("ok").GetBoolean());
        var e = new ModelRegistry(Path.Combine(_dir, "models")).Find(r.GetProperty("added").GetString()!)!;
        Assert.True(e.Installed);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", e.Sha256);
    }

    [Fact]
    public async Task Pick_UsesTheDialog()
    {
        var file = Path.Combine(_dir, "picked.gguf");
        File.WriteAllBytes(file, [9]);
        MindBridges.PickGguf = () => Task.FromResult<string?>(file);
        Assert.True((await Ask("""{"id":5,"type":"mind-pick"}""")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Download_WithoutEngine_SaysSo()
    {
        _engine.Running = false;
        var r = await Ask("""{"id":6,"type":"mind-download","op":"start"}""");
        Assert.Equal(MindBridges.EngineOff, r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Download_ForwardsToTheEngine_AndStatusReadsProgress()
    {
        Assert.True((await Ask("""{"id":7,"type":"mind-download","op":"start","model":"gemma-4-e4b-q4km"}""")).GetProperty("ok").GetBoolean());
        Assert.Equal(("mind-download", "start", "gemma-4-e4b-q4km"), (_engine.Last, _engine.LastArgs!["op"], _engine.LastArgs["model"]));
        var s = await Ask("""{"id":8,"type":"mind-status"}""");
        var d = s.GetProperty("download");
        Assert.Equal(("downloading", 2_290_000_000L, 4_977_171_584L), (d.GetProperty("state").GetString(), d.GetProperty("done").GetInt64(), d.GetProperty("total").GetInt64()));
    }

    private sealed class Engine : IEngineLink
    {
        public bool Running { get; set; } = true;
        public string? Last;
        public Dictionary<string, string>? LastArgs;
        public Task<bool> ReloadAsync(bool askHello = false) => Task.FromResult(true);
        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value) => Task.FromResult((true, (string?)null));
        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(new CityResult(null, null));
        public Task<bool> ClearClipboardAsync() => Task.FromResult(true);
        public Task<bool> EraseAllAsync() => Task.FromResult(true);
        public Task<IReadOnlyDictionary<string, string>?> CommandAsync(string name, Dictionary<string, string>? args)
        {
            (Last, LastArgs) = (name, args);
            IReadOnlyDictionary<string, string> r = name == "mind-status"
                ? new Dictionary<string, string> { ["phase"] = "none", ["dlModel"] = "gemma-4-e4b-q4km", ["dlState"] = "downloading", ["dlDone"] = "2290000000", ["dlTotal"] = "4977171584", ["dlBps"] = "3100000", ["dlError"] = "-" }
                : new Dictionary<string, string> { ["ok"] = "true" };
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(r);
        }
    }

    private sealed class NoLocator : ILocator
    {
        public Task<(double Lat, double Lon)?> LocateAsync() => Task.FromResult<(double, double)?>(null);
    }

    private sealed class Hf : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var json = req.RequestUri!.AbsolutePath.Contains("/tree/")
                ? $$$"""[{"type":"file","path":"y-Q4_K_M.gguf","size":5,"lfs":{"oid":"{{{new string('a', 64)}}}","size":5}}]"""
                : $$$"""{"sha":"{{{new string('1', 40)}}}","gated":false}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}

[CollectionDefinition("MindBridgeStatics", DisableParallelization = true)]
public sealed class MindBridgeStaticsCollection;
