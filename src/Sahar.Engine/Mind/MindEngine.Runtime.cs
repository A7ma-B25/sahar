using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Protocol;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Mind;

// STREAM A owns this file (feat/0.14-brain): the Mind process, its private pipe, warm/unload, models and downloads.
internal sealed partial class MindEngine
{
    private NamedPipeServerStream? _mindPipe;
    private System.Threading.Channels.Channel<string>? _outbox;
    private Process? _mindProc;
    private readonly object _mindGate = new();
    private string _phase = "none";
    private DownloadProgress? _lastProgress;
    private int _saidQuarter;
    private bool _wantsReady;   // the download just finished: say «هلق صرت فكّر لحالي» on ready
    private ModelManifest? _models;
    private int _starts;

    /// <summary>The Mind's exe next to the engine; tests may point elsewhere.</summary>
    private static string MindExe => Path.Combine(AppContext.BaseDirectory, "Sahar.Mind.exe");

    /// <summary>CI: <c>SAHAR_MIND_SCRIPT</c> runs the Mind on a scripted fake model (no llama-server).</summary>
    private static string? Script => Environment.GetEnvironmentVariable("SAHAR_MIND_SCRIPT") is { Length: > 0 } s ? s : null;

    partial void AttachRuntime()
    {
        Send = EnsureAndSend;
        SendIfRunning = m =>
        {
            lock (_mindGate) return _outbox is { } o && _mindProc is { HasExited: false } && o.Writer.TryWrite(MindWire.Encode(m));
        };
        Ready = () => Settings().MindEnabled && (Script is not null || ActiveModel() is not null);
        Received += OnRuntimeMessage;
        // The settings app's «عقلي» and «النماذج» (production commands; replies are key=value data).
        Pipe.Register("mind-status", _ => new PipeReply(true, Status()));
        Pipe.Register("mind-download", r =>
        {
            var op = Arg(r, "op") ?? "start";
            if (op is not ("check" or "start" or "pause" or "resume" or "cancel")) return new PipeReply(false, "bad op");
            // «نزّليه» on the models page is his yes (owner answer: never without it).
            EnsureAndSend(new MindMsg { T = "download", Op = op, Model = Arg(r, "model") });
            Log.Write($"mind: download {op}");
            return new PipeReply(true, "ok=true");
        });
        Pipe.Register("mind-bench", r => { EnsureAndSend(new MindMsg { T = "bench", Model = Arg(r, "quick") == "true" ? "quick" : null }); return new PipeReply(true, "ok=true"); });
        Pipe.Register("mind-model", r =>
        {
            var op = Arg(r, "op");
            if (op is not ("use" or "remove") || Arg(r, "model") is not { } id) return new PipeReply(false, "bad model op");
            EnsureAndSend(new MindMsg { T = "model", Op = op, Model = id });
            return new PipeReply(true, "ok=true");
        });
        Pipe.Register("mind-reload", _ => { EnsureAndSend(new MindMsg { T = "store-changed" }); return new PipeReply(true, "ok=true"); });
        // 0.17 «العقل أونلاين»: the key goes once to the Mind, which seals it (never logged, never kept here).
        Pipe.Register("mind-online", r =>
        {
            var op = Arg(r, "op");
            if (op is not ("key" or "forget" or "test" or "status" or "bench")) return new PipeReply(false, "bad online op");
            string? key = null;
            if (op == "key")
            {
                try { key = Encoding.UTF8.GetString(Convert.FromBase64String(Arg(r, "key64") ?? "")); }
                catch (FormatException) { return new PipeReply(false, "bad key"); }
            }
            if (op is "key" or "forget" or "test" && Arg(r, "provider") is not ("groq" or "openrouter" or "custom")) return new PipeReply(false, "bad provider");
            bool sent = EnsureAndSend(new MindMsg { T = "online", Op = op, Model = Arg(r, "provider"), Text = key });
            Log.Write($"mind: online {op}");
            return new PipeReply(sent, sent ? "ok=true" : "no mind");
        });
        Pipe.Register("mind-online-status", _ => new PipeReply(true, "json64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            _online is { } o ? System.Text.Json.JsonSerializer.Serialize(o, MindJson.Default.OnlineInfo) : "null")) + $" seq={_onlineSeq}"));
        if (TestMode)
        {
            Pipe.Register("debug-mind", _ => new PipeReply(true, Describe()));
            Pipe.Register("mind-offer", _ => { OfferDownloadQuestion(); return new PipeReply(true); });
            Pipe.Register("mind-unload", _ => { EnsureAndSend(new MindMsg { T = "unload" }); return new PipeReply(true); });
            Pipe.Register("mind-warm", _ => { WarmUp(); return new PipeReply(true); });
        }
    }

    private static string? Arg(PipeRequest r, string key) => r.Args?.TryGetValue(key, out var v) == true ? v : null;

    /// <summary>0.14.1 fix 6: read from models.json at most every 5 s (and again when the Mind sends its list). It was
    /// read and parsed on her UI thread for every header redraw (each streamed word of a turn, via the today fact) and
    /// every «mind-status» poll.</summary>
    private ModelEntry? ActiveModel()
    {
        long now = Environment.TickCount64;
        if (_activeAt != 0 && now - _activeAt < 5000) return _active;
        try { _active = new ModelRegistry(MindPaths.Models).Active; }
        catch (Exception) { _active = null; }
        _activeAt = now;
        return _active;
    }

    private ModelEntry? _active;
    private long _activeAt;

    /// <summary>He started typing in the chat row: load the model now so the first answer is quick (spec §3.1).</summary>
    public void WarmUp()
    {
        if (Ready()) EnsureAndSend(new MindMsg { T = "warm" });
    }

    /// <summary>Starts the Mind if needed and sends the line (queued until it connects). Never blocks the caller: lines
    /// go through an outbox that a writer task drains, while a reader task always drains the other way (run 43586768997:
    /// both sides writing at once on unbuffered pipes deadlocked the UI thread). False when there is no Mind.</summary>
    private bool EnsureAndSend(MindMsg msg)
    {
        if (!File.Exists(MindExe)) return false;
        var line = MindWire.Encode(msg);
        lock (_mindGate)
        {
            if (_outbox is null || _mindProc is not { HasExited: false }) StartMind();
            return _outbox?.Writer.TryWrite(line) == true;
        }
    }

    private void StartMind()
    {
        _outbox?.Writer.TryComplete();
        _outbox = null;
        var name = "sahar-mind-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 1 << 16, 1 << 16);
        var psi = new ProcessStartInfo(MindExe) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--pipe");
        psi.ArgumentList.Add(name);
        psi.ArgumentList.Add("--parent");
        psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Script is { } script) { psi.ArgumentList.Add("--script"); psi.ArgumentList.Add(script); }
        try { _mindProc = Process.Start(psi); }
        catch (Exception e) { Log.Write($"mind: could not start ({e.GetType().Name})"); pipe.Dispose(); return; }
        _starts++;
        Log.Write("mind: started");
        var outbox = System.Threading.Channels.Channel.CreateUnbounded<string>(new() { SingleReader = true });
        _outbox = outbox;
        _mindPipe = pipe;
        // The first line on the connection, before anything queued: the tools this engine has (stream C's registry),
        // none for a model the bench marked chat-only (its commands stay with the parser).
        outbox.Writer.TryWrite(MindWire.Encode(Hello(toolCalls: Script is not null || ActiveModel()?.Ability != ModelAbility.ChatOnly)));
        _ = Task.Run(async () =>
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await pipe.WaitForConnectionAsync(cts.Token);
                var reading = Task.Run(async () =>
                {
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1 << 16, leaveOpen: true);
                    while (await reader.ReadLineAsync() is { } line)
                    {
                        if (MindWire.Decode(line) is not { } m) { Log.Write("mind: dropped a line"); continue; }
                        Ui.TryEnqueue(() => { try { Raise(m); } catch (Exception e) { Log.Write($"mind: handler failed ({e.GetType().Name})"); } });
                    }
                });
                var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1 << 16, leaveOpen: true) { NewLine = "\n" };
                var writing = Task.Run(async () =>
                {
                    await foreach (var l in outbox.Reader.ReadAllAsync())
                    {
                        await writer.WriteLineAsync(l);
                        await writer.FlushAsync();
                    }
                });
                await Task.WhenAny(reading, writing);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
            lock (_mindGate)
            {
                outbox.Writer.TryComplete();
                if (ReferenceEquals(_outbox, outbox)) { _outbox = null; _mindPipe = null; }
            }
            try { pipe.Dispose(); } catch (Exception) { }
            Ui.TryEnqueue(() => { _phase = "none"; Log.Write("mind: gone"); });
        });
    }

    private void OnRuntimeMessage(MindMsg m)
    {
        switch (m.T)
        {
            case "state": _phase = m.Phase ?? _phase; break;
            case "ready":
                _phase = "ready";
                if (m.Ready is { } r) Log.Write($"mind: ready ({r.Backend}, {r.LoadMs} ms, vram {r.VramMB} MB)");
                if (_wantsReady) { _wantsReady = false; Speech.Say(Address.For(Address.You, RuntimeText.Done)); }
                break;
            case "online-status" when m.Online is { } info:
                _online = info;
                _onlineSeq++;
                OnlineKeysSeen(info);
                break;
            case "models":
                _models = new ModelManifest(m.Models ?? [], m.Model);
                _activeAt = 0;   // read again on the next ask
                break;
            case "progress" when m.Progress is { } p: Progress(p); break;
            case "bench-result" when m.Bench is { } b:
                Log.Write($"mind: bench {b.TokPerSec:0.#} tok/s, tools {b.ToolPass}/{b.ToolTotal}");
                break;
            case "error":
                Log.Write($"mind: error {m.Error}");
                if (m.Error == "model-missing" && m.Id is null) _phase = "none";
                break;
        }
    }

    /// <summary>Her bubble follows the download (owner answer): blocked (plain words + the VPN hint), the start, each
    /// quarter, the check, the end. Numbers only reach the log.</summary>
    private void Progress(DownloadProgress p)
    {
        if (p.Model == "runtime-cuda") { Log.Write($"mind: cuda pack {p.State}"); return; }   // the page shows it
        var before = _lastProgress?.State;
        _lastProgress = p;
        string? line = p.State switch
        {
            "blocked" => RuntimeText.Blocked,
            "downloading" when before is not "downloading" => RuntimeText.Started,
            "downloading" when RuntimeText.Milestone(p.Done, p.Total, _saidQuarter) is { } q => Said(q),
            "paused" when before is "downloading" => RuntimeText.Paused,
            "retrying" when before is "downloading" => RuntimeText.Retrying(p.Done, p.Total),   // 0.14.1: once, then quiet
            "verifying" when before is not "verifying" => RuntimeText.Verifying,
            "mismatch" => RuntimeText.Mismatch,
            "no-disk" => RuntimeText.NoDisk(long.TryParse(p.Error, out var miss) ? miss : 0),
            "failed" => RuntimeText.Failed,
            _ => null,
        };
        if (p.State != before) Log.Write($"mind: download {p.State}{(p.Error is { } e && p.State != "no-disk" ? $" ({e})" : "")}");
        if (p.State is not "downloading") _saidQuarter = p.State is "paused" or "retrying" ? _saidQuarter : 0;
        if (line is not null) Speech.Say(Address.For(Address.You, line));
        if (p.State == "done" && before != "done")
        {
            // Verified (spec §6, Afterwards): her brain is on, and a quick bench loads it once; «هلق صرت فكّر لحالي» on ready.
            if (!Settings().MindEnabled) Save(x => x with { MindEnabled = true });
            _wantsReady = true;
            EnsureAndSend(new MindMsg { T = "bench", Model = "quick" });
        }
    }

    private string Said(int quarter)
    {
        _saidQuarter = quarter;
        return RuntimeText.Progress(quarter);
    }

    /// <summary>Spec §6: the question, with the size, before anything is fetched. «إي» starts it; «بعدين» asks again
    /// tomorrow (the caller's daily limit); «لا» never asks again (still available on «النماذج»).</summary>
    public void OfferDownloadQuestion()
    {
        // 0.16: the ask bubble takes the keyboard; with the card open (he is typing in it) it waits for the card to close.
        if (Menu.IsOpen)
        {
            void Later(bool open)
            {
                if (open) return;
                Menu.OpenChanged -= Later;
                Ui.TryEnqueue(() => Safe(OfferDownloadQuestion));
            }
            Menu.OpenChanged += Later;
            return;
        }
        var model = ModelCatalog.Find(ModelCatalog.DefaultId)!;
        Surfaces.Cards.Ask(Panel, "mind-download", Address.For(Address.You, RuntimeText.Ask(model.Bytes)), RuntimeText.Yes, RuntimeText.Later, yes =>
        {
            Log.Write($"mind: download question answered ({(yes ? "yes" : "later")})");
            if (yes) EnsureAndSend(new MindMsg { T = "download", Op = "start", Model = model.Id });
        }, yesIcon: "download");
    }

    private OnlineInfo? _online;
    private int _onlineSeq;

    /// <summary>The Mind's sealed keys → the settings flags (so the chat chip knows without asking the Mind). Flags only.</summary>
    private void OnlineKeysSeen(OnlineInfo info)
    {
        var keyed = info.Providers.Where(p => p.Key).Select(p => p.Id).ToList();
        if (!keyed.SequenceEqual(Settings().MindOnlineKeys))
        {
            Save(x => x with { MindOnlineKeys = keyed });
            Log.Write($"mind: online keys {keyed.Count}");
        }
        Chat?.Refresh();
    }

    private string Status()
    {
        var s = Settings();
        var active = ActiveModel();
        var p = _lastProgress;
        var sb = new StringBuilder();
        sb.Append($"enabled={(s.MindEnabled ? "true" : "false")} running={(_outbox is not null ? "true" : "false")} phase={_phase}");
        sb.Append($" active={active?.Id ?? "-"}");
        if (p is not null) sb.Append(System.FormattableString.Invariant($" dlModel={p.Model} dlState={p.State} dlDone={p.Done} dlTotal={p.Total} dlBps={p.Bps} dlError={p.Error ?? "-"}"));
        return sb.ToString();
    }

    private string Describe()
    {
        int server = 0;
        try { server = Process.GetProcessesByName("llama-server").Length; } catch (Exception) { }
        return Status() + $" mindPid={(_mindProc is { HasExited: false } pr ? pr.Id : 0)} starts={_starts} servers={server}";
    }
}
