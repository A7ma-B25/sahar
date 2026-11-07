using Sahar.Core.Geometry;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Tools;

/// <summary>📋 الحافظة (spec §7.1): the last 20 things you copied; a click puts one back; "امسح الكل" clears them.</summary>
internal sealed class ClipboardTool(ListBubble list, ClipboardSense sense, Func<RectF> body)
{
    public void Open()
    {
        var items = sense.History.Items;
        var lines = items.Select(Line).ToList();
        if (lines.Count == 0) lines.Add(Sahar.Core.Settings.Address.ToYou("لسا ما نسخت شي"));
        list.Show(body(), "📋 آخر شي نسخته", lines, "امسح الكل", i => { if (i < items.Count) Pick(i); }, Clear);
    }

    public bool Pick(int index)
    {
        var items = sense.History.Items;
        if (index < 0 || index >= items.Count) return false;
        bool ok = sense.PutBack(items[index]);
        Log.Write(ok ? "clipboard: put back" : "clipboard: put back failed");
        return ok;
    }

    public void Clear()
    {
        sense.History.Clear();
        Log.Write("clipboard: cleared");
    }

    private static string Line(ClipItem i) => i.Secret ? "🔒 ••••••" : i.Kind switch
    {
        ClipKind.Image => $"🖼️ صورة {i.Width}×{i.Height}",
        ClipKind.Link => "🔗 " + Short(i.Text),
        _ => "📄 " + Short(i.Text),
    };

    private static string Short(string? t)
    {
        var one = (t ?? "").ReplaceLineEndings(" ").Trim();
        return one.Length <= 60 ? one : one[..60] + "…";
    }
}
