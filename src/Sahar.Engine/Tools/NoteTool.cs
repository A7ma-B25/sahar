using Sahar.Core.Geometry;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Surfaces;

namespace Sahar.Engine.Tools;

/// <summary>Quick note (spec §7.1; V15Inputs): a card with up to three lines and the last three notes under it, dimmed;
/// Enter (or «احفظها») saves on this PC, Shift+Enter is a new line.</summary>
internal sealed class NoteTool(Panel panel, NotesStore store, Action<string, bool> express)
{
    /// <summary>0.14: the store, for the Mind's note tool and its undo.</summary>
    public NotesStore Store => store;

    /// <summary>Saves a note typed in her chat row («ملاحظة …»); false when it was not kept.</summary>
    public bool Save(string text)
    {
        try
        {
            if (!store.Add(text, DateTimeOffset.Now)) return false;
            Log.Write(store.SaveFailed ? "note not saved to notes.json (kept for now)" : "note: saved");
            express("notification", true);
            return true;
        }
        catch (Exception e) { Log.Write($"note error: {e.GetType().Name}"); return false; }
    }

    public void Open() => panel.Show(new PanelSpec
    {
        Kind = "input", Name = "note", Title = Sahar.Core.Settings.Address.ToYou("شو بدك اكتب؟"), FieldIcon = null, Placeholder = Sahar.Core.Settings.Address.ToYou("اكتب ملاحظة…"),
        Multi = true, Max = 500, Primary = new PickAction("save", "احفظها"), RecentTitle = "آخر الملاحظات",
        Recent = store.Latest(3).Select(n => (n.Text.ReplaceLineEndings(" "), Sahar.Engine.Tools.ClipboardTool.Age(DateTimeOffset.Now - n.At))).ToList(),
    }, e =>
    {
        if (e.Kind != "submit" || string.IsNullOrWhiteSpace(e.Value)) return;
        panel.Done();
        Save(e.Value!);
    });
}
