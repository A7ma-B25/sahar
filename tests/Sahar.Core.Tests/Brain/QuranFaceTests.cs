using Sahar.Core.Brain;

namespace Sahar.Core.Tests.Brain;

public class QuranFaceTests
{
    [Theory]
    [InlineData("idle")]
    [InlineData("music")]   // music is paused for it
    [InlineData("sleep")]   // listening without touching the PC is not being away: she never dozes off during the Quran
    [InlineData("back")]
    public void WhileTheQuranPlays_RestingFacesBecomeTheQuranFace(string brainKey) =>
        Assert.Equal("quran", QuranFace.Over(brainKey, quranPlaying: true));

    [Theory]
    [InlineData("reminder")]
    [InlineData("timer-done")]
    [InlineData("claude-permission")]
    [InlineData("battery-low")]
    public void WhileTheQuranPlays_EventsStillShow(string brainKey) =>
        Assert.Equal(brainKey, QuranFace.Over(brainKey, quranPlaying: true));

    [Theory]
    [InlineData("idle")]
    [InlineData("sleep")]
    public void WithoutTheQuran_NothingChanges(string brainKey) =>
        Assert.Equal(brainKey, QuranFace.Over(brainKey, quranPlaying: false));
}
