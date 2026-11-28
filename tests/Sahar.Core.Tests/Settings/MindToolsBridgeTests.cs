using System.Text;
using System.Text.Json;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>0.19 «الأدوات»: the page's ops pass to the engine (a secret once, base64, never echoed or stored here), and
/// the page reads the Mind's tools status through it.</summary>
public sealed class MindToolsBridgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-tb-").FullName;
    private readonly Engine _engine = new();

    public void Dispose() => Directory.Delete(_dir, true);

    private async Task<JsonElement> Ask(string json) =>
        JsonDocument.Parse(await new SettingsBridge(Path.Combine(_dir, "settings.json"), _engine, new NoLocator(), () => DateTimeOffset.UnixEpoch, _dir).HandleAsync(json)).RootElement.Clone();

    private static string B64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

    [Fact]
    public async Task Add_SendsTheFieldsAndTheSecretApart_NeverEchoed()
    {
        var r = await Ask("""{"id":1,"type":"mind-tools-add","name":" ملفاتي ","kind":"exe","url":"","exe":"C:\\t\\files.exe","args":"--root x","secretKind":"env","secretName":"API_KEY","secret":"SECRETVALUE"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var (name, args) = _engine.Calls.Single();
        Assert.Equal("mind-tools", name);
        Assert.Equal("add", args!["op"]);
        using var data = JsonDocument.Parse(B64(args["data64"]));
        Assert.Equal("ملفاتي", data.RootElement.GetProperty("name").GetString());
        Assert.Equal("exe", data.RootElement.GetProperty("kind").GetString());
        Assert.Equal(@"C:\t\files.exe", data.RootElement.GetProperty("exe").GetString());
        Assert.Equal("API_KEY", data.RootElement.GetProperty("secretName").GetString());
        Assert.DoesNotContain("SECRETVALUE", B64(args["data64"]));
        Assert.Equal("SECRETVALUE", B64(args["value64"]));
        Assert.DoesNotContain("SECRETVALUE", r.GetRawText());
    }

    [Theory]
    [InlineData("""{"id":1,"type":"mind-tools-add","name":"x","kind":"node"}""", "bad-kind")]
    [InlineData("""{"id":1,"type":"mind-tools-add","name":"","kind":"url","url":"https://a.example/mcp"}""", "bad-name")]
    [InlineData("""{"id":1,"type":"mind-tools-add","name":"x","kind":"url","url":"http://evil.example/mcp"}""", "bad-url")]
    [InlineData("""{"id":1,"type":"mind-tools-add","name":"x","kind":"exe","exe":"server.bat"}""", "bad-exe")]
    [InlineData("""{"id":1,"type":"mind-tools-op","op":"add"}""", "bad-op")]
    [InlineData("""{"id":1,"type":"mind-tools-op","op":"enable"}""", "bad-request")]
    public async Task BadRequests_Refused(string json, string error)
    {
        var r = await Ask(json);
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(error, r.GetProperty("error").GetString());
        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public async Task Op_EnableATool()
    {
        var r = await Ask("""{"id":1,"type":"mind-tools-op","op":"enable","server":"s1a2b3c4","tool":"read_file"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var (_, args) = _engine.Calls.Single();
        Assert.Equal(("enable", "s1a2b3c4", "read_file"), (args!["op"], args["server"], B64(args["tool64"])));
    }

    [Fact]
    public async Task Status_RelaysTheMindsInfo()
    {
        _engine.Info = """{"servers":[],"announced":[],"media":{"imageChecked":true}}""";
        var r = await Ask("""{"id":1,"type":"mind-tools-status","refresh":"true"}""");
        Assert.True(r.GetProperty("engine").GetBoolean());
        Assert.True(r.GetProperty("info").GetProperty("media").GetProperty("imageChecked").GetBoolean());
        Assert.Equal(["mind-tools", "mind-tools-status"], _engine.Calls.Select(c => c.Name));
    }

    [Fact]
    public async Task EngineOff_Says()
    {
        _engine.Running = false;
        var r = await Ask("""{"id":1,"type":"mind-tools-op","op":"check-image"}""");
        Assert.Equal("engine-off", r.GetProperty("error").GetString());
    }

    private sealed class Engine : IEngineLink
    {
        public bool Running { get; set; } = true;
        public string? Info;
        public readonly List<(string Name, Dictionary<string, string>? Args)> Calls = [];
        public Task<bool> ReloadAsync(bool askHello = false) => Task.FromResult(true);
        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value) => Task.FromResult((true, (string?)null));
        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(new CityResult(null, null));
        public Task<bool> ClearClipboardAsync() => Task.FromResult(true);
        public Task<EraseOutcome?> EraseAllAsync() => Task.FromResult<EraseOutcome?>(new EraseOutcome([], true));
        public Task<IReadOnlyDictionary<string, string>?> CommandAsync(string name, Dictionary<string, string>? args)
        {
            if (!Running) return Task.FromResult<IReadOnlyDictionary<string, string>?>(null);
            Calls.Add((name, args));
            IReadOnlyDictionary<string, string> r = name == "mind-tools-status"
                ? new Dictionary<string, string> { ["json64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Info ?? "null")), ["seq"] = "3" }
                : new Dictionary<string, string> { ["ok"] = "true" };
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(r);
        }
    }

    private sealed class NoLocator : ILocator
    {
        public Task<(double Lat, double Lon)?> LocateAsync() => Task.FromResult<(double Lat, double Lon)?>(null);
    }
}
