using System.Text.Json;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Initiative;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

// STREAM C owns this file (feat/0.14-autonomy). Seam stubs: nothing is allowed, nothing runs, the gate stays shut.
public static partial class MindParts
{
    private static partial IToolRegistry CreateToolRegistry(IReadOnlyList<ToolSpec> announced) => new ToolRegistry(announced);
    private static partial IAutonomyPolicy CreatePolicy() => new AutonomyPolicy();
    private static partial IInitiativeGate CreateGate() => new InitiativeGate();
    private static partial ITurnRunner CreateTurnRunner(ILlm llm, IPromptParts prompts, IMindStore store) => new StubTurn();
    private static partial IActionLog CreateActionLog(ISecretBox box, Func<string, ITool?> tool, Func<DateTimeOffset> now) => new StubLog(now);

    private sealed class StubTurn : ITurnRunner
    {
        public Task RunAsync(MindMsg turn, MindMsg hello, ITurnIO io, CancellationToken ct)
        {
            io.Send(new MindMsg { T = "error", Id = turn.Id, Error = "busy" });
            return Task.CompletedTask;
        }
    }

    private sealed class StubLog(Func<DateTimeOffset> now) : IActionLog
    {
        private readonly List<ActionEntry> _all = [];
        public ActionEntry Append(string tool, string summary, ActSource source, string? why, ActionOutcome outcome, string? undoToken)
        {
            var e = new ActionEntry(Guid.NewGuid().ToString("n"), now(), tool, summary, source, why, outcome, undoToken);
            _all.Add(e);
            return e;
        }
        public IReadOnlyList<ActionEntry> Day(DateOnly day) => _all.Where(e => DateOnly.FromDateTime(e.At.LocalDateTime) == day).ToList();
        public IReadOnlyList<ActionEntry> All() => _all;
        public bool Undo(string id) => false;
        public void Feedback(string id, int value) { }
    }
}
