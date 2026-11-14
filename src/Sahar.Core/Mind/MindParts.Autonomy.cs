using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Initiative;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

// STREAM C owns this file (feat/0.14-autonomy): the tool registry, the autonomy policy, the initiative gate, the turn
// loop and the action log.
public static partial class MindParts
{
    private static partial IToolRegistry CreateToolRegistry(IReadOnlyList<ToolSpec> announced) => new ToolRegistry(announced);
    private static partial IAutonomyPolicy CreatePolicy() => new AutonomyPolicy();
    private static partial IInitiativeGate CreateGate() => new InitiativeGate();
    private static partial ITurnRunner CreateTurnRunner(ILlm llm, IPromptParts prompts, IMindStore store) => new TurnRunner(llm, prompts, store);
    private static partial IActionLog CreateActionLog(ISecretBox box, Func<string, ITool?> tool, Func<DateTimeOffset> now) => new ActionLog(box, tool, now);
}
