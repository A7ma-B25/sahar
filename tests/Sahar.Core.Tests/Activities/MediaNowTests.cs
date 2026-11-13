using Sahar.Core.Activities;
using Sahar.Core.Art;

namespace Sahar.Core.Tests.Activities;

public class MediaNowTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pick_MostRecentlyPlaying_VideoNever()
    {
        var sessions = new List<SessionInfo>
        {
            new("Spotify", true, false, false, T),
            new("VLC", true, true, false, T.AddMinutes(2)),
            new("msedge", true, false, true, T.AddMinutes(1)),
        };
        Assert.Equal(2, MediaNow.Pick(sessions));
        Assert.Equal(-1, MediaNow.Pick([new SessionInfo("VLC", true, true, false, T)]));
    }

    [Fact]
    public void Pick_NothingPlaying_TheLastPaused()
    {
        var sessions = new List<SessionInfo> { new("A", false, false, false, T), new("B", false, false, false, T.AddMinutes(3)) };
        Assert.Equal(1, MediaNow.Pick(sessions));
        Assert.Equal(-1, MediaNow.Pick([]));
    }

    [Theory]
    [InlineData(PausedKeep.TenMinutes, 9.9, true)]
    [InlineData(PausedKeep.TenMinutes, 10.1, false)]
    [InlineData(PausedKeep.Hour, 59, true)]
    [InlineData(PausedKeep.Hour, 61, false)]
    [InlineData(PausedKeep.Always, 6000, true)]
    public void Paused_KeptTenMinutes_HourOrAlways(PausedKeep keep, double minutes, bool kept) =>
        Assert.Equal(kept, MediaNow.KeepPaused(T, T.AddMinutes(minutes), keep));

    [Fact]
    public void Position_ExtrapolatesWhilePlaying_ClampsAtEnd()
    {
        var end = TimeSpan.FromMinutes(3);
        Assert.Equal(TimeSpan.FromSeconds(70), MediaNow.Position(TimeSpan.FromSeconds(60), T, T.AddSeconds(10), true, end));
        Assert.Equal(TimeSpan.FromSeconds(60), MediaNow.Position(TimeSpan.FromSeconds(60), T, T.AddSeconds(10), false, end));
        Assert.Equal(end, MediaNow.Position(TimeSpan.FromSeconds(170), T, T.AddSeconds(30), true, end));
        Assert.Equal(TimeSpan.Zero, MediaNow.Position(TimeSpan.FromSeconds(-5), T, T, false, end));
    }

    private static byte[] Pixels(params (Rgba Colour, int Count)[] parts)
    {
        var list = new List<byte>();
        foreach (var (c, n) in parts) for (int i = 0; i < n; i++) list.AddRange([c.B, c.G, c.R, 255]);
        return [.. list];
    }

    [Fact]
    public void Cover_VividMedian_IgnoresGreys()
    {
        var orange = Rgba.Parse("#E07A3A");
        var c = MediaNow.CoverColour(Pixels((Rgba.Parse("#808080"), 3000), (Rgba.Parse("#202020"), 500), (orange, 596)));
        Assert.Equal(orange, c);
    }

    [Fact]
    public void Cover_AllGrey_StillAColour() => Assert.Equal(Rgba.Parse("#808080"), MediaNow.CoverColour(Pixels((Rgba.Parse("#808080"), 4096))));

    [Fact]
    public void Cover_Empty_IsNull() => Assert.Null(MediaNow.CoverColourOrNull([]));

    [Theory]
    [InlineData("#E07A3A")]
    [InlineData("#101010")]
    [InlineData("#FFFFFF")]
    [InlineData("#0050FF")]
    public void Halo_LightnessClamped_Soft(string cover)
    {
        var (_, s, l) = MediaNow.Hsl(MediaNow.HaloTint(Rgba.Parse(cover)));
        Assert.InRange(l, 0.72, 0.86);
        Assert.True(s <= 0.61, $"saturation {s}");
    }
}

public class MediaAppLabelTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("chrome", "Chrome")]
    [InlineData("", "")]
    public void AppLabel(string aumid, string label) => Assert.Equal(label, MediaNow.AppLabel(aumid));
}
