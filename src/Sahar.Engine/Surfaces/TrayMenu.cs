using Sahar.Core.Activities;
using Sahar.Core.Settings;
using Sahar.Core.Surfaces;
using Sahar.Engine.Host;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// Her tray menu (V15Trays; owner ruling 9): her face and how she is, the activity as a segmented control, then
/// إعداداتي, الحكي, الدرج, فرجيني حركاتك, خبّيني ساعة, الطقس (a clear on/off with the city), مين أنا؟, دوّر على تحديث,
/// افتح مجلدي, and «سكّري سحورة» last. One language, the app's (Arabic unless English is chosen).
/// </summary>
internal static class TrayMenu
{
    private static readonly (string Id, string Icon, string Ar, string En)[] Items =
    [
        ("settings", "gear", "إعداداتي", "Settings"),
        ("chat", "compose", "الحكي", "Chat"),
        ("drawer", "drawer", "الدرج", "Drawer"),
        ("show", "sparkle", "فرجيني حركاتك", "Show me your moves"),
        ("hide", "hide", "خبّيني ساعة", "Hide for an hour"),
        ("weather", "rain", "الطقس", "Weather"),
        ("who", "user", "مين أنا؟", "Who am I?"),
        ("update", "update", "دوّر على تحديث", "Check for updates"),
        ("folder", "folder", "افتح مجلدي", "Open my folder"),
    ];

    public static PanelSpec Spec(Tray tray, ActivityLevel level, bool hidden, UiLanguage language)
    {
        bool en = language == UiLanguage.English;
        string L(string ar, string e) => en ? e : Address.ToYou(ar);
        string levelWord = level switch { ActivityLevel.Calm => L("هادية", "calm"), ActivityLevel.Lively => L("نشيطة", "lively"), _ => L("متوسطة", "medium") };
        var j = JModel.Of("tray").S("name", en ? "Sahar" : "سحورة")
            .S("status", en ? (hidden ? "Hidden" : "Awake") + ", " + levelWord : (hidden ? "مخبّاية" : "صاحية") + "، و" + levelWord);
        j.O("seg").S("label", L("نشاطها", "Activity")).S("value", level switch { ActivityLevel.Calm => "calm", ActivityLevel.Lively => "lively", _ => "medium" }).A("options")
            .Item().S("id", "calm").S("label", L("هادية", "Calm")).End()
            .Item().S("id", "medium").S("label", L("متوسطة", "Medium")).End()
            .Item().S("id", "lively").S("label", L("نشيطة", "Lively")).End()
            .End().End();
        j.A("items");
        var rows = new List<PickRow>();
        foreach (var (id, icon, ar, e) in Items)
        {
            if (id == "settings" && !tray.SettingsAvailable()) continue;
            string label = id == "show" && tray.ShowRunning ? L("وقّف العرض", "Stop the show") : L(ar, e);
            j.Item().S("id", id).S("label", label).S("icon", icon);
            if (id == "weather") j.B("toggle", tray.WeatherCity is not null).S("meta", tray.WeatherCity);
            j.End();
            rows.Add(new PickRow(id, label));
        }
        j.End();
        j.O("quit").S("id", "quit").S("label", L("سكّري سحورة", "Quit Sahar")).End();
        rows.Add(new PickRow("quit", L("سكّري سحورة", "Quit Sahar")));
        return new PanelSpec { Kind = "tray", Name = "tray", Title = en ? "Sahar" : "سحورة", Rows = rows, Raw = j };
    }
}
