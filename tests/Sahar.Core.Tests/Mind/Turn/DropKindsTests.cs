using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Turn;

/// <summary>0.14.1 fix 1: every reason a reply does not reach him is named (kinds only, for the Mind's log), a chat
/// turn never ends in silence, and her fixed fallback lines never become "her" history.</summary>
public sealed class DropKindsTests
{
    private readonly FakeLlm _llm = new();
    private readonly FakeIO _io = new();
    private readonly List<string> _drops = [];

    private TurnRunner Runner() => new(_llm, new FakePrompts(), new FakeStore()) { Dropped = _drops.Add };

    private static Sahar.Core.Mind.Protocol.MindMsg Hello() => new()
    {
        T = "hello", Version = "1", Lang = "ar", Address = new Sahar.Core.Mind.Protocol.MindAddress(Sahar.Core.Settings.Gender.Male, null),
        Tools = ToolCatalog.All, Caps = new Sahar.Core.Mind.Protocol.MindCaps(true, true, Sahar.Core.Mind.Memory.MindVoice.Complementary),
    };

    private static Sahar.Core.Mind.Protocol.MindMsg Chat(string text, string id = "t1") => new() { T = "turn", Id = id, Source = "chat", Text = text };

    [Fact]
    public async Task Unparsable_IsNamed()
    {
        _llm.Reply("not json at all");
        await Runner().RunAsync(Chat("شلونك"), Hello(), _io, default);
        Assert.Equal(ToolTemplates.BadOutput, _io.Final.Text);
        Assert.Equal(["unparsable"], _drops);
    }

    [Fact]
    public async Task AnInvalidCall_IsNamed()
    {
        _llm.Reply(Decision("""[{"tool":"files.delete","args":{}}]""", say: "مسحتهن"));
        await Runner().RunAsync(Chat("امسحي"), Hello(), _io, default);
        Assert.Contains("invalid-call", _drops);
    }

    [Fact]
    public async Task ARuntimeError_IsNamed_WithItsKind()
    {
        _llm.Script.Enqueue((_, _, _) => throw new MindRuntimeException("load-failed", "ConnectionError"));
        await Runner().RunAsync(Chat("شلونك"), Hello(), _io, default);
        Assert.Equal(["load-failed ConnectionError"], _drops);
    }

    [Fact]
    public async Task AnEmptySayInChat_IsNeverSilence()
    {
        _llm.Reply(Decision(say: ""));
        await Runner().RunAsync(Chat("شلونك"), Hello(), _io, default);
        Assert.False(string.IsNullOrWhiteSpace(_io.Final.Text));
        Assert.Equal(["empty-say"], _drops);
    }

    [Fact]
    public async Task AnEmptySayInInitiative_IsHerSilence_NotADrop()
    {
        _llm.Reply(Decision(say: ""));
        await Runner().RunAsync(new() { T = "turn", Id = "i1", Source = "initiative" }, Hello(), _io, default);
        Assert.Equal("", _io.Final.Text);
        Assert.Empty(_drops);
    }

    [Fact]
    public async Task FallbackLines_AreNotKeptAsHerHistory()
    {
        var r = Runner();
        _llm.Reply("garbage");
        await r.RunAsync(Chat("شلونك", "t1"), Hello(), _io, default);
        _llm.Reply(Decision(say: "منيحة"));
        await r.RunAsync(Chat("شو اسمك", "t2"), Hello(), _io, default);
        Assert.DoesNotContain(_llm.Requests[^1].Messages, m => m.Content.Contains("ما فهمت عليك", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoryAssistantTurns_AreDecisions_LikeHerAnswers()
    {
        var msgs = PromptBuilder.Build("P", [("شلونك", "منيحة، وإنت؟")], "", null, "chat", "شو اسمك");
        var her = msgs.Single(m => m.Role == "assistant").Content;
        using var d = System.Text.Json.JsonDocument.Parse(her);
        Assert.Equal("منيحة، وإنت؟", d.RootElement.GetProperty("say").GetString());
        Assert.Equal(0, d.RootElement.GetProperty("actions").GetArrayLength());
    }
}
