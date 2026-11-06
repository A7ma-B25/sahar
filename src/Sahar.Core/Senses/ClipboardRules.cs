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

    public static ClipKind KindOf(string text)
    {
        var t = text.Trim();
        bool link = !t.Contains(' ') && !t.Contains('\n') &&
            (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             t.StartsWith("www.", StringComparison.OrdinalIgnoreCase));
        return link ? ClipKind.Link : ClipKind.Text;
    }
}
