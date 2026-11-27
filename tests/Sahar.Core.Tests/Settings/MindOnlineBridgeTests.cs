using System.Text;
using System.Text.Json;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>0.17 «العقل أونلاين»: the key passes once to the engine (base64, never stored here), the page reads the
/// Mind's online status through it.</summary>
public sealed class MindOnlineBridgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-ob-").FullName;
    private readonly Engine _engine = new();

    public void Dispose() => Directory.Delete(_dir, true);

    private async Task<JsonElement> Ask(string json) =>
        JsonDocument.Parse(await new SettingsBridge(Path.Combine(_dir, "settings.json"), _engine, new NoLocator(), () => DateTimeOffset.UnixEpoch, _dir).HandleAsync(json)).RootElement.Clone();

    [Fact]
    public async Task Key_GoesToTheEngineOnce_NeverToSettings()
    {
        var r = await Ask("""{"id":1,"type":"mind-online-key","provider":"groq","key":"  gsk_SECRET \n"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var (name, args) = _engine.Calls.Single();
        Assert.Equal("mind-online", name);
        Assert.Equal("key", args!["op"]);
        Assert.Equal("gsk_SECRET", Encoding.UTF8.GetString(Convert.FromBase64String(args["key64"])));
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")) && File.ReadAllText(Path.Combine(_dir, "settings.json")).Contains("SECRET"));
        Assert.DoesNotContain("SECRET", r.GetRawText());
    }

    [Theory]
    [InlineData("""{"id":1,"type":"mind-online-key","provider":"nope","key":"x"}""", "bad-provider")]
    [InlineData("""{"id":1,"type":"mind-online-key","provider":"groq","key":"   "}""", "bad-key")]
    [InlineData("""{"id":1,"type":"mind-online-key","provider":"groq","key":"a b"}""", "bad-key")]
    [InlineData("""{"id":1,"type":"mind-online-test","provider":"x"}""", "bad-provider")]
    public async Task BadRequests_Refused(string json, string error)
    {
        var r = await Ask(json);
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(error, r.GetProperty("error").GetString());
        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public async Task EngineOff_Says()
    {
        _engine.Running = false;
        var r = await Ask("""{"id":1,"type":"mind-online-test","provider":"groq"}""");
        Assert.Equal("engine-off", r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Status_RelaysTheMindsInfo_AndSuggestions()
    {
        _engine.Info = """{"providers":[{"id":"groq","model":"openai/gpt-oss-120b","key":true,"used":12,"limit":1000,"exhausted":false}],"test":"ok","testProvider":"groq"}""";
        var r = await Ask("""{"id":1,"type":"mind-online-status","refresh":"true"}""");
        Assert.True(r.GetProperty("engine").GetBoolean());
        Assert.Equal(12, r.GetProperty("info").GetProperty("providers")[0].GetProperty("used").GetInt32());
        Assert.Equal("openai/gpt-oss-120b", r.GetProperty("models").GetProperty("groq")[0].GetString());
        Assert.Equal(["mind-online", "mind-online-status"], _engine.Calls.Select(c => c.Name));
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
            IReadOnlyDictionary<string, string> r = name == "mind-online-status"
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
