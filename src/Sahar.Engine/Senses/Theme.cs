using Microsoft.Win32;

namespace Sahar.Engine.Senses;

/// <summary>Whether Windows apps are dark (Settings → Personalisation → Colours → "Choose your app mode").</summary>
internal static class Theme
{
    public static bool AppsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }
}
