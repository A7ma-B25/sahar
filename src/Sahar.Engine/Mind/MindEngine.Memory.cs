using System.Text;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Protocol;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Mind;

// STREAM B owns this file (feat/0.14-memory): music titles/times (opt-in), forget and erase hooks, drafts in his voice.
internal sealed partial class MindEngine
{
    private MindStore? _memory;
    private MusicLearner? _music;
    private readonly object _musicGate = new();
    private bool _musicOn;

    partial void AttachMemory()
    {
        LearnConsent Consent() => LearnConsent.From(Settings());
        _memory = new MindStore(new SecretBox(MindPaths.Personal, KeyWrap.Default), () => DateTimeOffset.Now, Consent, () => Settings().MindEpisodeDays);
        _music = new MusicLearner(_memory, Consent, () => DateTimeOffset.Now);
        _musicOn = Settings().MindLearnMusic;
        SettingsChanged += s =>
        {
            if (s.MindLearnMusic == _musicOn) return;
            _musicOn = s.MindLearnMusic;
            Log.Write($"mind: music learning {(_musicOn ? "on" : "off")}");
        };

        // What he listens to: only with «شو عم تسمع» on and music playing; the title is read and kept encrypted, never
        // logged. Checked every 30 s on the UI thread; the store's work runs off it.
        var timer = Ui.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += async (_, _) =>
        {
            try
            {
                if (!Settings().MindLearnMusic || !Media.MusicPlaying) return;
                if (await Media.NowPlayingAsync() is not { } now) return;
                _ = Task.Run(() => Hear(now.Title, now.Artist));
            }
            catch (Exception e) { Log.Write($"mind: music check failed ({e.GetType().Name})"); }
        };
        if (!TestMode) timer.Start();

        if (TestMode)
            Pipe.Register("debug-mind-music", r =>
            {
                var title = Text(r.Args?.GetValueOrDefault("title64"));
                var artist = Text(r.Args?.GetValueOrDefault("artist64"));
                bool kept = title is not null && Hear(title, artist);
                return new PipeReply(true, $"kept={(kept ? "true" : "false")} count={_memory.Listens().Count}");
            });
    }

    private bool Hear(string title, string? artist)
    {
        try
        {
            lock (_musicGate) return _music!.Heard(title, artist);
        }
        catch (Exception e)
        {
            Log.Write($"mind: music not kept ({e.GetType().Name})");
            return false;
        }
    }

    private static string? Text(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }
}
