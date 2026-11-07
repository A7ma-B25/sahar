using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Tools;

/// <summary>
/// 🎵 الموسيقى (spec §7.1): ⏮️ ⏯️ ⏭️ for whatever app plays (Windows' media controls), and her bubble says
/// what is playing. The title and artist are shown, never logged (spec §14).
/// </summary>
internal sealed class MusicTool(DispatcherQueue ui, Media media, Action<string> say)
{
    public const string NothingPlaying = "ما في شي عم يشتغل";

    /// <summary>Her bubble: "🎵 {title} — {artist}", or that nothing is playing.</summary>
    public void SayNowPlaying() => media.NowPlayingAsync().ContinueWith(t =>
    {
        var now = t.IsCompletedSuccessfully ? t.Result : null;
        string line = now is { } n ? (n.Artist.Length > 0 ? $"🎵 {n.Title} — {n.Artist}" : $"🎵 {n.Title}") : NothingPlaying;
        ui.TryEnqueue(() => say(line));
    });

    public void Press(string id)
    {
        var control = id switch { "music-prev" => MusicControl.Previous, "music-next" => MusicControl.Next, _ => MusicControl.Toggle };
        Log.Write($"music: {control}");
        media.ControlAsync(control).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || !t.Result) ui.TryEnqueue(() => say(NothingPlaying));
        });
    }

    public string Describe() => $"session={(media.HasSession ? "true" : "false")}";
}
