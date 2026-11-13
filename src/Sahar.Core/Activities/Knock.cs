using Sahar.Core.Brain;
using Sahar.Core.Geometry;

namespace Sahar.Core.Activities;

/// <summary>What an activity does while something is fullscreen: shown as usual, a knock from the top edge, kept for
/// «وإنت غايب», or nothing.</summary>
public enum FullscreenAct { Show, Knock, Keep, Nothing }

/// <summary>
/// Fullscreen (spec 0.13 §1.4): on another monitor everything shows as usual but without sounds; on hers she hides, and
/// only Claude asking, a storm, gale or heat alert, and a low battery knock: a small tag drops from the top edge of that
/// monitor for 4 s, with no buttons. Finishes (Claude done, copy done) and the quiet alerts are kept for «وإنت غايب».
/// </summary>
public static class Knock
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(4);

    public static FullscreenAct Decide(ActivityKind k, FullscreenWhere where, WeatherAlert? alert = null)
    {
        if (where != FullscreenWhere.Hers) return FullscreenAct.Show;
        return k switch
        {
            ActivityKind.ClaudeAsk or ActivityKind.BatteryLow => FullscreenAct.Knock,
            ActivityKind.WeatherAlert => alert is WeatherAlert.Storm or WeatherAlert.Wind or WeatherAlert.Heat ? FullscreenAct.Knock : FullscreenAct.Keep,
            ActivityKind.ClaudeDone or ActivityKind.CopyDone => FullscreenAct.Keep,
            _ => FullscreenAct.Nothing,
        };
    }

    /// <summary>Her sounds play only with nothing fullscreen.</summary>
    public static bool Sounds(FullscreenWhere where) => where == FullscreenWhere.None;

    /// <summary>The knock window: the tag plus room for its shadow, centred on the monitor's top edge; never the whole monitor.</summary>
    public static RectI Rect(RectI monitor, SizeI tag, float dpi)
    {
        int side = (int)MathF.Ceiling(TagPlace.ShadowSideDip * dpi), below = (int)MathF.Ceiling((TagPlace.ShadowBelowDip + 8) * dpi);
        int w = Math.Min(tag.Width + 2 * side, monitor.Width - 2), h = Math.Min(tag.Height + below, monitor.Height - 2);
        int left = monitor.Left + (monitor.Width - w) / 2;
        return new RectI(left, monitor.Top, left + w, monitor.Top + h);
    }
}
