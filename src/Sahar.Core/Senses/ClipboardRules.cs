namespace Sahar.Core.Senses;

public enum ClipKind { Text, Link, Image }

/// <summary>Spec §7.2: what she may keep from the clipboard.</summary>
public static class ClipboardRules
{
    /// <param name="formatNames">Registered format names on the clipboard.</param>
    /// <param name="canIncludeInHistory">The "CanIncludeInClipboardHistory" DWORD, if present.</param>
    /// <param name="ownerExe">Executable of the clipboard's owner window.</param>
    public static bool ShouldKeep(IReadOnlyCollection<string> formatNames, uint? canIncludeInHistory, string ownerExe) =>
        !formatNames.Contains("ExcludeClipboardContentFromMonitorProcessing") &&
        !formatNames.Contains("Clipboard Viewer Ignore") &&
        canIncludeInHistory is not 0 &&
        !PrivacyLists.IsPasswordManager(ownerExe);

    /// <summary>One token of 8–128 characters with at least three of: lower, upper, digit, symbol (and not a link).</summary>
    public static bool LooksSecret(string text)
    {
        var t = text.Trim();
        if (t.Length is < 8 or > 128 || t.Any(char.IsWhiteSpace) || KindOf(t) == ClipKind.Link) return false;
        int classes = (t.Any(char.IsLower) ? 1 : 0) + (t.Any(char.IsUpper) ? 1 : 0) + (t.Any(char.IsDigit) ? 1 : 0) +
                      (t.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        return classes >= 3;
    }

    /// <summary>
    /// Browser password managers (extensions) copy from the browser's own process with no "private" mark: a secret-
    /// looking copy from a browser is kept in memory only, masked in the list, and never written to disk.
    /// </summary>
    public static bool KeepInMemoryOnly(string ownerExe, string text) =>
        PrivacyLists.ExeName(ownerExe) is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi" or "arc" or "chromium"
        && LooksSecret(text);

    public static ClipKind KindOf(string text)
    {
        var t = text.Trim();
        bool link = !t.Contains(' ') && !t.Contains('\n') &&
            (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             t.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
        return link ? ClipKind.Link : ClipKind.Text;
    }
}
