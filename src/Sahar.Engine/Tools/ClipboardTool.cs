using Sahar.Core.Geometry;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Sahar.Engine.Surfaces;
using A = Sahar.Core.Settings.Address;

namespace Sahar.Engine.Tools;

/// <summary>الحافظة (spec §7.1; V15Lists): the last 20 things you copied; a click puts one back; «امسح الكل» asks «أكيد؟»
/// first. What was copied never reaches the log.</summary>
internal sealed class ClipboardTool(Panel panel, ClipboardSense sense, Func<DateTimeOffset>? now = null)
{
    public void Open() => panel.Show(Spec(), e =>
    {
        if (e.Kind == "pick" && int.TryParse(e.Value, out int i)) { panel.Done(); Pick(i); }
        else if (e.Kind == "action" && e.Value == "clear") { Clear(); panel.Update(Spec()); }
    });

    private PanelSpec Spec()
    {
        var items = sense.History.Items;
        var at = (now ?? (() => DateTimeOffset.Now))();
        return new PanelSpec
        {
            Kind = "list", Name = "clipboard", Title = A.ToYou("آخر شي نسخته"), Sub = items.Count > 0 ? A.ToYou("كبسة عليه بترجّعه للحافظة") : null,
            Rows = items.Select((it, i) => new PickRow(i.ToString(System.Globalization.CultureInfo.InvariantCulture), Line(it))
                { Icon = it.Secret ? "lock" : it.Kind == ClipKind.Image ? "image" : "copy", Meta = Age(at - it.At) }).ToList(),
            ListHeight = items.Count == 0 ? 150 : 302,
            Empty = new PickBox(A.ToYou("لسا ما نسخت شي"), A.ToYou("أي شي بتنسخه بيطلع هون."), "copy"),
            FootAction = items.Count == 0 ? null : new PickAction("clear", A.ToYou("امسح الكل"), "trash", "أكيد؟", A.ToYou("إي، امسح"), "لا"),
        };
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

    private static string Line(ClipItem i) => i.Secret ? "••••••" : i.Kind switch
    {
        ClipKind.Image => Sahar.Core.Text.Digits.ToArabicIndic($"صورة {i.Width}×{i.Height}"),
        _ => Short(i.Text),
    };

    /// <summary>«هلق», «٣ د», «١ س», «مبارح», or the date.</summary>
    public static string Age(TimeSpan ago)
    {
        if (ago.TotalMinutes < 1) return "هلق";
        if (ago.TotalHours < 1) return Sahar.Core.Text.Digits.ToArabicIndic($"{(int)ago.TotalMinutes} د");
        if (ago.TotalDays < 1) return Sahar.Core.Text.Digits.ToArabicIndic($"{(int)ago.TotalHours} س");
        if (ago.TotalDays < 2) return "مبارح";
        return Sahar.Core.Text.Digits.ToArabicIndic($"{(int)ago.TotalDays} يوم");
    }

    private static string Short(string? t)
    {
        var one = (t ?? "").ReplaceLineEndings(" ").Trim();
        return one.Length <= 60 ? one : one[..60] + "…";
    }
}
