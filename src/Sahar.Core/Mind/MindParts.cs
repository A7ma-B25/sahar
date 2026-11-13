using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Initiative;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

/// <summary>
/// SEAM (0.14, three streams): where the Mind process and the engine get each stream's parts. Each stream owns ONE
/// file and fills its own factories there: <c>MindParts.Autonomy.cs</c> (stream C: tools, policy, gate, turn loop,
/// action log), <c>MindParts.Memory.cs</c> (stream B: store, secret box, prompt parts, drafter). Stream A owns the
/// runtime (Sahar.Mind) and composes these. The seam commit ships stubs that do nothing and decide nothing.
/// </summary>
public static partial class MindParts
{
    public static IToolRegistry ToolRegistry(IReadOnlyList<ToolSpec> announced) => CreateToolRegistry(announced);
    public static IAutonomyPolicy Policy() => CreatePolicy();
    public static IInitiativeGate Gate() => CreateGate();
    public static ITurnRunner TurnRunner(ILlm llm, IPromptParts prompts, IMindStore store) => CreateTurnRunner(llm, prompts, store);
    public static IActionLog ActionLog(ISecretBox box, Func<string, ITool?> tool, Func<DateTimeOffset> now) => CreateActionLog(box, tool, now);

    /// <summary>The store under <paramref name="folder"/> (%AppData%\Sahar\mind in production; a temp folder in tests).</summary>
    /// <summary><paramref name="episodeDays"/>: how long raw chat episodes are kept (MindEpisodeDays: 7 / 30 / 90; null = 30).</summary>
    public static IMindStore Store(string folder, Func<DateTimeOffset> now, Func<LearnConsent> consent, Func<int>? episodeDays = null) =>
        CreateStore(folder, now, consent, episodeDays);

    /// <summary>«امسح كل شي عني» / «انسي كل شي»: crypto-shred the personal folder (key first, then the files). Stream B.</summary>
    public static void Shred(string folder) => ShredStore(folder);
    public static ISecretBox SecretBox(string folder) => CreateSecretBox(folder);
    public static IPromptParts PromptParts(IMindStore store) => CreatePromptParts(store);
    public static IDrafter Drafter(ILlm llm, IMindStore store) => CreateDrafter(llm, store);

    // stream C (MindParts.Autonomy.cs)
    private static partial IToolRegistry CreateToolRegistry(IReadOnlyList<ToolSpec> announced);
    private static partial IAutonomyPolicy CreatePolicy();
    private static partial IInitiativeGate CreateGate();
    private static partial ITurnRunner CreateTurnRunner(ILlm llm, IPromptParts prompts, IMindStore store);
    private static partial IActionLog CreateActionLog(ISecretBox box, Func<string, ITool?> tool, Func<DateTimeOffset> now);

    // stream B (MindParts.Memory.cs)
    private static partial IMindStore CreateStore(string folder, Func<DateTimeOffset> now, Func<LearnConsent> consent, Func<int>? episodeDays);
    static partial void ShredStore(string folder);
    private static partial ISecretBox CreateSecretBox(string folder);
    private static partial IPromptParts CreatePromptParts(IMindStore store);
    private static partial IDrafter CreateDrafter(ILlm llm, IMindStore store);
}
