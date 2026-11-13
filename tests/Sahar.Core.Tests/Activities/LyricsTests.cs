using System.Net;
using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class LyricsTests
{
    private const string Song = "[ar:Fairuz]\n[00:10.50]نسّم علينا الهوا\n[00:15.00][00:40.00]من مفرق الوادي\n\n[00:20.25] يا هوا دخل الهوا\n";

    [Fact]
    public void Lrc_ParsesTimes_Sorted()
    {
        var lines = Lrc.Parse(Song);
        Assert.Equal(4, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(10.5), lines[0].At);
        Assert.Equal("يا هوا دخل الهوا", lines[2].Line);
        Assert.Equal(TimeSpan.FromSeconds(40), lines[3].At);
    }

    [Fact]
    public void Lrc_LineAtT()
    {
        var lines = Lrc.Parse(Song);
        Assert.Equal((null, null), Lrc.At(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal((null, "نسّم علينا الهوا"), Lrc.At(lines, TimeSpan.FromSeconds(12)));
        Assert.Equal(("نسّم علينا الهوا", "من مفرق الوادي"), Lrc.At(lines, TimeSpan.FromSeconds(16)));
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Asked.Add(r.RequestUri!.ToString());
            return Task.FromResult(answer(r));
        }
    }

    private static string Dir() => Directory.CreateTempSubdirectory("lyrics").FullName;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Lyrics_NoRequestWithoutConsent(bool enabled, bool consented)
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var r = await new LyricsClient(fake, Dir()).GetAsync("t", "a", null, TimeSpan.FromMinutes(3), enabled, consented);
        Assert.Equal(LyricsStatus.Off, r.Status);
        Assert.Empty(fake.Asked);
    }

    [Fact]
    public async Task Lyrics_Found_CachedSecondTime()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"syncedLyrics\":\"[00:01.00]أهلا\"}") });
        var dir = Dir();
        var client = new LyricsClient(fake, dir);
        var r = await client.GetAsync("نسم علينا الهوا", "فيروز", "x", TimeSpan.FromSeconds(200), true, true);
        Assert.Equal(LyricsStatus.Found, r.Status);
        Assert.Equal("أهلا", r.Lines[0].Line);
        Assert.Single(fake.Asked);
        Assert.StartsWith("https://lrclib.net/api/get?", fake.Asked[0]);
        Assert.Contains("duration=200", fake.Asked[0]);
        await client.GetAsync("نسم علينا الهوا", "فيروز", "x", TimeSpan.FromSeconds(200), true, true);
        Assert.Single(fake.Asked);
        Assert.DoesNotContain(Directory.GetFiles(dir), f => f.Contains("فيروز"));   // the cache key is a hash
    }

    [Fact]
    public async Task Lyrics_NoneCached_SecondCallNoRequest()
    {
        var fake = new Fake(r => r.RequestUri!.AbsolutePath.EndsWith("/search")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new LyricsClient(fake, Dir());
        Assert.Equal(LyricsStatus.None, (await client.GetAsync("t", "a", null, TimeSpan.FromMinutes(3), true, true)).Status);
        Assert.Equal(2, fake.Asked.Count);   // get, then search
        Assert.Equal(LyricsStatus.None, (await client.GetAsync("t", "a", null, TimeSpan.FromMinutes(3), true, true)).Status);
        Assert.Equal(2, fake.Asked.Count);
    }

    [Fact]
    public async Task Lyrics_NetworkDown_FailedNotCached()
    {
        var fake = new Fake(_ => throw new HttpRequestException("down"));
        var client = new LyricsClient(fake, Dir());
        Assert.Equal(LyricsStatus.Failed, (await client.GetAsync("t", "a", null, TimeSpan.FromMinutes(3), true, true)).Status);
        await client.GetAsync("t", "a", null, TimeSpan.FromMinutes(3), true, true);
        Assert.Equal(2, fake.Asked.Count);
    }
}
