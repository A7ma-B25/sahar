using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

// STREAM B owns this file (feat/0.14-memory). Seam stubs: nothing is kept, nothing is learned (in memory only).
public static partial class MindParts
{
    private static partial IMindStore CreateStore(string folder, Func<DateTimeOffset> now, Func<LearnConsent> consent, Func<int>? episodeDays) => new StubStore(now);
    private static partial ISecretBox CreateSecretBox(string folder) => new StubBox();
    private static partial IPromptParts CreatePromptParts(IMindStore store) => new StubPrompts();
    private static partial IDrafter CreateDrafter(ILlm llm, IMindStore store) => new StubDrafter();

    private sealed class StubStore(Func<DateTimeOffset> now) : IMindStore
    {
        public IReadOnlyList<ProfileItem> Profile() => [];
        public ProfileItem? Propose(string text, ProfileCategory category, ItemSource source, Sensitivity sensitive) => null;
        public bool Confirm(string id) => false;
        public bool Edit(string id, string text) => false;
        public bool Reject(string id) => false;
        public bool Delete(string id) => false;
        public IReadOnlyList<ProfileItem> ForPrompt(string line, int maxTokens) => [];
        public IReadOnlyList<MemoryItem> Memories(string? search = null) => [];
        public MemoryItem AddMemory(string text, MemoryKind kind, Sensitivity sensitive = Sensitivity.None) => new("stub", text, kind, now(), sensitive);
        public bool DeleteMemory(string id) => false;
        public StyleCard Style() => new("", 0, 0, 0);
        public int AddSamples(LearnSource source, IEnumerable<string> lines) => 0;
        public IReadOnlyList<string> Samples(string topic, int count) => [];
        public void ForgetAll() { }
        public string Export() => "{}";
    }

    private sealed class StubBox : ISecretBox
    {
        private readonly Dictionary<string, byte[]> _files = [];
        public byte[]? Read(string name) => _files.TryGetValue(name, out var b) ? b : null;
        public void Write(string name, ReadOnlySpan<byte> data) => _files[name] = data.ToArray();
        public void Delete(string name) => _files.Remove(name);
    }

    private sealed class StubPrompts : IPromptParts
    {
        public string Persona(MindVoice voice, MindAddress address) => "";
        public string PersonalBlock(string line, int maxTokens) => "";
        public DistressReply? Distress(string line) => null;
        public Sensitivity Sensitive(string text, Sensitivity modelTag) => modelTag;
    }

    private sealed class StubDrafter : IDrafter
    {
        public Task<Draft?> DraftAsync(DraftRequest request, CancellationToken ct) => Task.FromResult<Draft?>(null);
    }
}
