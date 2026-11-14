using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;

namespace Sahar.Engine.Menu;

/// <summary>
/// 0.14 (stream C): her brain in the card's header (spec §8 "Menu card"). While the Mind thinks the header says
/// «عم فكّر…», then her words as they come (whole sentences that fit the line); her questions («إي» / «لا», keys
/// 1/2) sit in the header like a drill's segmented track, in the fact's line. A question still open when the card
/// closes is a «لا». Once a day, when nothing is more urgent, the fact says what she did today.
/// </summary>
internal sealed partial class MenuCard
{
    public const string AskYes = "ask-yes", AskNo = "ask-no";
    private const float LineWidth = MenuCardLayout.Width - 2 * MenuCardLayout.Padding - 32;

    private sealed record Question(string Text, Action<bool> Answer);

    private Question? _question;
    private bool _thinking;
    private string? _stream;

    /// <summary>The once-a-day «عملت ٣ شغلات اليوم» fact (null = nothing to say), when nothing is more urgent.</summary>
    public Func<HeaderFact?>? TodayFact { get; set; }

    public bool Asking => _question is not null;

    /// <summary>The first letter in the chat field (spec §3.1: warm on typing). Never the text.</summary>
    public event Action? ChatTyped;

    /// <summary>Does a line fit the header's first line (17 px, semibold)?</summary>
    public static bool FitsHeader(string text) =>
        text.IndexOf('\n') < 0 && DWrite.Measure(Address.ToYou(text), 17, 600, rtl: true).X <= LineWidth;

    /// <summary>«عم فكّر…» on or off (off also drops the streamed words).</summary>
    public void MindThinking(bool on)
    {
        if (_thinking == on && (on || _stream is null)) return;
        _thinking = on;
        _stream = null;
        Refresh();
    }

    /// <summary>Her words so far: shown while they fit the line; longer replies end in her bubble instead.</summary>
    public void MindStream(string text)
    {
        if (!_thinking || !FitsHeader(text) || text == _stream) return;
        _stream = text;
        Refresh();
    }

    /// <summary>Her question in the header; <paramref name="answer"/> gets true for «إي». Focus goes to «لا» (Enter is safe).</summary>
    public void MindAsk(string question, Action<bool> answer)
    {
        if (!IsOpen) { answer(false); return; }
        _question = new Question(question, answer);
        _focus = AskNo;
        _catcher.Focus();   // 1 and 2 reach the card, not the chat field
        if (View != "main") Show("main", fade: true);
        else Refresh();
        ShowFocus();
    }

    /// <summary>Takes the question away (answered elsewhere, or timed out as «لا»).</summary>
    public void MindAskEnd()
    {
        if (_question is null) return;
        _question = null;
        _focus = null;
        Refresh();
    }

    public string MindDescribe() =>
        $"asking={(_question is not null ? "true" : "false")} thinking={(_thinking ? "true" : "false")} stream={(_stream is null ? 0 : _stream.Length)}";

    private void Answer(bool yes)
    {
        if (_question is not { } q) return;
        _question = null;
        _focus = null;
        Refresh();
        q.Answer(yes);
    }

    private bool MindPress(string id)
    {
        if (id is not (AskYes or AskNo)) return false;
        Answer(id == AskYes);
        return true;
    }

    private bool MindKey(MenuKey key)
    {
        if (_question is null) return false;
        switch (key)
        {
            case MenuKey.Digit1: Answer(true); return true;
            case MenuKey.Digit2 or MenuKey.Escape: Answer(false); return true;
            case MenuKey.Digit3: return true;
            default: return false;
        }
    }

    private void MindClosing()
    {
        _thinking = false;
        _stream = null;
        if (_question is not { } q) return;
        _question = null;
        q.Answer(false);
    }

    private bool MindAsking(string view) => _question is not null && view == "main";

    private string? MindLine(string view) =>
        _question is { } q && view == "main" ? q.Text : _stream ?? (_thinking ? Sahar.Core.Mind.Tools.ToolTemplates.Thinking : null);

    private HeaderFact? MindFact(string view) => view == "main" ? TodayFact?.Invoke() : null;

    /// <summary>The header's Ask track in the fact's line, from the logical start: two 72-DIP segments.</summary>
    private static CardLayout MindLayout(CardLayout l, bool asking)
    {
        if (!asking) return l;
        const float x0 = MenuCardLayout.Padding + 16, y = 52, w = 150, h = 30, seg = 72;
        RectF At(float startX, float top, float width, float height) => new(l.Width - startX - width, top, width, height);
        var rects = new Dictionary<string, RectF>(l.Rects)
        {
            ["ask-track"] = At(x0, y, w, h),
            [AskYes] = At(x0 + 3, y + 3, seg, h - 6),
            [AskNo] = At(x0 + 3 + seg, y + 3, seg, h - 6),
        };
        var rows = new List<IReadOnlyList<string>> { new[] { AskYes, AskNo } };
        rows.AddRange(l.Rows);
        return l with { Rects = rects, FocusOrder = [AskYes, AskNo, .. l.FocusOrder], Rows = rows };
    }
}

/// <summary>Draws the header's Ask track: a sunken track with «إي» and «لا», as a drill's segmented choice.</summary>
internal static class MindHeader
{
    public static void Background(D2DContext dc, CardContent c)
    {
        if (c.Layout.Rects.TryGetValue("ask-track", out var track))
            dc.FillRoundedRect(CardPainter.R(track), 12, dc.SolidBrush(new D2DColor(c.T.Sunken)));
    }

    public static void Foreground(D2DContext dc, CardContent c)
    {
        var ink = dc.SolidBrush(new D2DColor(c.T.Ink));
        foreach (var (id, label) in new[] { (MenuCard.AskYes, "إي"), (MenuCard.AskNo, "لا") })
            if (c.Layout.Rects.TryGetValue(id, out var r)) dc.TextLayout(label, 14, 500, CardPainter.R(r), ink, TextAlign.Centre, c.Rtl);
    }
}
