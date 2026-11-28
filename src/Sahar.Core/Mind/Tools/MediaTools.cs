using System.Globalization;
using System.Text.RegularExpressions;
using Sahar.Core.Mind.Online;

namespace Sahar.Core.Mind.Tools;

/// <summary>0.19 spec §5: a grant as it applies — a sensitive tool always asks (D4), whatever he picked.</summary>
public static class ToolGrants
{
    public static ToolGrant Effective(ToolGrant grant, bool sensitive) => sensitive ? ToolGrant.Ask : grant;
}

/// <summary>
/// 0.19 spec §8: the two built-in media tools. <c>image.make</c> sends a short English prompt (the decision's
/// <c>prompt_en</c>, ≤ 300) to Pollinations; <c>voice.make</c> sends his words (≤ 200 characters; the grammar allows a
/// little more so she can say it is too long) to Groq's Saudi Orpheus voice. Files go to <c>Pictures\Sahar</c> (a picture as .jpg: Pollinations answers JPEG). CI's
/// loopback <see cref="ProviderProfiles.TestBase"/> stands in for both hosts.
/// </summary>
public static partial class MediaTools
{
    public const int PromptMax = 300, VoiceMax = 200, VoiceArgMax = 400;
    public const string VoiceModel = "canopylabs/orpheus-arabic-saudi";

    public static ToolSpec Image { get; } = new("image.make",
        "ارسميله صورة لما يطلب: prompt_en = وصف قصير للصورة بالإنكليزي",
        [new("prompt_en", ArgType.String, true, MaxLength: PromptMax)], ActionClass.B, Source: ToolSource.Media);

    public static ToolSpec Voice { get; } = new("voice.make",
        "اعمليله ملف صوتي بيقول كلام هو بيطلبه: text = الكلام نفسه (قصير)",
        [new("text", ArgType.String, true, MaxLength: VoiceArgMax)], ActionClass.B, Source: ToolSource.Media);

    /// <summary>A fresh <paramref name="seed"/> each time: the same words give a new picture (Pollinations' default seed is fixed).</summary>
    public static Uri ImageUri(string prompt, int seed) =>
        new((ProviderProfiles.TestBase is { } t ? t + "pollinations/" : "https://image.pollinations.ai/") + "prompt/" + Uri.EscapeDataString(prompt.Trim())
            + "?seed=" + seed.ToString(CultureInfo.InvariantCulture));

    public static Uri VoiceUri() => new(ProviderProfiles.Groq().BaseUrl, "audio/speech");

    public static string FileName(string kind, DateTimeOffset at) =>
        $"sahar-{kind}-{at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{(kind == "image" ? "jpg" : "wav")}";

    [GeneratedRegex(@"^sahar-(image-\d{8}-\d{6}(-\d{1,2})?\.(png|jpg)|voice-\d{8}-\d{6}(-\d{1,2})?\.wav)$")] private static partial Regex MediaName();

    /// <summary>Only her own picture and voice files (a bare name, never a path): what the chat window may show.</summary>
    public static bool IsMediaFile(string name) => MediaName().IsMatch(name);
}
