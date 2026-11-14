using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Autonomy;

/// <summary>C2: the autonomy policy (spec §3.7 with the owner's rules from the brief): she acts alone only in her own
/// reversible world, asks first for anything outside it, and never decides class C.</summary>
public sealed class AutonomyPolicyTests
{
    private static readonly IToolRegistry Reg = MindParts.ToolRegistry(ToolCatalog.All);
    private static readonly IAutonomyPolicy Policy = MindParts.Policy();

    private static ToolCall Call(string tool, string args = "{}") => new(tool, JsonDocument.Parse(args).RootElement.Clone());

    private static AutonomyDecision Decide(ToolCall call, ActSource source, AutonomyContext ctx) =>
        Policy.Decide(ToolCatalog.Get(call.Tool) ?? new ToolSpec(call.Tool, "", [], ActionClass.C), call, Reg.ClassOf(call), source, ctx);

    private static AutonomyContext Ctx(string name) => name switch
    {
        "calm" => new(),
        "away" => new(Away: true),
        "locked" => new(Locked: true),
        "fullscreen" => new(Fullscreen: true),
        "camera" => new(CameraMic: true),
        "adhan" => new(AdhanQuiet: true),
        "typing" => new(Typing: true),
        "menu" => new(MenuOpen: true),
        "battery" => new(OnBattery: true, BatteryPercent: 12),
        "self-off" => new(SelfActionsOn: false),
        "goal" => new(ForConfirmedGoalOrRoutine: true),
        "call-start" => new(CameraMic: true, CallStarting: true),
        "asked3" => new(SelfAsksThisHour: 3),
        _ => throw new ArgumentException(name),
    };

    // tool, args, source, context → verdict, reason
    public static TheoryData<string, string, ActSource, string, Verdict, string?> Table => new()
    {
        // ---- his chat line (user) ----
        { "timer.start", """{"minutes":5}""", ActSource.User, "calm", Verdict.Act, null },
        { "timer.start", """{"minutes":5}""", ActSource.User, "away", Verdict.Act, null },
        { "timer.start", """{"minutes":5}""", ActSource.User, "fullscreen", Verdict.Act, null },
        { "timer.start", """{"minutes":5}""", ActSource.User, "self-off", Verdict.Act, null },
        { "note.save", """{"text":"x"}""", ActSource.User, "camera", Verdict.Act, null },
        { "hush.on", "{}", ActSource.User, "typing", Verdict.Act, null },
        { "sleep.hide", "{}", ActSource.User, "menu", Verdict.Act, null },
        { "quran.open", "{}", ActSource.User, "calm", Verdict.Act, null },
        { "game.start", """{"game":"ball"}""", ActSource.User, "battery", Verdict.Act, null },
        { "settings.set", """{"key":"size","value":"large"}""", ActSource.User, "calm", Verdict.Act, null },
        { "prayer.next", "{}", ActSource.User, "adhan", Verdict.Act, null },
        { "settings.set", """{"key":"weatherEnabled","value":true}""", ActSource.User, "calm", Verdict.Ask, "b-class" },
        { "settings.set", """{"key":"language","value":"english"}""", ActSource.User, "away", Verdict.Ask, "b-class" },
        { "shelf.clear", "{}", ActSource.User, "calm", Verdict.Ask, "b-class" },
        { "settings.set", """{"key":"updateCheckEnabled","value":false}""", ActSource.User, "calm", Verdict.Refuse, "c-class" },
        { "claude.allow", "{}", ActSource.User, "calm", Verdict.Refuse, "c-class" },
        // ---- her own (initiative, routine, reflection) ----
        { "timer.start", """{"minutes":25}""", ActSource.Self, "calm", Verdict.Act, null },
        { "reminder.set", """{"inMinutes":30,"text":"مي"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "move.play", """{"move":"dance"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "game.start", """{"game":"stars"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "music.control", """{"op":"toggle"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "music.control", """{"op":"next"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "note.save", """{"text":"x"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "shelf.tidy", "{}", ActSource.Self, "calm", Verdict.Act, null },
        { "settings.set", """{"key":"activity","value":"calm"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "memory.remember", """{"text":"x","kind":"fact"}""", ActSource.Self, "calm", Verdict.Act, null },
        { "goal.add", """{"text":"x"}""", ActSource.Self, "self-off", Verdict.Act, null },
        { "prayer.next", "{}", ActSource.Self, "calm", Verdict.Act, null },
        { "settings.set", """{"key":"size","value":"large"}""", ActSource.Self, "calm", Verdict.Ask, "self-a" },
        { "hush.on", "{}", ActSource.Self, "calm", Verdict.Ask, "self-a" },
        { "sleep.hide", "{}", ActSource.Self, "calm", Verdict.Ask, "self-a" },
        { "quran.open", "{}", ActSource.Self, "calm", Verdict.Ask, "self-a" },
        { "quran.open", "{}", ActSource.Self, "goal", Verdict.Act, null },
        { "settings.set", """{"key":"weatherEnabled","value":true}""", ActSource.Self, "calm", Verdict.Ask, "b-class" },
        { "shelf.clear", "{}", ActSource.Self, "calm", Verdict.Ask, "b-class" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "self-off", Verdict.Refuse, "self-off" },
        { "hush.on", "{}", ActSource.Self, "self-off", Verdict.Refuse, "self-off" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "away", Verdict.Refuse, "away" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "locked", Verdict.Refuse, "locked" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "fullscreen", Verdict.Refuse, "fullscreen" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "camera", Verdict.Refuse, "camera" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "adhan", Verdict.Refuse, "adhan" },
        { "prayer.next", "{}", ActSource.Self, "away", Verdict.Refuse, "away" },
        { "settings.set", """{"key":"weatherEnabled","value":true}""", ActSource.Self, "fullscreen", Verdict.Refuse, "fullscreen" },
        { "music.control", """{"op":"pause"}""", ActSource.Self, "call-start", Verdict.Act, null },
        { "music.control", """{"op":"toggle"}""", ActSource.Self, "call-start", Verdict.Refuse, "camera" },
        { "settings.set", """{"key":"weatherEnabled","value":true}""", ActSource.Self, "asked3", Verdict.Refuse, "rate" },
        { "hush.on", "{}", ActSource.Self, "asked3", Verdict.Refuse, "rate" },
        { "timer.start", """{"minutes":25}""", ActSource.Self, "asked3", Verdict.Act, null },
        { "settings.set", """{"key":"mindSelfActions","value":true}""", ActSource.Self, "calm", Verdict.Refuse, "c-class" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Policy_FullTable(string tool, string args, ActSource source, string ctx, Verdict verdict, string? reason)
    {
        var d = Decide(Call(tool, args), source, Ctx(ctx));
        Assert.True(verdict == d.Verdict && reason == d.Reason, $"{tool} {args} {source} {ctx}: {d.Verdict} {d.Reason}");
    }

    [Fact]
    public void Policy_CNeverActs()
    {
        var contexts = new[] { "calm", "away", "locked", "fullscreen", "camera", "adhan", "typing", "menu", "battery", "self-off", "goal", "call-start", "asked3" };
        foreach (var spec in ToolCatalog.All)
            foreach (var source in Enum.GetValues<ActSource>())
                foreach (var c in contexts)
                {
                    var call = Call(spec.Name);
                    var d = Policy.Decide(spec with { Class = ActionClass.C }, call, ActionClass.C, source, Ctx(c));
                    Assert.Equal(Verdict.Refuse, d.Verdict);
                    Assert.Equal("c-class", d.Reason);
                }
    }

    [Fact]
    public void Policy_UserCPointsToSettings_SelfCIsSilent()
    {
        var call = Call("settings.set", """{"key":"blockedApps","value":"x"}""");
        Assert.Equal(AutonomyPolicy.PointToSettings, Decide(call, ActSource.User, new()).PointTo);
        Assert.Null(Decide(call, ActSource.Self, new()).PointTo);
    }

    [Fact]
    public void Policy_SelfAsksRateLimited()
    {
        var call = Call("settings.set", """{"key":"language","value":"english"}""");
        for (int n = 0; n < 3; n++) Assert.Equal(Verdict.Ask, Decide(call, ActSource.Self, new(SelfAsksThisHour: n)).Verdict);
        Assert.Equal("rate", Decide(call, ActSource.Self, new(SelfAsksThisHour: 3)).Reason);
        Assert.Equal(Verdict.Ask, Decide(call, ActSource.User, new(SelfAsksThisHour: 9)).Verdict);   // his own requests are never rate-limited
    }
}
