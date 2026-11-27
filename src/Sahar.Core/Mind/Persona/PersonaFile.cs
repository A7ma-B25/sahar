using System.Text;
using System.Text.RegularExpressions;
using Sahar.Core.Settings;

namespace Sahar.Core.Mind.Persona;

/// <summary>What saving his persona did.</summary>
public enum PersonaSave { Ok, Empty, TooLong, Io }

/// <summary>
/// «شخصية سحر» (0.16 Task 7, owner ruling 8): her personality as a plain Arabic file he can read and change,
/// <c>%AppData%\Sahar\persona.md</c> beside settings.json, loaded on top of any model. No file means the shipped
/// <see cref="Default"/>; «رجّعها متل ما كانت» deletes his file, so a better default in a later version reaches him.
/// Words that follow his gender are written <c>{بدو|بدها}</c>. Only the personality lives here: the safety rules stay in
/// code (<see cref="PromptParts"/>) and no edit can remove them. Never throws; never logs the text.
/// </summary>
public sealed partial class PersonaFile(string path)
{
    public const string FileName = "persona.md";
    /// <summary>≈ 670 tokens at most: the persona never crowds the turn out of the context.</summary>
    public const int MaxChars = 2000;

    public string Path => path;
    private string Tmp => path + ".tmp";

    /// <summary>His persona file on this PC (next to settings.json).</summary>
    public static PersonaFile ForUser() =>
        new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SettingsStore.DefaultPath)!, FileName));

    /// <summary>The persona she ships with (the 0.14 persona's personal part, unchanged in spirit).</summary>
    public static string Default { get; } = string.Join('\n',
        "إنتِ سَحَر، رفيقة صغيرة عايشة على شاشة {جهازه|جهازها}. بتحكي شامي بسيط ودافي، جمل قصيرة (سطرين بالكتير)، وبتحكي إنكليزي بس إذا {هو حكى|هي حكيت} إنكليزي.",
        "بتحطي إيموجي خفيف بمحلّه، متل ما بيحكي الصحاب.",
        "لما {يحكيلك|تحكيلك} عن شعور أو وقت صعب:",
        "- اسمعي واعكسي اللي فهمتيه بكلماتك البسيطة، بعدين لخّصي.",
        "- اسألي سؤال واحد مفتوح بالمرة، وقبل أي نصيحة اسألي إذا {بدو|بدها} حل ولا بس {يفضفض|تفضفض}.",
        "- إذا في فكرة عم تدور {براسو|براسها}، سمّيها بلطف واعرضي طريقة تانية لنشوفها، بلا ضغط.",
        "- {ساعديه يلاقي أسبابه هو|ساعديها تلاقي أسبابها هي} وشو المهم {إلو|إلها}، واحترمي «مش هلق».",
        "- اقترحي خطوة صغيرة وحدة من أدواتك (استراحة، تذكير، ملاحظة) وخدي {موافقتو|موافقتها} أول.",
        "إذا ما في شي {عنو|عنها} أو عن شغلة مفيدة، السكوت أحسن جواب.");

    /// <summary>His persona (trimmed, at most <see cref="MaxChars"/>), or the default when there is none, it is blank,
    /// or it cannot be read.</summary>
    public string Load()
    {
        var text = Read();
        if (text is null) return Default;
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    /// <summary>He has his own persona (a file that is not the default).</summary>
    public bool Edited => Read() is { } t && t != Default;

    /// <summary>Keeps his text (atomically). The default text is not an edit: his file goes instead.</summary>
    public PersonaSave Save(string text)
    {
        var clean = Normalise(text);
        if (clean.Length == 0) return PersonaSave.Empty;
        if (clean.Length > MaxChars) return PersonaSave.TooLong;
        try
        {
            if (clean == Default) { Delete(); return PersonaSave.Ok; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(Tmp, clean + "\n", new UTF8Encoding(false));
            File.Move(Tmp, path, overwrite: true);
            return PersonaSave.Ok;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            try { if (File.Exists(Tmp)) File.Delete(Tmp); } catch (Exception) { /* a leftover temp file is harmless */ }
            return PersonaSave.Io;
        }
    }

    /// <summary>Back to the shipped persona: his file goes. Returns the default text.</summary>
    public string Restore()
    {
        try { Delete(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Load still falls back when it cannot read */ }
        return Default;
    }

    /// <summary>The text for whoever she talks to: each <c>{his|hers}</c> becomes one word (his when unknown, as Arabic).</summary>
    public static string ForGender(string text, Gender g) =>
        Choice().Replace(text, m => g == Gender.Female ? m.Groups[2].Value : m.Groups[1].Value);

    [GeneratedRegex(@"\{([^{}|\n]*)\|([^{}|\n]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Choice();

    private void Delete()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private string? Read()
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Review minor (0.16): a huge file (put there by hand) is never read whole; only its start can be used.
            using var reader = new StreamReader(path, Encoding.UTF8);
            var buffer = new char[MaxChars * 4];
            int n = reader.ReadBlock(buffer, 0, buffer.Length);
            var t = Normalise(new string(buffer, 0, n));
            return t.Length == 0 ? null : t;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
}
