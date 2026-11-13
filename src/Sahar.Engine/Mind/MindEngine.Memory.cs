using System.Runtime.InteropServices;
using System.Text;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Settings;
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

        // «انسي كل شي» from the «عنك» page: the settings app shredded mind\ (key first); she says it, softly.
        Pipe.Register("mind-forgot", _ =>
        {
            Log.Write("mind: forgot everything");
            Speech.Say(Address.ToYou("نسيت كل شي… مرحبا من جديد 🌙"));
            return new PipeReply(true, "ok=true");
        });

        // A draft in his voice (Ruling 6): shown with «بصوتك» and one tap to copy. She never sends it anywhere.
        Received += msg =>
        {
            if (msg.T != "draft" || string.IsNullOrWhiteSpace(msg.Text)) return;
            ShowDraft(msg.Text);
        };

        if (TestMode)
        {
            Pipe.Register("debug-mind-draft", r =>
            {
                var text = Text(r.Args?.GetValueOrDefault("text64"));
                if (text is null) return new PipeReply(false, "no-text");
                ShowDraft(text);
                return new PipeReply(true, $"open={(ListBubble.IsOpen ? "true" : "false")} button64={Convert.ToBase64String(Encoding.UTF8.GetBytes(ListBubble.ButtonText))} lines={ListBubble.Lines}");
            });
            // The UI stage's way to put proposals in (the Mind would, from their chats): through the real store and key.
            Pipe.Register("debug-mind-propose", r =>
            {
                var text = Text(r.Args?.GetValueOrDefault("text64"));
                var cat = Enum.TryParse<ProfileCategory>(r.Args?.GetValueOrDefault("category"), true, out var c) ? c : ProfileCategory.Likes;
                int days = int.TryParse(r.Args?.GetValueOrDefault("daysAgo"), out var d) ? d : 0;
                if (text is null) return new PipeReply(false, "no-text");
                var item = _memory.Propose(text, cat, new ItemSource(LearnSource.Chats, DateTimeOffset.Now.AddDays(-days)), Sensitivity.None);
                if (item is not null && r.Args?.GetValueOrDefault("confirm") == "true") _memory.Confirm(item.Id);
                return new PipeReply(true, $"ok={(item is null ? "false" : "true")} id={item?.Id ?? ""}");
            });
            Pipe.Register("debug-mind-memory", _ =>
                new PipeReply(true, $"listens={_memory.Listens().Count} files={(Directory.Exists(MindPaths.Personal) ? Directory.EnumerateFileSystemEntries(MindPaths.Personal).Count() : 0)}"));
        }

        if (TestMode)
            Pipe.Register("debug-mind-music", r =>
            {
                var title = Text(r.Args?.GetValueOrDefault("title64"));
                var artist = Text(r.Args?.GetValueOrDefault("artist64"));
                bool kept = title is not null && Hear(title, artist);
                return new PipeReply(true, $"kept={(kept ? "true" : "false")} count={_memory.Listens().Count}");
            });
    }

    private void ShowDraft(string text)
    {
        Log.Write($"mind: draft shown ({text.Length} chars)");   // its length only, never its words
        var lines = Wrap(text, 34);
        ListBubble.Show(Motion.Body, Draft.SelfLabel + " ✍️", lines, "انسخها 📋", _ => { },
            () => Ui.TryEnqueue(() =>
            {
                bool ok = Copy(text);
                Log.Write($"mind: draft {(ok ? "copied" : "not copied")}");
                Speech.Say(ok ? Address.ToYou("نسختها! الصقها وين ما بدك 📋") : "ما قدرت انسخها، جرّب كمان مرة؟");
            }));
    }

    /// <summary>Her bubble's lines: words kept whole, about <paramref name="width"/> characters a line.</summary>
    internal static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line.ToString()); line.Clear(); }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0) lines.Add(line.ToString());
        }
        return lines.Count > 0 ? lines : [text];
    }

    /// <summary>The draft on the clipboard as Unicode text (one tap; he pastes it himself).</summary>
    private static bool Copy(string text)
    {
        const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 2;
        var data = Encoding.Unicode.GetBytes(text + "\0");
        bool open = false;
        for (int i = 0; i < 5 && !open; i++)
        {
            open = Native.OpenClipboard(0) != 0;
            if (!open) Thread.Sleep(20);
        }
        if (!open) return false;
        try
        {
            Native.EmptyClipboard();
            nint mem = Native.GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
            if (mem == 0) return false;
            nint p = Native.GlobalLock(mem);
            Marshal.Copy(data, 0, p, data.Length);
            Native.GlobalUnlock(mem);
            if (Native.SetClipboardData(CF_UNICODETEXT, mem) == 0) { Native.GlobalFree(mem); return false; }
            return true;
        }
        finally { Native.CloseClipboard(); }
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
