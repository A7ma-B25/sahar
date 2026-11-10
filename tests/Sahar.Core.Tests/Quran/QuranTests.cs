using Sahar.Core.Menu;
using Sahar.Core.Settings;
using Xunit;
using Q = Sahar.Core.Quran.Quran;

namespace Sahar.Core.Tests.Quran;

public class QuranTests
{
    [Fact]
    public void Quran_114Surahs()
    {
        Assert.Equal(114, Q.Surahs.Count);
        Assert.Equal(114, Q.Surahs.Distinct().Count());
        Assert.Equal("الفاتحة", Q.SurahName(1));
        Assert.Equal("البقرة", Q.SurahName(2));
        Assert.Equal("يس", Q.SurahName(36));
        Assert.Equal("الملك", Q.SurahName(67));
        Assert.Equal("الإخلاص", Q.SurahName(112));
        Assert.Equal("الناس", Q.SurahName(114));
        Assert.Throws<ArgumentOutOfRangeException>(() => Q.SurahName(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Q.SurahName(115));
    }

    [Fact]
    public void Quran_SixReciters()
    {
        Assert.Equal(["ar.alafasy", "ar.abdulbasitmurattal", "ar.husary", "ar.minshawi", "ar.mahermuaiqly", "ar.abdurrahmaansudais"],
            Q.Reciters.Select(r => r.Id));
        Assert.Equal("مشاري العفاسي", Q.ReciterName("ar.alafasy"));
        Assert.Equal("مشاري العفاسي", Q.ReciterName("unknown"));   // a bad setting falls back to the first
    }

    [Fact]
    public void Quran_Url()
    {
        Assert.Equal("https://cdn.islamic.network/quran/audio-surah/128/ar.alafasy/36.mp3", Q.Url("ar.alafasy", 36).ToString());
        Assert.Equal("file:///C:/t/q/ar.husary/1.mp3", Q.Url("ar.husary", 1, "file:///C:/t/q/").AbsoluteUri);
        Assert.Equal("http://127.0.0.1:9/ar.husary/2.mp3", Q.Url("ar.husary", 2, "http://127.0.0.1:9").ToString());
    }

    [Fact]
    public void Menu_Listen_WhileTheQuranPlays_OffersStop() =>
        Assert.Equal(["quran-stop", "music"],
            MenuCatalog.Drill("listen", new MenuState(false, false, true))!.Options.Select(i => i.Id));

    [Fact]
    public void Settings_QuranDefaults()
    {
        var s = new SaharSettings();
        Assert.Equal("ar.alafasy", s.QuranReciter);
        Assert.Null(s.QuranLastSurah);
    }
}
