using Sahar.Core.Art;
using Sahar.Core.Design;

namespace Sahar.Core.Settings;

/// <summary>After a settings change: a hop (or, when she cannot hop, her joy face), or nothing at all.</summary>
public enum Cheer { None, HopOrFace }

/// <summary>Small rules of the settings app and of her answer to it (review fix pass, 2026-11-13).</summary>
public static class SettingsAppPolicy
{
    /// <summary>WebView2's own background traffic switched off (the README promises the app never goes online):
    /// no background networking, component or variations updates, domain reliability reports, sync, pings, phishing
    /// lookups. Test mode also keeps a covered window painting.</summary>
    public static string BrowserArguments(bool testMode)
    {
        var features = new List<string> { "msSmartScreenProtection", "AutofillServerCommunication", "OptimizationHints", "MediaRouter" };
        var args = new List<string>
        {
            "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability", "--disable-sync",
            "--no-pings", "--disable-client-side-phishing-detection", "--disable-default-apps", "--no-first-run",
        };
        if (testMode)
        {
            features.Add("CalculateNativeWinOcclusion");
            args.AddRange(["--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding"]);
        }
        args.Add("--disable-features=" + string.Join(',', features));   // one flag: Chromium keeps only the last of several
        return string.Join(' ', args);
    }

    /// <summary>Dark from her setting; "like Windows" follows Windows' apps setting.</summary>
    public static bool IsDark(Appearance a, bool systemDark) => a switch { Appearance.Dark => true, Appearance.Light => false, _ => systemDark };

    /// <summary>The window's colour before the page paints (no light flash in dark mode).</summary>
    public static Rgba Background(Appearance a, bool systemDark) => Tokens.For(IsDark(a, systemDark)).Bg;

    /// <summary>Why the window could not open, in her voice: WebView2 is named only when it is really missing.</summary>
    public static string StartFailed(bool runtimeMissing) => runtimeMissing
        ? "ما قدرت افتح الإعدادات: بدي \"WebView2\" من مايكروسوفت."
        : "ما قدرت افتح الإعدادات هلق. سكّرها وجرّب كمان مرة.";

    /// <summary>A settings change makes her hop with her joy face (or the face alone when she cannot hop) — but never in
    /// the adhan's silence, and never over another face (sick, sad, an event's): a hop always shows the joy face.</summary>
    public static Cheer CheerFor(bool adhanQuiet, string expression) =>
        !adhanQuiet && expression is "idle" or "music" ? Cheer.HopOrFace : Cheer.None;
}
