using Sahar.Core.Settings;
using Windows.Devices.Geolocation;

namespace Sahar.Settings;

/// <summary>"Use Windows location" (plan Ruling 5): the OS Geolocator, 10 s at most. Coordinates are never logged.</summary>
internal sealed class Locator : ILocator
{
    public async Task<(double Lat, double Lon)?> LocateAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) { SettingsLog.Write("locate: not allowed"); return null; }
            var geo = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var pos = await geo.GetGeopositionAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            SettingsLog.Write("locate: found");
            return (pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude);
        }
        catch (Exception e)
        {
            SettingsLog.Write($"locate: failed ({e.GetType().Name})");
            return null;
        }
    }
}
