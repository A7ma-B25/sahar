using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;
using Sahar.Core.Tests.Mind.Online;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Turn;

/// <summary>0.17 spec §4: she remembers the last turns after a restart (persisted, encrypted); the local brain still
/// reads only its small budget, and a long online answer never pushes its short history out.</summary>
public sealed class TurnRunnerHistoryTests
{
    private readonly MemBox _box = new();
    private readonly DateTimeOffset _now = new(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(3));
    private TurnHistory History() => new(_box, () => _now, () => 30);
    private static readonly MindMsg Hello = new() { T = "hello", Address = new MindAddress(Gender.Male, null), Tools = ToolCatalog.All, Caps = new MindCaps(true, true, MindVoice.Complementary) };
    private static MindMsg Chat(string t) => new() { T = "turn", Id = "t", Source = "chat", Text = t };

    [Fact]
    public async Task KeptLine_PersistsAndAReborn_RunnerSeesIt()
    {
        var llm = new FakeLlm();
        llm.Reply(Decision(say: "اسمك أحمد، بتذكر 😊"));
        await new TurnRunner(llm, new FakePrompts(), new FakeStore()) { History = History() }.RunAsync(Chat("اسمي أحمد"), Hello, new FakeIO(), default);
        Assert.Equal("local", History().Recent().Single().Brain);

        var llm2 = new FakeLlm();
        llm2.Reply(Decision(say: "أحمد"));
        await new TurnRunner(llm2, new FakePrompts(), new FakeStore()) { History = History() }.RunAsync(Chat("شو اسمي؟"), Hello, new FakeIO(), default);
        Assert.Contains(llm2.Requests[0].Messages, m => m.Role == "user" && m.Content == "اسمي أحمد");
    }

    [Fact]
    public async Task LongOnlineAnswer_SkippedForLocal_ShortOnesKept()
    {
        var h = History();
        h.Add("مرحبا", "هلا!", "local");
        h.Add("اكتبلي مقال", new string('م', 3000), "online");
        var llm = new FakeLlm();
        llm.Reply(Decision(say: "تمام"));
        await new TurnRunner(llm, new FakePrompts(), new FakeStore()) { History = h }.RunAsync(Chat("كيفك"), Hello, new FakeIO(), default);
        var msgs = llm.Requests[0].Messages;
        Assert.Contains(msgs, m => m.Content == "مرحبا");
        Assert.DoesNotContain(msgs, m => m.Content == "اكتبلي مقال");
    }
}
