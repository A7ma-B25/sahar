using Sahar.Core.Geometry;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Tools;

/// <summary>Quick note (spec §7.1): a typing bubble with the last three notes under it; Enter saves on this PC.</summary>
internal sealed class NoteTool(TextBubble bubble, NotesStore store, Func<RectF> body, Action<string, bool> express)
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

    public void Open() =>
        bubble.Show(body(), "اكتب ملاحظة…", store.Latest(3).Select(n => n.Text).ToList(), text =>
        {
            try
            {
                if (!store.Add(text, DateTimeOffset.Now)) return;
                if (store.SaveFailed) Log.Write("note not saved to notes.json (kept for now)");
                else Log.Write("note: saved");
                express("notification", true);
            }
            catch (Exception e) { Log.Write($"note error: {e}"); }
        });
}
