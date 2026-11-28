using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;

namespace Sahar.Mind.Tools;

/// <summary>
/// 0.19 spec §4–§8: her tools in the Mind. The config (servers, their tools, his grants, the approved hashes) is sealed
/// in her box (<c>tools</c>), each server's secret apart (<c>tools-secrets</c>, never sent anywhere but to that
/// server). One MCP link per server, opened on first use and closed with the Mind. Before every call the server's list is
/// read again and merged (rug-pull guard): a tool that changed is not run. Calls 30 s, media 90 s. The page's ops come
/// as <c>tools</c> messages and always end with a <c>tools-status</c>. Logs: op codes, server index, tool name, ms.
/// </summary>
internal sealed partial class McpHost(Func<SaharSettings> settings, Action<MindMsg> send, Func<string?> groqKey) : IMindTools, IAsyncDisposable
{
    public static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(30), ConnectLimit = TimeSpan.FromSeconds(20);
    private const string ConfigName = "tools", SecretsName = "tools-secrets";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (McpLink Link, JobProcess? Proc)> _links = [];
    private readonly Dictionary<string, string> _states = [];
    private readonly MediaHost _media = new();
    private readonly SocketsHttpHandler _remote = new() { UseProxy = true, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
    private readonly SocketsHttpHandler _local = new() { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) };
    private ISecretBox? _box;
    private McpToolsFile? _file;
    private string? _imageTest, _voiceTest, _result, _resultServer;

    private ISecretBox Box => _box ??= MindParts.SecretBox(MindPaths.Personal);
    private ToolsLedger Ledger => new(Path.Combine(MindPaths.Personal, "tools-ledger.json"));

    private McpToolsFile File
    {
        get
        {
            if (_file is { } f) return f;
            try { _file = McpCatalog.Parse(Box.Read(ConfigName) is { } b ? Encoding.UTF8.GetString(b) : null); }
            catch (Exception e) { MindLog.Write($"tools: config unreadable ({e.GetType().Name})"); _file = McpCatalog.Empty; }
            return _file;
        }
    }

    private void Save(McpToolsFile f)
    {
        _file = f;
        try { Box.Write(ConfigName, Encoding.UTF8.GetBytes(McpCatalog.Serialize(f))); }
        catch (Exception e) { MindLog.Write($"tools: config not kept ({e.GetType().Name})"); }
    }

    private Dictionary<string, string> Secrets()
    {
        try { return Box.Read(SecretsName) is { } b ? JsonSerializer.Deserialize(b, ToolsJson.Default.DictionaryStringString) ?? [] : []; }
        catch (Exception) { return []; }
    }

    private void SaveSecrets(Dictionary<string, string> s)
    {
        try { Box.Write(SecretsName, JsonSerializer.SerializeToUtf8Bytes(s, ToolsJson.Default.DictionaryStringString)); }
        catch (Exception e) { MindLog.Write($"tools: secret not kept ({e.GetType().Name})"); }
    }

    /// <summary>«انسي كل شي» or a store change: everything is read again; open links close.</summary>
    public async Task ResetAsync() { _box = null; _file = null; await CloseAllAsync(); }

    /// <summary>What the decision may propose now. «محلي فقط»: only servers on this laptop (an .exe or a loopback link).</summary>
    public IReadOnlyList<ToolSpec> Announced
    {
        get
        {
            var s = settings();
            var f = File;
            if (s.MindLocalOnly) f = f with { Servers = f.Servers.Where(Local).ToList(), Media = f.Media with { ImageChecked = false, VoiceChecked = false } };
            return McpCatalog.Announced(f, groqKey() is not null);
        }
    }

    private static bool Local(McpServer s) => s.Kind == "exe" || (Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.IsLoopback);

    public void SendStatus() =>
        send(new MindMsg { T = "tools-status", Toolbox = McpCatalog.Info(File, groqKey() is not null, _states, _imageTest, _voiceTest, _result, _resultServer) with { Announced = Announced } });

    private int Index(string serverId) => File.Servers.ToList().FindIndex(s => s.Id == serverId);

    // ---- the page's ops ----

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")] private static partial Regex HeaderName();
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")] private static partial Regex EnvName();

    /// <summary>engine → mind <c>tools</c>: Op, Model = server id, Tool = tool name, Data = add JSON (no secret), Text = the
    /// secret or the op's value. Never logged except the op and its code.</summary>
    public async Task HandleAsync(MindMsg m, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _result = null;
            _resultServer = m.Model;
            var now = DateTimeOffset.Now;
            var f = File;
            switch (m.Op)
            {
                case "status": break;
                case "add": await AddAsync(m, ct); break;
                case "remove" when f.Servers.FirstOrDefault(s => s.Id == m.Model) is { } gone:
                    await CloseAsync(gone.Id);
                    Save(f with { Servers = f.Servers.Where(s => s.Id != gone.Id).ToList() });
                    var secrets = Secrets();
                    if (secrets.Remove(gone.Id)) SaveSecrets(secrets);
                    _states.Remove(gone.Id);
                    Ledger.Append(new LedgerEntry(now, "removed", gone.Slug, null, null));
                    _result = "removed";
                    break;
                case "enable" or "disable" when m.Model is { } sid && m.Tool is { } tool:
                    if (McpCatalog.Enable(f, sid, tool, m.Op == "enable", now) is { } on)
                    {
                        Save(on);
                        var t = on.Servers.First(s => s.Id == sid).Tools.First(x => x.Name == tool);
                        Ledger.Append(new LedgerEntry(now, m.Op == "enable" ? "approved" : "disabled", on.Servers.First(s => s.Id == sid).Slug, tool, t.Hash));
                        _result = m.Op == "enable" ? "enabled" : "disabled";
                    }
                    else _result = f.Servers.Sum(s => s.Tools.Count(t => t.Enabled)) >= McpCatalog.MaxEnabled ? "limit" : "unfit";
                    break;
                case "grant" when m.Model is { } sid && m.Tool is { } tool && Enum.TryParse<ToolGrant>(m.Text, true, out var g):
                    if (McpCatalog.SetGrant(f, sid, tool, g) is { } granted) { Save(granted); _result = "granted"; }
                    break;
                case "sensitive" when m.Model is { } sid && m.Tool is { } tool:
                    if (McpCatalog.SetSensitive(f, sid, tool, m.Text == "true") is { } marked) { Save(marked); _result = "marked"; }
                    break;
                case "approve" when f.Servers.FirstOrDefault(s => s.Id == m.Model) is { Kind: "exe", Exe: { } exe } paused:
                    // He approves the program as it is now (a changed .exe stays paused until then).
                    if (Sha(exe) is not { } sha) { _result = "missing"; break; }
                    Save(f with { Servers = f.Servers.Select(s => s.Id == paused.Id ? s with { ExeSha256 = sha, Paused = false } : s).ToList() });
                    Ledger.Append(new LedgerEntry(now, "exe-approved", paused.Slug, null, sha));
                    _states.Remove(paused.Id);
                    await RefreshAsync(paused.Id, ct);
                    break;
                case "refresh" when m.Model is { } sid:
                    await RefreshAsync(sid, ct);
                    break;
                case "media-grant" when m.Model is "image" or "voice":
                    var grant = m.Text == "always" ? ToolGrant.Always : ToolGrant.Ask;
                    Save(f with { Media = m.Model == "image" ? f.Media with { ImageGrant = grant } : f.Media with { VoiceGrant = grant } });
                    _result = "granted";
                    break;
                case "check-image":
                    _imageTest = settings().MindLocalOnly ? "local-only" : await _media.CheckImageAsync(ct);
                    if (_imageTest == "ok" && !File.Media.ImageChecked) Save(File with { Media = File.Media with { ImageChecked = true } });
                    break;
                case "check-voice":
                    _voiceTest = settings().MindLocalOnly ? "local-only" : await _media.CheckVoiceAsync(groqKey(), ct);
                    if (_voiceTest == "ok" && !File.Media.VoiceChecked) Save(File with { Media = File.Media with { VoiceChecked = true } });
                    break;
                default: _result = "bad-op"; break;
            }
            MindLog.Write($"tools: {m.Op} {_result ?? _imageTest ?? "-"}");
        }
        finally
        {
            _gate.Release();
            SendStatus();
        }
    }

    private async Task AddAsync(MindMsg m, CancellationToken ct)
    {
        JsonElement a;
        try { using var d = JsonDocument.Parse(m.Data ?? "{}"); a = d.RootElement.Clone(); }
        catch (JsonException) { _result = "bad-request"; return; }
        string? S(string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
        var f = File;
        var name = S("name");
        var kind = S("kind");
        if (name is not { Length: > 0 and <= 40 } || kind is not ("url" or "exe")) { _result = "bad-name"; return; }
        string? url = null, exe = null, sha = null;
        if (kind == "url")
        {
            url = S("url");
            if (url is null || !McpUrl(url)) { _result = "bad-url"; return; }
        }
        else
        {
            exe = S("exe");
            if (exe is null || !Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(exe)) { _result = "bad-exe"; return; }
            if ((sha = Sha(exe)) is null) { _result = "bad-exe"; return; }
        }
        var secretKind = S("secretKind") is "header" or "env" ? S("secretKind") : null;
        var secretName = S("secretName");
        var secret = m.Text?.Trim();
        if (secretKind is not null)
        {
            bool okName = secretName is not null && (secretKind == "header" ? HeaderName().IsMatch(secretName) : EnvName().IsMatch(secretName));
            if (!okName || secret is not { Length: > 0 and <= 4096 } || secret.Any(char.IsControl)) { _result = "bad-secret"; return; }
        }
        if (f.Servers.Count >= 20) { _result = "too-many"; return; }
        var id = "s" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var slug = McpSchema.Slug(name, f.Servers.Select(s => s.Slug).ToList());
        var server = new McpServer(id, name, slug, kind, url, exe, kind == "exe" ? McpCatalog.SplitArgs(S("args")) : [], sha, secretKind,
            secretKind is null ? null : secretName, false, []);
        Save(f with { Servers = [.. f.Servers, server] });
        if (secretKind is not null)
        {
            var secrets = Secrets();
            secrets[id] = secret!;
            SaveSecrets(secrets);
        }
        Ledger.Append(new LedgerEntry(DateTimeOffset.Now, "added", slug, null, sha));
        _resultServer = id;
        await RefreshAsync(id, ct);
        if (_result == "listed") _result = "added";
    }

    /// <summary>https anywhere, or http to this laptop only.</summary>
    public static bool McpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)) && u.UserInfo.Length == 0;

    private static string? Sha(string exe)
    {
        try { using var s = System.IO.File.OpenRead(exe); return Convert.ToHexStringLower(SHA256.HashData(s)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Connects (if needed), lists and merges; a tool that changed is quarantined (ledgered once).</summary>
    private async Task RefreshAsync(string serverId, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(ConnectLimit);
        try
        {
            var link = await LinkAsync(serverId, limit.Token);
            if (link is null) { _result = _states.GetValueOrDefault(serverId, "error"); return; }
            Merge(serverId, await link.ListAsync(limit.Token));
            _result = "listed";
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _states[serverId] = Code(e);
            _result = _states[serverId];
            MindLog.Write($"tools: server {Index(serverId)} {_result} ({e.GetType().Name})");
            await CloseAsync(serverId);
        }
    }

    private void Merge(string serverId, IReadOnlyList<ListedTool> listed)
    {
        var f = File;
        if (f.Servers.FirstOrDefault(s => s.Id == serverId) is not { } s) return;
        var merged = McpCatalog.Merge(s, listed);
        foreach (var t in merged.Tools)
            if (McpCatalog.StateOf(t, s.Slug) == McpCatalog.Changed && s.Tools.FirstOrDefault(o => o.Name == t.Name) is { } before && before.Hash != t.Hash)
                Ledger.Append(new LedgerEntry(DateTimeOffset.Now, "changed", s.Slug, t.Name, t.Hash));
        Save(f with { Servers = f.Servers.Select(x => x.Id == serverId ? merged : x).ToList() });
    }

    private static string Code(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException)
        {
            if (x is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }) return "oauth";
            if (x.Message.Contains("401", StringComparison.Ordinal) && x.Message.Contains("nauthorized", StringComparison.Ordinal)) return "oauth";
            if (x is OperationCanceledException or TimeoutException) return "timeout";
            if (x is HttpRequestException) return "offline";
        }
        return "error";
    }

    /// <summary>The open link, or a new one (an .exe: SHA-256 checked first; changed → paused until he approves).</summary>
    private async Task<McpLink?> LinkAsync(string serverId, CancellationToken ct)
    {
        if (_links.TryGetValue(serverId, out var open))
        {
            if (open.Proc is not { HasExited: true }) return open.Link;
            await CloseAsync(serverId);
        }
        var f = File;
        if (f.Servers.FirstOrDefault(s => s.Id == serverId) is not { } s) return null;
        if (s.Paused) { _states[serverId] = "exe-changed"; return null; }
        var secret = s.SecretKind is null ? null : Secrets().GetValueOrDefault(serverId);
        var sw = Stopwatch.StartNew();
        McpLink link;
        JobProcess? proc = null;
        if (s.Kind == "exe")
        {
            if (s.Exe is null || Sha(s.Exe) is not { } sha) { _states[serverId] = "missing"; return null; }
            if (sha != s.ExeSha256)
            {
                Save(f with { Servers = f.Servers.Select(x => x.Id == serverId ? x with { Paused = true } : x).ToList() });
                Ledger.Append(new LedgerEntry(DateTimeOffset.Now, "paused", s.Slug, null, sha));
                _states[serverId] = "exe-changed";
                MindLog.Write($"tools: server {Index(serverId)} exe-changed (paused)");
                return null;
            }
            var env = new Dictionary<string, string>();
            if (s.SecretKind == "env" && s.SecretName is { } en && secret is not null) env[en] = secret;
            proc = JobProcess.Start(s.Exe, s.Args, env);
            try { link = await McpLink.StreamAsync(proc.Input, proc.Output, ct); }
            catch { proc.Dispose(); throw; }
        }
        else
        {
            var u = new Uri(s.Url!);
            var headers = new Dictionary<string, string>();
            if (s.SecretKind == "header" && s.SecretName is { } hn && secret is not null) headers[hn] = secret;
            link = await McpLink.HttpAsync(u, headers, u.IsLoopback ? _local : _remote, ct);
        }
        _links[serverId] = (link, proc);
        _states[serverId] = "ok";
        MindLog.Write($"tools: server {Index(serverId)} connected {sw.ElapsedMilliseconds}ms");
        return link;
    }

    private async Task CloseAsync(string serverId)
    {
        if (!_links.Remove(serverId, out var c)) return;
        try { await c.Link.DisposeAsync(); } catch (Exception) { }
        c.Proc?.Dispose();
    }

    private async Task CloseAllAsync()
    {
        foreach (var id in _links.Keys.ToList()) await CloseAsync(id);
    }

    public async ValueTask DisposeAsync() => await CloseAllAsync();

    // ---- a call in a turn (after the engine's yes) ----

    public async Task<MindToolResult> RunAsync(ToolCall call, CancellationToken ct)
    {
        if (call.Tool == MediaTools.Image.Name)
            return await _media.ImageAsync(Str(call, "prompt_en") ?? "", save: true, ct);
        if (call.Tool == MediaTools.Voice.Name)
            return await _media.VoiceAsync(Str(call, "text") ?? "", groqKey(), save: true, ct);
        if (Announced.FirstOrDefault(t => t.Name == call.Tool) is not { ServerId: { } sid } spec)
            return new(false, "none", ToolTemplates.ToolChanged, Error: "changed");
        var tool = ToolTemplates.McpParts(call.Tool).Tool;
        var sw = Stopwatch.StartNew();
        await _gate.WaitAsync(ct);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(CallLimit);
            try
            {
                if (await LinkAsync(sid, limit.Token) is not { } link) return Log(sid, tool, "unavailable", sw, new(false, "none", ToolTemplates.ToolFailed, Error: "unavailable"));
                // The rug-pull guard: what he approved must still be what the server says, right now.
                Merge(sid, await link.ListAsync(limit.Token));
                if (!Announced.Any(t => t.Name == call.Tool && t.DescriptorHash == spec.DescriptorHash))
                {
                    SendStatus();
                    return Log(sid, tool, "changed", sw, new(false, "none", ToolTemplates.ToolChanged, Error: "changed"));
                }
                var o = await link.CallAsync(tool, call.Args, limit.Token);
                var text = ToolOutput.Sanitize(o.Text);
                if (o.IsError) return Log(sid, tool, "tool-error", sw, new(false, "text", ToolTemplates.ToolFailed, Text: text.Length > 0 ? text : null, Error: "tool-error"));
                if (text.Trim().Length == 0 && o.Images is [var (data, mime), ..])
                {
                    var name = SaveImage(data, mime == "image/png" ? "png" : "jpg");
                    return Log(sid, tool, "ok", sw, name is null ? new(false, "none", ToolTemplates.ToolFailed, Error: "failed") : new(true, "image", ToolTemplates.MindDone(spec), File: name));
                }
                return Log(sid, tool, "ok", sw, new(true, "text", ToolTemplates.MindDone(spec), Text: text.Length > 0 ? text : "—"));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await CloseAsync(sid);   // a server that does not answer in time is stopped (its job dies with it)
                return Log(sid, tool, "timeout", sw, new(false, "none", ToolTemplates.ToolTimeout, Error: "timeout"));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await CloseAsync(sid);
                _states[sid] = Code(e);
                return Log(sid, tool, _states[sid], sw, new(false, "none", ToolTemplates.ToolFailed, Error: "failed"));
            }
        }
        finally { _gate.Release(); }
    }

    private MindToolResult Log(string sid, string tool, string code, Stopwatch sw, MindToolResult r)
    {
        MindLog.Write($"tool: server {Index(sid)} {tool} {code} {sw.ElapsedMilliseconds}ms");
        return r;
    }

    private static string? Str(ToolCall c, string k) =>
        c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? SaveImage(byte[] data, string ext)
    {
        if (data.Length is 0 or > 20 << 20) return null;
        try
        {
            Directory.CreateDirectory(MediaHost.Folder);
            var name = Path.GetFileNameWithoutExtension(MediaTools.FileName("image", DateTimeOffset.Now)) + "-" + Random.Shared.Next(2, 99).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + ext;
            System.IO.File.WriteAllBytes(Path.Combine(MediaHost.Folder, name), data);
            return name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
