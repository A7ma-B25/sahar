using Sahar.Core.Geometry;
using Sahar.Core.Text;

namespace Sahar.Core.Quran;

/// <summary>Where a recitation is (spec §10.1; 0.16 adds paused).</summary>
public enum RecitationState { Idle, Opening, Playing, Paused, Ended, Stopped, Failed }

/// <summary>Where he stopped (owner ruling 5): kept in settings, used when he asks to go on.</summary>
public sealed record QuranResume(int Surah, string Reciter, int Seconds);

/// <summary>What a key does on the open bar.</summary>
public enum BarKey { Back10, Forward10, PrevSurah, NextSurah, Toggle, Collapse }

/// <summary>The bar's targets (V15QuranBar).</summary>
public enum BarButton { Close, PrevSurah, Back10, Toggle, Forward10, NextSurah, Track, Retry, Dismiss }

/// <summary>What the bar shows: the surah, the reciter, the state, where it is, and whether the stream can seek.</summary>
public sealed record QuranBarView(int Surah, string Reciter, RecitationState State, TimeSpan Position, TimeSpan? Duration, bool CanSeek);

/// <summary>
/// The Quran bar's rules (0.16 Task 8; spec §10.3–10.4; owner ruling 7: play/pause, ±10 s, seek, previous/next surah,
/// stop, resume — no āyah stepping). Pure. Time runs right → left, like reading: the fill grows from the right, → is
/// back and ← is forward. Nothing here logs; the engine logs the surah number and the reciter id only.
/// </summary>
public static class QuranBar
{
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan IdleCollapse = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan KeepEvery = TimeSpan.FromSeconds(15);
    /// <summary>The first and last seconds are not worth resuming.</summary>
    public static readonly TimeSpan ResumeMargin = TimeSpan.FromSeconds(5);

    public const string FailedLine = "ما قدرت شغّل التلاوة هلق. يمكن النت ضعيف.";
    public const string RetryLabel = "جرّب كمان مرة";
    public const string DismissLabel = "سكّر";

    /// <summary>The position after a step, inside [0, duration] (only ≥ 0 while the length is unknown).</summary>
    public static TimeSpan Seek(TimeSpan at, TimeSpan delta, TimeSpan? duration)
    {
        var to = at + delta;
        if (to < TimeSpan.Zero) to = TimeSpan.Zero;
        if (duration is { } d && d > TimeSpan.Zero && to > d) to = d;
        return to;
    }

    public static bool CanPrev(int surah) => surah > 1;
    public static bool CanNext(int surah) => surah < 114;

    public static bool Enabled(BarButton b, QuranBarView v) => b switch
    {
        BarButton.Close => true,
        BarButton.Retry or BarButton.Dismiss => v.State == RecitationState.Failed,
        _ when v.State is not (RecitationState.Playing or RecitationState.Paused) => false,
        BarButton.Toggle => true,
        BarButton.PrevSurah => CanPrev(v.Surah),
        BarButton.NextSurah => CanNext(v.Surah),
        BarButton.Back10 or BarButton.Forward10 or BarButton.Track => v.CanSeek,
        _ => false,
    };

    /// <summary>How much of the track is filled (0–1), from the right.</summary>
    public static float Fill(TimeSpan at, TimeSpan? duration) =>
        duration is { } d && d > TimeSpan.Zero ? (float)Math.Clamp(at.TotalSeconds / d.TotalSeconds, 0, 1) : 0f;

    /// <summary>A point on the track (DIP, bar-relative) → a position: the right edge is the start.</summary>
    public static TimeSpan FromTrack(float x, RectF track, TimeSpan duration)
    {
        float f = track.Width <= 0 ? 0 : Math.Clamp((track.Right - x) / track.Width, 0f, 1f);
        return TimeSpan.FromSeconds(Math.Round(duration.TotalSeconds * f));
    }

    /// <summary>The knob's centre for a fill.</summary>
    public static float KnobX(float fill, RectF track) => track.Right - track.Width * Math.Clamp(fill, 0f, 1f);

    /// <summary>Keys follow the screen (RTL): → back 10 s, ← forward 10 s, Ctrl+← next surah, Ctrl+→ previous, Space
    /// play/pause, Esc collapses.</summary>
    public static BarKey? Key(int vk, bool ctrl) => vk switch
    {
        0x25 => ctrl ? BarKey.NextSurah : BarKey.Forward10,
        0x27 => ctrl ? BarKey.PrevSurah : BarKey.Back10,
        0x20 => BarKey.Toggle,
        0x1B => BarKey.Collapse,
        _ => null,
    };

    /// <summary>m:ss (h:mm:ss past an hour) in Arabic-Indic digits; «--:--» when unknown.</summary>
    public static string Clock(TimeSpan? t)
    {
        if (t is not { } v || v < TimeSpan.Zero) return "--:--";
        int s = (int)Math.Floor(v.TotalSeconds);
        string text = s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        return Digits.ToArabicIndic(text);
    }

    /// <summary>The slider's spoken value: «١٢:٠٤ من ٤٣:١٠».</summary>
    public static string ValueText(TimeSpan? at, TimeSpan? duration) => $"{Clock(at)} من {Clock(duration)}";

    /// <summary>The bar folds back into the tag after 6 s with no pointer over it.</summary>
    public static bool Collapse(DateTimeOffset lastActivity, DateTimeOffset now, bool pointerOver) =>
        !pointerOver && now - lastActivity >= IdleCollapse;

    public static string Title(QuranBarView v) => Quran.SurahName(v.Surah);

    public static string Subtitle(QuranBarView v)
    {
        var who = Quran.ReciterName(v.Reciter);
        return v.State switch
        {
            RecitationState.Paused => who + " · واقفة",
            RecitationState.Opening => who + " · عم تفتح…",
            _ => who,
        };
    }

    /// <summary>The words a screen reader hears for a target (the mockup's aria-labels).</summary>
    public static string Label(BarButton b, QuranBarView v) => b switch
    {
        BarButton.Close => "وقّف التلاوة وسكّر",
        BarButton.PrevSurah => "السورة يلي قبل",
        BarButton.Back10 => "ارجع ١٠ ثواني",
        BarButton.Toggle => v.State == RecitationState.Playing ? "وقّف شوي" : "كمّل",
        BarButton.Forward10 => "قدّم ١٠ ثواني",
        BarButton.NextSurah => "السورة الجاية",
        BarButton.Track => "وين وصلت بالسورة",
        BarButton.Retry => RetryLabel,
        _ => DismissLabel,
    };

    /// <summary>What to keep when he pauses or stops (nothing in the first seconds).</summary>
    public static QuranResume? Keep(int surah, string reciter, TimeSpan at) =>
        at < ResumeMargin ? null : new QuranResume(surah, reciter, (int)Math.Floor(at.TotalSeconds));

    /// <summary>Review I6: keep <paramref name="next"/> only when it moved 5 s or more from what is saved (no settings
    /// write every 15 s while paused), and never over the saved place of this surah before her own place is known — a
    /// stream that opened unable to seek starts at 0, and that must not erase where he was (unless he got past it).</summary>
    public static bool ShouldKeep(QuranResume? saved, QuranResume? next, bool placeKnown)
    {
        if (next is null) return false;
        if (saved is null || saved.Surah != next.Surah || saved.Reciter != next.Reciter) return true;
        if (!placeKnown && next.Seconds <= saved.Seconds) return false;
        return Math.Abs(next.Seconds - saved.Seconds) >= 5;
    }

    /// <summary>The surah picker's resume row: «وقفت عند ١٢:٠٤» when a place is kept for this surah and reciter.</summary>
    public static string? ResumeLine(QuranResume? saved, int surah, string reciter) =>
        ResumeFrom(saved, surah, reciter, null) is { } at ? "وقفت عند " + Clock(at) : null;

    /// <summary>Where to start when he asks to go on: the saved place for this surah and reciter, unless it is at the
    /// very end.</summary>
    public static TimeSpan? ResumeFrom(QuranResume? saved, int surah, string reciter, TimeSpan? duration)
    {
        if (saved is null || saved.Surah != surah || saved.Reciter != reciter) return null;
        var at = TimeSpan.FromSeconds(saved.Seconds);
        if (at < ResumeMargin) return null;
        if (duration is { } d && d > TimeSpan.Zero && at > d - ResumeMargin) return null;
        return at;
    }
}

/// <summary>The bar's layout in DIP (V15QuranBar: 340 wide, padding 12, radius 20; rows 38 / 24 / 48 with gaps of 10;
/// buttons 40, the play button a 48 circle, gap 6, centred). RTL: the first in reading order is on the right.</summary>
public static class QuranBarLayout
{
    public const float Width = 340, Pad = 12, Radius = 20, Gap = 10;
    public const float Row1 = 38, Row2 = 24, Row3 = 48, Badge = 38, CloseSize = 34, Small = 40, Big = 48, ButtonGap = 6;
    /// <summary>The failed state's buttons (36 high; widths fit Rubik 14/500 with 16 padding).</summary>
    public const float FailedButtons = 36, RetryWidth = 132, DismissWidth = 64;

    public static float Row2Y => Pad + Row1 + Gap;
    public static float Row3Y => Row2Y + Row2 + Gap;

    public static float Height(RecitationState s) => s == RecitationState.Failed
        ? Row2Y + 2 + 22 + Gap + FailedButtons + Pad
        : Row3Y + Row3 + Pad;

    /// <summary>The time track's hit area (the drawn line is 4 high in its middle).</summary>
    public static RectF Track => new(Pad + 38 + Gap, Row2Y, Width - 2 * (Pad + 38 + Gap), Row2);

    public static RectF Badge38 => new(Width - Pad - Badge, Pad, Badge, Badge);
    public static RectF Elapsed => new(Width - Pad - 38, Row2Y, 38, Row2);
    public static RectF Total => new(Pad, Row2Y, 38, Row2);

    public static IReadOnlyDictionary<BarButton, RectF> Buttons(RecitationState s)
    {
        var d = new Dictionary<BarButton, RectF> { [BarButton.Close] = new(Pad, Pad + (Row1 - CloseSize) / 2, CloseSize, CloseSize) };
        if (s == RecitationState.Failed)
        {
            float y = Row2Y + 2 + 22 + Gap, right = Width - Pad - 2;
            d[BarButton.Retry] = new(right - RetryWidth, y, RetryWidth, FailedButtons);
            d[BarButton.Dismiss] = new(right - RetryWidth - 8 - DismissWidth, y, DismissWidth, FailedButtons);
            return d;
        }
        d[BarButton.Track] = Track;
        float total = 4 * Small + Big + 4 * ButtonGap, x = (Width - total) / 2, row = Row3Y;
        // Left to right on screen: next surah, forward 10, play, back 10, previous surah.
        foreach (var b in new[] { BarButton.NextSurah, BarButton.Forward10, BarButton.Toggle, BarButton.Back10, BarButton.PrevSurah })
        {
            float w = b == BarButton.Toggle ? Big : Small;
            d[b] = new RectF(x, row + (Row3 - w) / 2, w, w);
            x += w + ButtonGap;
        }
        return d;
    }

    /// <summary>The target under a bar-relative point, if any.</summary>
    public static BarButton? Hit(IReadOnlyDictionary<BarButton, RectF> buttons, float x, float y)
    {
        foreach (var (b, r) in buttons) if (r.Contains(x, y)) return b;
        return null;
    }
}
