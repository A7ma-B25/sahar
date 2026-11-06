namespace Sahar.Core.Senses;

/// <summary>Her gentle comments (spec §8.1 level 1): about the app in front and for how long — never its window title.</summary>
public static class CommentRules
{
    public static bool Allowed(bool typing, bool quiet, bool menuOpen, bool hidden) => !typing && !quiet && !menuOpen && !hidden;

    public static string? Pick(string? exe, TimeSpan onIt, int hour, bool firstToday)
    {
        if (firstToday && hour is >= 5 and < 12) return "صباح الخير ☀️";
        if (exe is null) return null;
        var app = AppNames.Friendly(exe);
        if (onIt >= TimeSpan.FromMinutes(120)) return app is null ? "صار لك ساعتين على نفس البرنامج، كيف ماشي؟" : $"صار لك ساعتين على {app}، كيف ماشي؟";
        if (onIt >= TimeSpan.FromMinutes(60)) return app is null ? "ساعة كاملة على نفس البرنامج… خود نفس 🌿" : $"ساعة كاملة على {app}… خود نفس 🌿";
        return null;
    }
}

public static class AppNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["code"] = "VS Code", ["winword"] = "Word",
        ["excel"] = "Excel", ["powerpnt"] = "PowerPoint", ["figma"] = "Figma", ["photoshop"] = "Photoshop", ["teams"] = "Teams",
        ["ms-teams"] = "Teams", ["slack"] = "Slack", ["whatsapp"] = "WhatsApp", ["telegram"] = "Telegram", ["discord"] = "Discord",
        ["notion"] = "Notion", ["outlook"] = "Outlook", ["olk"] = "Outlook", ["devenv"] = "Visual Studio", ["rider64"] = "Rider",
    };

    public static string? Friendly(string exe) => Names.GetValueOrDefault(PrivacyLists.ExeName(exe));
}
