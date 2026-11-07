namespace Sahar.Core.Senses;

/// <summary>Spec §14: apps she never tracks and whose clipboard she never keeps.</summary>
public static class PrivacyLists
{
    public static readonly string[] PasswordManagers =
        ["keepass", "keepassxc", "1password", "bitwarden", "lastpass", "dashlane", "keeper", "enpass", "roboform", "nordpass", "protonpass", "proton pass"];

    private static readonly string[] PrivateMarks = ["InPrivate", "Incognito", "Private Browsing", "نافذة خاصة", "التصفح الخاص"];

    /// <summary>The executable's name without folder or ".exe", lower case ("" if none).</summary>
    public static string ExeName(string path)
    {
        var name = path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        return (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).ToLowerInvariant();
    }

    public static bool IsPasswordManager(string exePath)
    {
        var n = ExeName(exePath);
        return n.Length > 0 && PasswordManagers.Any(p => n.Contains(p.Replace(" ", ""), StringComparison.Ordinal));
    }

    public static bool IsPrivateWindow(string title) => PrivateMarks.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase));
}
