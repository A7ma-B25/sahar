namespace Sahar.Core.Mind.Memory;

/// <summary>Her two voices (spec §7.1): complementary (default) is Sahar herself; Self drafts as he would write.</summary>
public enum MindVoice { Complementary, Self }

/// <summary>The four learning sources (owner answer: all four, opt-in EACH): her chats with him, chats he exports
/// (WhatsApp/Telegram; only his own lines kept), examples he writes, and music titles + times (local only).</summary>
public enum LearnSource { Chats, Imports, Examples, Music }

public enum ProfileCategory { Likes, UnderPressure, WhatHelps, Routines, Values, Style, People, Goals }
public enum Confidence { Low, Medium, High }
/// <summary>Only Confirmed and Edited items shape her (spec §7.2); Proposed never reaches the prompt.</summary>
public enum ItemStatus { Proposed, Confirmed, Edited, Rejected }
/// <summary>Spec §7.6: anything but None needs explicit consent each time; reflection drops it.</summary>
public enum Sensitivity { None, Health, Religion, Politics, Sexuality, Ethnicity, Money, Legal, Family, OtherPeople, Other }

/// <summary>Where an item came from: the source kind, when, and an optional short snippet (≤ 80 chars).</summary>
public sealed record ItemSource(LearnSource Kind, DateTimeOffset At, string? Snippet = null);

/// <summary>One line of «شو فهمت عنك» (spec §7.2). <see cref="Confidence"/> comes from a Core rule over
/// <see cref="Sources"/> (count, independence, recency), never from the model.</summary>
public sealed record ProfileItem(
    string Id, string Text, ProfileCategory Category, IReadOnlyList<ItemSource> Sources, Confidence Confidence,
    ItemStatus Status, Sensitivity Sensitive, DateTimeOffset CreatedAt, DateTimeOffset? ConfirmedAt = null)
{
    public bool ShapesHer => Status is ItemStatus.Confirmed or ItemStatus.Edited;
}

public enum MemoryKind { Fact, Preference, Goal, Event }

/// <summary>A confirmed memory (from a proposal he accepted, or added by hand).</summary>
public sealed record MemoryItem(string Id, string Text, MemoryKind Kind, DateTimeOffset At, Sensitivity Sensitive = Sensitivity.None);

/// <summary>His writing style in plain words (spec §7.1) plus the counts behind it.</summary>
public sealed record StyleCard(string SummaryAr, int Samples, int FromImports, int FromExamples);

/// <summary>Each source's consent (owner: opt-in each). Default all off until he says yes. Kept in settings.json
/// (MindLearnChats/Imports/Examples/Music, editable on «عقلي»); the store reads it through a delegate.</summary>
public sealed record LearnConsent(bool Chats = false, bool Imports = false, bool Examples = false, bool Music = false)
{
    public static LearnConsent From(Settings.SaharSettings s) => new(s.MindLearnChats, s.MindLearnImports, s.MindLearnExamples, s.MindLearnMusic);

    public bool Allows(LearnSource s) => s switch
    {
        LearnSource.Chats => Chats,
        LearnSource.Imports => Imports,
        LearnSource.Examples => Examples,
        LearnSource.Music => Music,
        _ => false,
    };
}

/// <summary>
/// The Mind's personal store (stream B): profile, memories, style samples, episodes; everything encrypted at rest
/// (AES-256-GCM, key wrapped with DPAPI CurrentUser), two writers (settings app + Mind) under a named mutex, like
/// SettingsStore. Every method is safe to call with nothing stored. Never logs text.
/// </summary>
public interface IMindStore
{
    IReadOnlyList<ProfileItem> Profile();
    /// <summary>A new proposal; null when a rejected item with the same text exists (kept as a hash) or the source
    /// has no consent (<see cref="LearnConsent"/>). Sensitive proposals stay Proposed and need his explicit yes.</summary>
    ProfileItem? Propose(string text, ProfileCategory category, ItemSource source, Sensitivity sensitive);
    bool Confirm(string id);
    bool Edit(string id, string text);
    bool Reject(string id);
    bool Delete(string id);
    /// <summary>The confirmed items for a turn, most relevant to <paramref name="line"/> first, within a token budget.</summary>
    IReadOnlyList<ProfileItem> ForPrompt(string line, int maxTokens);

    IReadOnlyList<MemoryItem> Memories(string? search = null);
    MemoryItem AddMemory(string text, MemoryKind kind, Sensitivity sensitive = Sensitivity.None);
    bool DeleteMemory(string id);

    StyleCard Style();
    /// <summary>His own lines as style samples (already filtered to his lines and scrubbed); returns how many were kept.</summary>
    int AddSamples(LearnSource source, IEnumerable<string> lines);
    /// <summary>The nearest samples to a topic (few-shot for Self voice).</summary>
    IReadOnlyList<string> Samples(string topic, int count);

    /// <summary>«انسي كل شي»: the key is deleted first (crypto-shred), then the whole mind folder.</summary>
    void ForgetAll();
    /// <summary>«صدّري ذاكرتي»: readable JSON (unencrypted; the page warns).</summary>
    string Export();
}

/// <summary>Encrypted files under %AppData%\Sahar\mind (stream B owns the crypto). Other streams store their own
/// personal data (the action log) through this, never as plain files.</summary>
public interface ISecretBox
{
    byte[]? Read(string name);
    void Write(string name, ReadOnlySpan<byte> data);
    void Delete(string name);
}
