using Sahar.Core.Geometry;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Tools;

/// <summary>Quick note (spec §7.1): a typing bubble with the last three notes under it; Enter saves on this PC.</summary>
internal sealed class NoteTool(TextBubble bubble, NotesStore store, Func<RectF> body, Action<string, bool> express)
{
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
