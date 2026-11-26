using Sahar.Core.Surfaces;

namespace Sahar.Engine.Surfaces;

/// <summary>A row in a picker (see web/surfaces/views.js listView).</summary>
internal sealed record PickRow(string Id, string Label)
{
    public int? Num { get; init; }
    public string? NumText { get; init; }
    public string? Meta { get; init; }
    public string? Icon { get; init; }
    public bool Sel { get; init; }
    public bool Play { get; init; }
    public bool Pinned { get; init; }
    public bool Strong { get; init; }
    public bool Dim { get; init; }
    public bool Check { get; init; }
    public bool Live { get; init; }
    public bool Soft { get; init; }
}

internal sealed record PickBox(string Title, string? Hint, string? Icon = null);
internal sealed record PickAction(string Id, string Label, string? Icon = null, string? ConfirmQ = null, string? ConfirmYes = null, string? ConfirmNo = null);

/// <summary>Every panel surface's model in one shape (the views pick by Kind): a list picker, an input card, an ask or
/// choose bubble, «الدرج». The tray menu has its own builder (TrayMenu).</summary>
internal sealed class PanelSpec
{
    public required string Kind { get; init; }          // list | input | ask | choose | drawer
    public required string Name { get; init; }          // surah, reciter, clipboard, prayer, method, timer, note, city, name, gender, update, ask, draft…
    public string Title { get; init; } = "";
    public string? Sub { get; init; }
    public string? SubAction { get; init; }
    public string? SubIcon { get; init; }
    public bool Back { get; init; }
    public string? Search { get; init; }
    public string? EnterHint { get; init; }
    public int ListHeight { get; init; } = 302;
    public IReadOnlyList<PickRow> Rows { get; init; } = [];
    public PickBox? Empty { get; init; }
    public PickBox? None { get; init; }
    public (string Title, string Sub, string StopLabel)? Now { get; init; }
    public bool NowPaused { get; init; }
    public PickAction? FootAction { get; init; }
    public PickAction? FootLink { get; init; }
    public string FootHint { get; init; } = "Esc لسكّر";
    // input
    public string? FieldIcon { get; init; }
    public string? Placeholder { get; init; }
    public string? Suffix { get; init; }
    public string? Value { get; init; }
    public bool Multi { get; init; }
    public int Max { get; init; } = 120;
    public bool Numeric { get; init; }
    public string? Helper { get; init; }
    public string? Error { get; init; }
    public bool Live { get; init; }
    public IReadOnlyList<PickRow> Suggestions { get; init; } = [];
    public PickAction? Primary { get; init; }
    public PickAction? Secondary { get; init; }
    public string? RecentTitle { get; init; }
    public IReadOnlyList<(string Text, string Meta)> Recent { get; init; } = [];
    // ask / choose
    public IReadOnlyList<PickAction> Buttons { get; init; } = [];
    // drawer
    public IReadOnlyList<(string Title, IReadOnlyList<PickRow> Rows)> Sections { get; init; } = [];

    /// <summary>What the old UI tests read: the lines a picker showed, or the recent lines under an input.</summary>
    public IReadOnlyList<string> Lines => Kind == "input" ? Recent.Select(r => r.Text).ToList()
        : Kind is "ask" or "choose" ? (Rows.Count > 0 ? Rows : Buttons.Select(b => new PickRow(b.Id, b.Label)).ToList()).Select(r => r.Label).ToList()
        : Rows.Select(r => r.Label).ToList();

    /// <summary>The one button the old list had (the footer action, the header chip, or the secondary).</summary>
    public PickAction? Button => FootAction ?? (SubAction is { } a ? new PickAction(a, Sub ?? "") : null) ?? FootLink ?? Secondary;

    /// <summary>A model of its own (the tray menu): shown as is.</summary>
    public JModel? Raw { get; init; }

    public JModel ToModel()
    {
        if (Raw is not null) return Raw;
        var j = JModel.Of(Kind).S("name", Name).S("title", Title).S("text", Kind == "ask" ? Title : null).T("back", Back);
        if (Sub is not null) { j.O("sub").S("text", Sub).S("action", SubAction).S("icon", SubIcon).End(); }
        if (Search is not null) j.O("search").S("placeholder", Search).End();
        j.S("enterHint", EnterHint).N("listHeight", ListHeight);
        Rows_(j, "rows", Rows);
        Box(j, "empty", Empty);
        Box(j, "none", None);
        if (Now is { } now) j.O("now").S("title", now.Title).S("sub", now.Sub).S("stopLabel", now.StopLabel).S("pauseLabel", NowPaused ? "كمّل" : "وقفة").T("paused", NowPaused).End();
        j.O("foot").S("hint", FootHint);
        Act(j, "action", FootAction);
        Act(j, "link", FootLink);
        j.End();
        if (Kind == "input")
        {
            j.O("field").S("icon", FieldIcon).S("placeholder", Placeholder).S("suffix", Suffix).S("value", Value).T("multi", Multi).N("max", Max).T("numeric", Numeric).End();
            j.S("helper", Helper).S("error", Error).T("live", Live);
            Rows_(j, "suggestions", Suggestions);
            Act(j, "primary", Primary);
            Act(j, "secondary", Secondary);
            if (Recent.Count > 0)
            {
                j.O("recent").S("title", RecentTitle ?? "").A("rows");
                foreach (var (text, meta) in Recent) j.Item().S("text", text).S("meta", meta).End();
                j.End().End();
            }
        }
        if (Buttons.Count > 0)
        {
            j.A("buttons");
            bool first = true;
            foreach (var b in Buttons) { j.Item().S("id", b.Id).S("label", b.Label).S("icon", b.Icon).T("primary", first).End(); first = false; }
            j.End();
        }
        if (Sections.Count > 0)
        {
            j.A("sections");
            foreach (var (title, rows) in Sections) { j.Item().S("title", title); Rows_(j, "rows", rows); j.End(); }
            j.End();
        }
        return j;
    }

    private static void Rows_(JModel j, string key, IReadOnlyList<PickRow> rows)
    {
        if (rows.Count == 0) return;
        j.A(key);
        foreach (var r in rows)
        {
            j.Item().S("id", r.Id.Length > 0 ? r.Id : null).S("label", r.Label).S("numText", r.NumText).S("meta", r.Meta).S("icon", r.Icon)
                .T("sel", r.Sel).T("play", r.Play).T("pinned", r.Pinned).T("strong", r.Strong).T("dim", r.Dim).T("check", r.Check).T("live", r.Live).T("soft", r.Soft);
            if (r.Num is { } n) j.N("num", n);
            j.End();
        }
        j.End();
    }

    private static void Box(JModel j, string key, PickBox? b)
    {
        if (b is null) return;
        j.O(key).S("title", b.Title).S("hint", b.Hint).S("icon", b.Icon).End();
    }

    private static void Act(JModel j, string key, PickAction? a)
    {
        if (a is null) return;
        j.O(key).S("id", a.Id).S("label", a.Label).S("icon", a.Icon);
        if (a.ConfirmQ is not null) j.O("confirm").S("q", a.ConfirmQ).S("yes", a.ConfirmYes ?? "").S("no", a.ConfirmNo ?? "").End();
        j.End();
    }
}
