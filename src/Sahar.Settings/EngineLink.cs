using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Sahar.Core.Protocol;
using Sahar.Core.Settings;

namespace Sahar.Settings;

/// <summary>
/// The engine over its "sahar" pipe (plan 0.10c Task 3): one JSON line per call, a 1 s connect timeout, and quiet
/// failure (false / null) when she is not running. Never logs what is sent (a typed city, a name).
/// </summary>
internal sealed class EngineLink(bool testMode) : IEngineLink
{
    private static readonly TimeSpan Connect = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(12);
    private static readonly UTF8Encoding Utf8 = new(false);

    public bool Running => File.Exists(@"\\.\pipe\sahar");

    public async Task<bool> ReloadAsync(bool askHello = false)
    {
        var r = await Call("settings-reload", askHello ? new() { ["hello"] = "ask" } : null);
        return r is not null;
    }

    public async Task<(bool Ok, string? Reason)> TryShortcutAsync(string value)
    {
        var r = await Call("shortcut-try", new() { ["value"] = value });
        if (r is null) return (true, null);   // she went away meanwhile: the store's own check decides
        return r.GetValueOrDefault("ok") == "true" ? (true, null) : (false, Text(r.GetValueOrDefault("reason64")));
    }

    public async Task<CityResult> FindCityAsync(string text)
    {
        var r = await Call("city-search", new() { ["text64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) });
        if (r is null) return new CityResult(null, SettingsBridge.NoEngineCity);
        if (r.GetValueOrDefault("ok") != "true") return new CityResult(null, Text(r.GetValueOrDefault("reason64")));
        double Num(string k) => double.Parse(r[k], NumberStyles.Float, CultureInfo.InvariantCulture);
        return new CityResult(new GeoPlace(Text(r.GetValueOrDefault("name64")) ?? text.Trim(), Num("lat"), Num("lon")), null);
    }

    public async Task<bool> ClearClipboardAsync() => (await Call("clipboard-clear", null))?.GetValueOrDefault("ok") == "true";

    /// <summary>She empties her data folder and quits; then she is started again (fresh, she asks your name).</summary>
    public async Task<EraseOutcome?> EraseAllAsync()
    {
        var engines = Process.GetProcessesByName("Sahar.Engine");
        var r = await Call("erase-all", null);
        if (r is null) return null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        foreach (var p in engines)
        {
            try
            {
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero) await p.WaitForExitAsync(new CancellationTokenSource(left).Token);
            }
            catch (Exception) { /* gone already, or not ours */ }
        }
        var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Sahar.Engine.exe"));
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, Arguments = testMode ? "--test-mode --capturable" : "" });
            SettingsLog.Write("erase: done, she starts again");
        }
        catch (Exception e)
        {
            SettingsLog.Write($"erase: she could not start again ({e.GetType().Name})");
        }
        return new EraseOutcome([], true);
    }

    private static string? Text(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }

    /// <summary>One command; its key=value reply, or null when she is not there or did not answer.</summary>
    private static async Task<Dictionary<string, string>?> Call(string name, Dictionary<string, string>? args)
    {
        if (!File.Exists(@"\\.\pipe\sahar")) return null;
        try
        {
            using var cts = new CancellationTokenSource(Answer);
            await using var client = new NamedPipeClientStream(".", "sahar", PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(Connect, cts.Token);
            using var reader = new StreamReader(client, Utf8);
            await using var writer = new StreamWriter(client, Utf8) { NewLine = "\n", AutoFlush = true };
            await writer.WriteLineAsync(ProtocolJson.Write(new PipeRequest("cmd", name, args)).AsMemory(), cts.Token);
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) return null;
            var reply = ProtocolJson.ParseReply(line);
            if (!reply.Ok) { SettingsLog.Write($"engine: {name} refused"); return null; }
            var kv = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (reply.Error ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) kv[pair[..eq]] = pair[(eq + 1)..];
            }
            return kv;
        }
        catch (Exception e)
        {
            SettingsLog.Write($"engine: {name} failed ({e.GetType().Name})");
            return null;
        }
    }
}
