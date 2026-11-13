using System.Text;
using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Log;

/// <summary>C4: the settings page's «شو عملت اليوم» requests (mind-log, mind-undo, mind-feedback).</summary>
public sealed class AutonomyBridgeTests
{
    private sealed class Box : ISecretBox
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public byte[]? Read(string name) => Files.TryGetValue(name, out var b) ? b : null;
        public void Write(string name, ReadOnlySpan<byte> data) => Files[name] = data.ToArray();
        public void Delete(string name) => Files.Remove(name);
    }

    private sealed class Engine(Func<string, Dictionary<string, string>?, IReadOnlyDictionary<string, string>?> answer) : IEngineLink
    {
        public readonly List<(string, Dictionary<string, string>?)> Calls = [];
        public bool Running => true;
        public Task<bool> ReloadAsync(bool askHello = false) => Task.FromResult(true);
        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value) => Task.FromResult((true, (string?)null));
        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(new CityResult(null, null));
        public Task<bool> ClearClipboardAsync() => Task.FromResult(true);
        public Task<bool> EraseAllAsync() => Task.FromResult(true);
        public Task<IReadOnlyDictionary<string, string>?> CommandAsync(string name, Dictionary<string, string>? args)
        {
            Calls.Add((name, args));
            return Task.FromResult(answer(name, args));
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 13, 10, 12, 0, TimeSpan.FromHours(3));

    private static async Task<JsonElement> Ask(IEngineLink engine, string json)
    {
        var bridge = new SettingsBridge(Path.Combine(Path.GetTempPath(), "sahar-none-" + Guid.NewGuid().ToString("n") + ".json"), engine,
            new NoLocator(), () => Now);
        return JsonDocument.Parse(await bridge.HandleAsync(json)).RootElement.Clone();
    }

    private sealed class NoLocator : ILocator
    {
        public Task<(double Lat, double Lon)?> LocateAsync() => Task.FromResult<(double, double)?>(null);
    }

    [Fact]
    public async Task Log_FromTheEngine()
    {
        var body = ActionLogView.Json([new ActionEntry("a", Now, "timer.start", "شغّلت مؤقت ٢٥ دقيقة", ActSource.User, "ركّزني", ActionOutcome.Done, "t")], _ => true);
        var engine = new Engine((n, a) => n == "mind-log" && a!["day"] == "yesterday"
            ? new Dictionary<string, string> { ["json64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) } : null);
        var r = await Ask(engine, """{"id":1,"type":"mind-log","day":"yesterday"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.True(r.GetProperty("engine").GetBoolean());
        var e = r.GetProperty("log").GetProperty("entries")[0];
        Assert.Equal("شغّلت مؤقت ٢٥ دقيقة", e.GetProperty("summary").GetString());
        Assert.True(e.GetProperty("undo").GetBoolean());
        Assert.Equal("ركّزني", e.GetProperty("why").GetString());
        Assert.Equal(1, r.GetProperty("log").GetProperty("counts").GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task Log_SheIsOff_ReadsTheBox_NothingUndoable()
    {
        var box = new Box();
        var log = MindParts.ActionLog(box, _ => null, () => Now);
        log.Append("note.save", "حفظت ملاحظة", ActSource.User, null, ActionOutcome.Done, "n");
        log.Append("settings.set", "سألتك: شغّل الطقس؟", ActSource.Self, null, ActionOutcome.AskedNo, null);
        log.Append("claude.permission", "Claude بدّه إذنك", ActSource.Self, null, ActionOutcome.Refused, null);
        var keep = MindBridges.AutonomyOfflineLog;
        MindBridges.AutonomyOfflineLog = () => MindParts.ActionLog(box, _ => null, () => DateTimeOffset.Now);
        try
        {
            var r = await Ask(new Engine((_, _) => null), """{"id":2,"type":"mind-log","day":"all"}""");
            Assert.False(r.GetProperty("engine").GetBoolean());
            var entries = r.GetProperty("log").GetProperty("entries");
            Assert.Equal(3, entries.GetArrayLength());
            Assert.All(entries.EnumerateArray(), e => Assert.False(e.GetProperty("undo").GetBoolean()));
            var counts = r.GetProperty("log").GetProperty("counts");
            Assert.Equal((3, 1, 1), (counts.GetProperty("all").GetInt32(), counts.GetProperty("asked").GetInt32(), counts.GetProperty("refused").GetInt32()));
            // 👍 while she is off goes straight to the box
            var id = entries[0].GetProperty("id").GetString()!;
            var fb = await Ask(new Engine((_, _) => null), $$"""{"id":3,"type":"mind-feedback","entry":"{{id}}","value":1}""");
            Assert.True(fb.GetProperty("ok").GetBoolean());
            Assert.Equal(1, MindParts.ActionLog(box, _ => null, () => DateTimeOffset.Now).All().First(e => e.Id == id).Feedback);
            Assert.False((await Ask(new Engine((_, _) => null), """{"id":6,"type":"mind-feedback","entry":"x","value":7}""")).GetProperty("ok").GetBoolean());
        }
        finally { MindBridges.AutonomyOfflineLog = keep; }
    }

    [Fact]
    public async Task Undo_GoesToTheEngine()
    {
        var engine = new Engine((n, a) => n == "mind-undo" ? new Dictionary<string, string> { ["ok"] = a!["id"] == "live" ? "true" : "false" } : null);
        Assert.True((await Ask(engine, """{"id":4,"type":"mind-undo","entry":"live"}""")).GetProperty("ok").GetBoolean());
        var late = await Ask(engine, """{"id":5,"type":"mind-undo","entry":"gone"}""");
        Assert.Equal("too-late", late.GetProperty("error").GetString());
        var off = await Ask(new Engine((_, _) => null), """{"id":6,"type":"mind-undo","entry":"live"}""");
        Assert.Equal("engine-off", off.GetProperty("error").GetString());
        Assert.Equal("bad-request", (await Ask(engine, """{"id":7,"type":"mind-undo"}""")).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Feedback_SheIsOn_GoesToHer()
    {
        var engine = new Engine((n, _) => n == "mind-feedback" ? new Dictionary<string, string> { ["ok"] = "true" } : null);
        Assert.True((await Ask(engine, """{"id":8,"type":"mind-feedback","entry":"e1","value":-1}""")).GetProperty("ok").GetBoolean());
        Assert.Equal(("mind-feedback", "e1", "-1"), (engine.Calls[0].Item1, engine.Calls[0].Item2!["id"], engine.Calls[0].Item2!["value"]));
    }
}
