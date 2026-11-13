using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

// STREAM B owns this file (feat/0.14-memory): the encrypted store (B1); prompt parts and drafts (B3).
public static partial class MindParts
{
    private static partial IMindStore CreateStore(string folder, Func<DateTimeOffset> now, Func<LearnConsent> consent) =>
        new MindStore(new SecretBox(folder, KeyWrap.Default), now, consent);
    private static partial ISecretBox CreateSecretBox(string folder) => new SecretBox(folder, KeyWrap.Default);
    private static partial IPromptParts CreatePromptParts(IMindStore store) => new StubPrompts();
    private static partial IDrafter CreateDrafter(ILlm llm, IMindStore store) => new StubDrafter();

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
