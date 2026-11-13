using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Persona;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind;

// STREAM B owns this file (feat/0.14-memory): the encrypted store and box (B1), prompt parts and drafts (B3).
public static partial class MindParts
{
    private static partial IMindStore CreateStore(string folder, Func<DateTimeOffset> now, Func<LearnConsent> consent, Func<int>? episodeDays) =>
        new MindStore(new Memory.SecretBox(folder, KeyWrap.Default), now, consent, episodeDays);
    /// <summary>Key first, then the files, then the folder; never throws (files in use stay, unreadable).</summary>
    static partial void ShredStore(string folder) => Memory.SecretBox.Shred(folder);
    private static partial ISecretBox CreateSecretBox(string folder) => new Memory.SecretBox(folder, KeyWrap.Default);
    private static partial IPromptParts CreatePromptParts(IMindStore store) => new PromptParts(store);
    private static partial IDrafter CreateDrafter(ILlm llm, IMindStore store) => new Drafter(llm, store);
}
