namespace Sahar.Core.Settings;

/// <summary>
/// What changed between two settings, by group (plan 0.10c Task 2): the engine's settings reload applies only these
/// and logs their names (never values). Groups, in order: size, activity, address (name, gender), menu (items, header,
/// opening, place, light/dark), stars, life (comments, mood, play), prayer, weather, shortcut, quran.
/// </summary>
public static class SettingsChanges
{
    public static IReadOnlyList<string> Between(SaharSettings a, SaharSettings b)
    {
        var list = new List<string>();
        if (a.Size != b.Size) list.Add("size");
        if (a.Activity != b.Activity) list.Add("activity");
        if (a.UserName != b.UserName || a.UserGender != b.UserGender || a.HelloDone != b.HelloDone) list.Add("address");
        if (!SameItems(a.MenuItems, b.MenuItems) || a.MenuHeader != b.MenuHeader || a.MenuOpen != b.MenuOpen
            || a.MenuPlace != b.MenuPlace || a.Appearance != b.Appearance) list.Add("menu");
        if (a.InnerStars != b.InnerStars) list.Add("stars");
        if (a.AwarenessEnabled != b.AwarenessEnabled || a.AutoMood != b.AutoMood || a.PlaysAlone != b.PlaysAlone) list.Add("life");
        if (a.PrayerCity != b.PrayerCity || a.PrayerLatitude != b.PrayerLatitude || a.PrayerLongitude != b.PrayerLongitude
            || a.PrayerMethod != b.PrayerMethod || a.PrayerHanafiAsr != b.PrayerHanafiAsr || a.PrayerReminderOn != b.PrayerReminderOn
            || a.PrayerReminderMinutes != b.PrayerReminderMinutes) list.Add("prayer");
        if (a.WeatherEnabled != b.WeatherEnabled || a.WeatherCity != b.WeatherCity || a.WeatherLatitude != b.WeatherLatitude
            || a.WeatherLongitude != b.WeatherLongitude) list.Add("weather");
        if (a.MenuShortcut != b.MenuShortcut) list.Add("shortcut");
        if (a.QuranReciter != b.QuranReciter) list.Add("quran");
        return list;
    }

    private static bool SameItems(IReadOnlyList<string>? x, IReadOnlyList<string>? y) =>
        x is null ? y is null : y is not null && x.SequenceEqual(y);
}
