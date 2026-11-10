using System.Globalization;
using System.Text;
using Sahar.Core.Games;
using Sahar.Core.Motion;
using Sahar.Core.Text;
using Sahar.Core.Tools;

namespace Sahar.Core.Chat;

/// <summary>What a chat line asks for (spec §5.1; owner ruling 0.9 adds games and her moves).</summary>
public enum ChatKind { Empty, Timer, Note, Hush, Sleep, Play, Game, Move, Prayer, Greeting, Unknown }

/// <summary>
/// One parsed chat line. Timer: <see cref="Minutes"/>, or <see cref="OutOfRange"/> when the number is not 1–600.
/// Note: <see cref="Text"/> exactly as typed (null = no text). Hush: <see cref="HushFor"/> (null = until you say).
/// Game: <see cref="Game"/>. Move: <see cref="Move"/> (null = a soul hop, «نطّي»).
/// </summary>
public sealed record ChatCommand(ChatKind Kind, int? Minutes = null, string? Text = null, TimeSpan? HushFor = null, bool OutOfRange = false,
    GameKind? Game = null, MotionKind? Move = null);

/// <summary>
/// Her local chat commands (v1, no AI): Arabic or English, any digits (Latin, Arabic-Indic, Persian), diacritics and
/// tatweel ignored. The first word decides (a leading «يلا» is skipped); a note keeps its text exactly as typed.
/// </summary>
public static class ChatCommands
{
    public const int DefaultTimerMinutes = 25;

    private static readonly HashSet<string> TimerWords = ["مؤقت", "موقت", "timer"];
    private static readonly HashSet<string> NoteWords = ["ملاحظة", "ملاحظه", "note"];
    private static readonly HashSet<string> HushWords = ["اسكتي", "اسكت", "hush", "quiet"];
    private static readonly HashSet<string> SleepWords = ["نامي", "نام", "sleep"];
    private static readonly HashSet<string> PlayWords = ["نلعب", "العب", "play"];
    private static readonly HashSet<string> PrayerWords = ["الصلاة", "الصلاه", "صلاة", "prayer"];
    private static readonly HashSet<string> GreetingWords = ["كيفك", "شلونك", "مرحبا", "اهلين", "اهلا", "هلا", "السلام", "hi", "hello", "hey"];

    public static ChatCommand Parse(string text)
    {
        var norm = Normalize(text ?? "");
        if (norm.Length == 0) return new(ChatKind.Empty);
        if (norm == "be quiet") return new(ChatKind.Hush);
        var words = norm.Split(' ');
        int skip = words.Length > 1 && words[0] == "يلا" ? 1 : 0;
        var first = words[skip];
        var rest = string.Join(' ', words.Skip(skip + 1));

        if (TimerWords.Contains(first))
        {
            if (rest.Length == 0) return new(ChatKind.Timer, DefaultTimerMinutes);
            return TimerInput.ParseMinutes(rest) is { } m ? new(ChatKind.Timer, m) : new(ChatKind.Timer, OutOfRange: true);
        }
        if (NoteWords.Contains(first))
        {
            var note = AfterWords(text!, skip + 1);
            return new(ChatKind.Note, Text: note.Length == 0 ? null : note);
        }
        if (HushWords.Contains(first)) return new(ChatKind.Hush, HushFor: HushDuration(rest));
        if (SleepWords.Contains(first)) return new(ChatKind.Sleep);
        if (PlayWords.Contains(first))
            return GameNamed(rest) is { } g ? new(ChatKind.Game, Game: g) : new(ChatKind.Play);
        if (first is "ارقصي" or "dance") return new(ChatKind.Move, Move: MotionKind.Dance);
        if (first is "تمطي" or "stretch") return new(ChatKind.Move, Move: MotionKind.Stretch);
        if (first is "نطي" or "hop" or "jump") return new(ChatKind.Move);
        if (PrayerWords.Contains(first)) return new(ChatKind.Prayer);
        if (GreetingWords.Contains(first)) return new(ChatKind.Greeting);
        return new(ChatKind.Unknown);
    }

    /// <summary>Trimmed; tatweel and diacritics dropped; أ إ آ → ا; any digits → 0–9; lower case; single spaces; no trailing punctuation.</summary>
    public static string Normalize(string text)
    {
        var b = new StringBuilder(text.Length);
        bool space = false;
        foreach (var raw in Digits.ToLatin(text))
        {
            var ch = raw;
            if (ch == 'ـ' || ch is >= 'ً' and <= 'ْ' || ch == 'ٰ') continue;
            if (ch is 'أ' or 'إ' or 'آ') ch = 'ا';
            if (char.IsWhiteSpace(ch)) { space = b.Length > 0; continue; }
            if (space) { b.Append(' '); space = false; }
            b.Append(char.ToLowerInvariant(ch));
        }
        var s = b.ToString();
        return s.TrimEnd('.', '!', '?', '؟', '،', ',', ' ');
    }

    /// <summary>The original text after its first <paramref name="n"/> words, trimmed (a note keeps its own spelling).</summary>
    private static string AfterWords(string original, int n)
    {
        var s = original.Trim();
        for (int i = 0; i < n && s.Length > 0; i++)
        {
            int at = 0;
            while (at < s.Length && !char.IsWhiteSpace(s[at])) at++;
            s = s[at..].TrimStart();
        }
        return s.Trim();
    }

    private static TimeSpan? HushDuration(string rest) => rest switch
    {
        "" => null,
        "نص ساعة" or "نص ساعه" or "half an hour" or "half hour" => TimeSpan.FromMinutes(30),
        "ساعة" or "ساعه" or "hour" or "an hour" or "1 hour" => TimeSpan.FromHours(1),
        "ساعتين" or "2 hours" => TimeSpan.FromHours(2),
        _ => TimerInput.ParseMinutes(rest) is { } m ? TimeSpan.FromMinutes(m) : null,
    };

    private static GameKind? GameNamed(string rest) => rest switch
    {
        "الحقني" or "الحقيني" or "chase" => GameKind.Chase,
        "غمضلي" or "غميضة" or "hide" or "hide and seek" => GameKind.HideSeek,
        "الطابة" or "طابة" or "ball" => GameKind.Ball,
        "النجوم" or "لقط النجوم" or "stars" => GameKind.Stars,
        _ => null,
    };
}
