using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Sahar.Core.Mind.Download;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Download;

/// <summary>Spec §6 + owner answers: reachability first, never without his yes, pause/resume across restarts (Range +
/// ETag), SHA-256 verify, allow-listed hosts on every hop, a disk check.</summary>
public sealed class DownloaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-dl-").FullName;
    private static readonly Uri Hf = new("https://huggingface.co/o/r/resolve/0123/m.gguf");
    private readonly byte[] _body = RandomNumberGenerator.GetBytes(300_000);
    private DateTimeOffset _now = new(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
    private DownloadPlan Plan(byte[]? body = null, bool approved = true) =>
        new("m", Hf, Path.Combine(_dir, "m.gguf"), (body ?? _body).Length, Sha(body ?? _body), approved ? _now : null);
    private Downloader Dl(FakeHub hub, long free = long.MaxValue) => new(hub, () => _now, _ => free);

    [Fact]
    public async Task Run_FollowsHfRedirect_Verifies_Renames()
    {
        var hub = new FakeHub(_body);
        var r = await Dl(hub).RunAsync(Plan(), null, default);
        Assert.Equal("done", r.State);
        Assert.Equal(_body, File.ReadAllBytes(Path.Combine(_dir, "m.gguf")));
        Assert.Empty(Directory.GetFiles(_dir, "*.part*"));
        Assert.Contains(hub.Hosts, h => h == "cas-bridge.xethub.hf.co");
    }

    [Fact]
    public async Task Never_StartsWithoutHisYes()
    {
        var hub = new FakeHub(_body);
        var r = await Dl(hub).RunAsync(Plan(approved: false), null, default);
        Assert.Equal("not-approved", r.State);
        Assert.Equal(0, hub.Requests);
    }

    [Fact]
    public async Task Resume_AcrossRestart_KeepsOffset()
    {
        var hub = new FakeHub(_body) { CutAfter = 120_000 };
        var first = await Dl(hub).RunAsync(Plan(), null, default);
        Assert.Equal("paused", first.State);
        Assert.True(first.Done >= 120_000);
        hub.CutAfter = null;
        var again = await Dl(hub).RunAsync(Plan(), null, default);   // a new Downloader, as after a restart
        Assert.Equal("done", again.State);
        Assert.StartsWith("bytes=", hub.LastRange);
        Assert.True(long.Parse(hub.LastRange!["bytes=".Length..].TrimEnd('-')) >= 120_000);
        Assert.Equal(_body, File.ReadAllBytes(Path.Combine(_dir, "m.gguf")));
    }

    [Fact]
    public async Task Resume_EtagChanged_StartsOver()
    {
        var hub = new FakeHub(_body) { CutAfter = 100_000 };
        await Dl(hub).RunAsync(Plan(), null, default);
        hub.CutAfter = null;
        hub.ETag = "\"other\"";   // the file changed on the server: If-Range fails → 200 with the whole body
        var r = await Dl(hub).RunAsync(Plan(), null, default);
        Assert.Equal("done", r.State);
        Assert.Equal(_body, File.ReadAllBytes(Path.Combine(_dir, "m.gguf")));
    }

    [Fact]
    public async Task Pause_KeepsPart_AndState()
    {
        var hub = new FakeHub(_body) { Slow = true };
        using var cts = new CancellationTokenSource();
        var progress = new Sync(p => { if (p.Done > 50_000) cts.Cancel(); });   // synchronous: never after the test
        var r = await Dl(hub).RunAsync(Plan(), progress, cts.Token);
        Assert.Equal("paused", r.State);
        Assert.True(File.Exists(Path.Combine(_dir, "m.gguf.part")));
        var state = Downloader.ReadState(Path.Combine(_dir, "m.gguf"))!;
        Assert.Equal("paused", state.State);
        Assert.Equal(new FileInfo(Path.Combine(_dir, "m.gguf.part")).Length, state.Offset);
    }

    [Fact]
    public async Task Verify_Mismatch_Deletes()
    {
        var hub = new FakeHub(_body);
        var wrong = Plan() with { Sha256 = new string('0', 64) };
        var r = await Dl(hub).RunAsync(wrong, null, default);
        Assert.Equal("mismatch", r.State);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Redirect_ToOtherHost_Refused()
    {
        var hub = new FakeHub(_body) { RedirectHost = "evil.example" };
        var r = await Dl(hub).RunAsync(Plan(), null, default);
        Assert.Equal("failed", r.State);
        Assert.Equal("host", r.Error);
        Assert.DoesNotContain("evil.example", hub.Hosts);
    }

    [Fact]
    public async Task NoDisk_SaysHowMuch()
    {
        var r = await Dl(new FakeHub(_body), free: 1000).RunAsync(Plan(), null, default);
        Assert.Equal("no-disk", r.State);
        Assert.Equal(_body.Length + DiskCheck.Spare - 1000, r.Missing);
    }

    [Fact]
    public async Task Check_Reachable()
    {
        Assert.True((await Dl(new FakeHub(_body)).CheckAsync(Hf, default)).Ok);
    }

    [Fact]
    public async Task Check_TimeoutIsBlocked()
    {
        var reach = await new Downloader(new FakeHub(_body) { Hang = true }, () => _now, _ => long.MaxValue) { CheckTimeout = TimeSpan.FromMilliseconds(200) }
            .CheckAsync(Hf, default);
        Assert.False(reach.Ok);
        Assert.Equal("timeout", reach.Why);
    }

    [Fact]
    public async Task Check_Http403IsBlocked()
    {
        var reach = await Dl(new FakeHub(_body) { Status = HttpStatusCode.Forbidden }).CheckAsync(Hf, default);
        Assert.Equal((false, "http-403"), (reach.Ok, reach.Why));
    }

    [Fact]
    public async Task Check_DnsFailureIsBlocked()
    {
        var reach = await Dl(new FakeHub(_body) { Throw = new HttpRequestException("x", new System.Net.Sockets.SocketException(11001)) }).CheckAsync(Hf, default);
        Assert.Equal((false, "dns"), (reach.Ok, reach.Why));
    }

    [Theory]
    [InlineData("https://huggingface.co/x", true)]
    [InlineData("https://cdn-lfs-us-1.huggingface.co/x", true)]
    [InlineData("https://cas-bridge.xethub.hf.co/x", true)]
    [InlineData("https://github.com/ggml-org/llama.cpp/releases/download/b12207/x.zip", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("http://huggingface.co/x", false)]
    [InlineData("https://huggingface.co.evil.example/x", false)]
    [InlineData("https://evilhuggingface.co/x", false)]
    public void Rules_AllowList(string url, bool ok) => Assert.Equal(ok, DownloadRules.Allowed(new Uri(url)));

    [Fact]
    public void Disk_SizePlusOneGb() => Assert.Equal(0, DiskCheck.Missing(free: 6L << 30, size: 5L << 30));

    /// <summary>huggingface.co answers 302 to the CDN; the CDN serves Range with an ETag.</summary>
    private sealed class FakeHub(byte[] body) : HttpMessageHandler
    {
        public string ETag = "\"v1\"";
        public long? CutAfter;
        public bool Slow, Hang;
        public string? RedirectHost;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Exception? Throw;
        public int Requests;
        public string? LastRange;
        public List<string> Hosts = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Requests++;
            Hosts.Add(req.RequestUri!.Host);
            if (Throw is not null) throw Throw;
            if (Hang) await Task.Delay(Timeout.Infinite, ct);
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);
            if (req.RequestUri.Host == "huggingface.co")
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri($"https://{RedirectHost ?? "cas-bridge.xethub.hf.co"}/blob?sig=1");
                return r;
            }
            long from = 0;
            if (req.Headers.Range?.Ranges.FirstOrDefault() is { From: { } f })
            {
                LastRange = $"bytes={f}-";
                bool ifRangeOk = req.Headers.IfRange?.EntityTag?.Tag is not { } tag || tag == ETag;
                from = ifRangeOk ? f : 0;
            }
            var slice = body[(int)from..];
            if (CutAfter is { } cut && cut > from) slice = slice[..(int)Math.Min(slice.Length, cut - from)];
            var resp = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
            Stream content = CutAfter is not null ? new BreakingStream(slice) : Slow ? new SlowStream(slice) : new MemoryStream(slice);
            resp.Content = new StreamContent(content);
            resp.Content.Headers.ContentLength = body.Length - from;
            resp.Headers.ETag = new EntityTagHeaderValue(ETag);
            resp.Headers.AcceptRanges.Add("bytes");
            if (req.Method == HttpMethod.Head) resp.Content = new ByteArrayContent([]);
            return resp;
        }
    }

    private sealed class Sync(Action<DownloadProgress> on) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => on(value);
    }

    /// <summary>Gives its bytes then fails like a reset connection.</summary>
    private sealed class BreakingStream(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) { int n = base.Read(buffer, offset, count); return n > 0 ? n : throw new IOException("reset"); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = await base.ReadAsync(buffer, ct);
            return n > 0 ? n : throw new IOException("reset");
        }
    }

    private sealed class SlowStream(byte[] data) : MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(5, ct);
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 8192)], ct);
        }
    }
}
