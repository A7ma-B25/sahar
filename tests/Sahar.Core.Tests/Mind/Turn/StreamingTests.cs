using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Turn;

/// <summary>0.15.0: her words as they come. The owner waited ≥ 5 s for whole replies: she now streams word by word,
/// her action goes out as soon as the model has decided it (before her words are written), and every turn reports
/// where its time went (model, waiting for him, first word) — kinds and numbers only.</summary>
public sealed class StreamingTests
{
    private readonly FakeLlm _llm = new();
    private readonly FakeIO _io = new();
    private readonly List<TurnTiming> _timings = [];

    private TurnRunner Runner() => new(_llm, new FakePrompts(), new FakeStore()) { Timed = _timings.Add };

    private static MindMsg Hello() => new()
    {
        T = "hello", Version = "1", Lang = "ar", Address = new MindAddress(Sahar.Core.Settings.Gender.Male, null), Tools = ToolCatalog.All,
        Caps = new MindCaps(true, true, Sahar.Core.Mind.Memory.MindVoice.Complementary),
    };

    private static MindMsg Chat(string text) => new() { T = "turn", Id = "t1", Source = "chat", Text = text };

    [Fact]
    public async Task Words_StreamOneByOne_WithoutWaitingForASentence()
    {
        _llm.Reply(Decision(say: "منيحة وإنت شو عم تعمل هلق يا غالي"));
        await Runner().RunAsync(Chat("كيفك"), Hello(), _io, default);
        var partials = _io.Of("partial").Select(p => p.Text!).ToList();
        Assert.True(partials.Count >= 4, string.Join(" | ", partials));
        Assert.Equal("منيحة", partials[0]);
        Assert.All(partials, p => Assert.StartsWith(p, "منيحة وإنت شو عم تعمل هلق يا غالي"));
        Assert.Equal("منيحة وإنت شو عم تعمل هلق يا غالي", _io.Final.Text);
    }

    [Fact]
    public async Task Action_GoesOutBeforeHerWords_ThenHerWordsStream()
    {
        _llm.Reply(Decision("""[{"tool":"move.play","args":{"move":"butterfly"},"why":"فراشة"}]""", say: "يلا، رح الحق الفراشة هلق 🦋"));
        await Runner().RunAsync(Chat("العبي مع الفراشة"), Hello(), _io, default);
        var sent = _io.Sent.ToList();
        int call = sent.FindIndex(m => m.T == "call"), firstWord = sent.FindIndex(m => m.T == "partial");
        Assert.True(call >= 0 && firstWord > call, $"call at {call}, first word at {firstWord}");
        Assert.Single(_io.Of("call"));   // dispatched once, not again after the decision
        Assert.Equal("يلا، رح الحق الفراشة هلق 🦋", _io.Final.Text);
    }

    /// <summary>CI run 45853945366: two Asks went out at once, and with the card closed the second one waited two
    /// minutes in her bubble. Only the first action goes out early; after his «لا» the turn asks nothing more.</summary>
    [Fact]
    public async Task AfterHisNo_TheTurnAsksNothingMore()
    {
        _io.Engine = c => new MindMsg { T = "answer", Id = c.Id, Call = c.Call, Ok = false };
        _llm.Reply(Decision("""[{"tool":"timer.start","args":{"minutes":5},"why":"x"},{"tool":"timer.cancel","args":{},"why":"y"}]""", say: "تمام"));
        await Runner().RunAsync(Chat("مؤقت"), Hello(), _io, default);
        Assert.Single(_io.Of("call"));
        Assert.Equal(ToolTemplates.AskedNo, _io.Final.Text);
    }

    private void Partly(string text, Exception? then = null) => _llm.Script.Enqueue((_, partial, _) =>
    {
        for (int i = 0; i < text.Length; i += 7) partial?.Invoke(text.Substring(i, Math.Min(7, text.Length - i)));
        if (then is not null) throw then;
        return Task.FromResult(text);
    });

    private const string Butterfly = """{"actions":[{"tool":"move.play","args":{"move":"butterfly"},"why":"x"}],"next":"done","say":"يلا رح الحق""";

    /// <summary>Review I1: the action already ran; a decision cut off by max_tokens must not end in «ما فهمت».</summary>
    [Fact]
    public async Task EarlySent_ThenTruncatedJson_SaysWhatRan()
    {
        Partly(Butterfly);
        await Runner().RunAsync(Chat("العبي مع الفراشة"), Hello(), _io, default);
        Assert.Single(_io.Of("call"));
        Assert.Equal("تمام", _io.Final.Text);   // the engine's confirmation, not BadOutput
    }

    /// <summary>Review I1: llama-server died mid-stream after the action went out.</summary>
    [Fact]
    public async Task EarlySent_ThenRuntimeError_SaysWhatRan()
    {
        Partly(Butterfly, new Sahar.Core.Mind.Runtime.MindRuntimeException("load-failed"));
        await Runner().RunAsync(Chat("العبي مع الفراشة"), Hello(), _io, default);
        Assert.Single(_io.Of("call"));
        Assert.Equal("تمام", _io.Final.Text);
        Assert.Empty(_io.Of("error"));   // an error after her line would replace it with «لسا عم اتعلّم»
    }

    [Fact]
    public async Task AFailedEarlyAction_NeverStreamsAClaim()
    {
        _io.Engine = c => new MindMsg { T = "tool-result", Id = c.Id, Call = c.Call, Ok = false, Error = "failed", Text = ToolTemplates.Failed };
        _llm.Reply(Decision("""[{"tool":"note.save","args":{"text":"خبز"},"why":"ملاحظة"}]""", say: "تمام حفظت الملاحظة"));
        await Runner().RunAsync(Chat("سجلي خبز"), Hello(), _io, default);
        Assert.DoesNotContain(_io.Of("partial"), p => TurnRunner.Claims(p.Text!));
        Assert.Equal(ToolTemplates.Failed, _io.Final.Text);
    }

    [Fact]
    public async Task EveryTurn_ReportsWhereItsTimeWent()
    {
        _llm.Reply(Decision(say: "منيحة وإنت؟"));
        await Runner().RunAsync(Chat("كيفك"), Hello(), _io, default);
        var t = Assert.Single(_timings);
        Assert.Equal(1, t.Steps);
        Assert.True(t.FirstWordMs >= 0);
        Assert.True(t.ModelMs >= 0 && t.WaitMs >= 0);
        Assert.Equal(10, t.OutTokens);
        Assert.Matches(@"^model \d+ms, wait \d+ms, first word \d+ms, steps 1, tokens 10/10$", t.ToString());
    }

    [Fact]
    public void Schema_KeepsTheWhyShort() =>
        Assert.Contains("\"why\":{\"type\":\"string\",\"maxLength\":40}", Sahar.Core.Mind.MindParts.ToolRegistry(ToolCatalog.Core).DecisionSchema());
}
