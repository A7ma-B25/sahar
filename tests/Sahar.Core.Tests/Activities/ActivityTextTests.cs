using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class ActivityTextTests
{
    [Theory]
    [InlineData(4, "٤ د")]
    [InlineData(50, "٥٠ د")]
    [InlineData(95, "١ س ٣٥ د")]
    [InlineData(320, "٥ س ٢٠ د")]
    [InlineData(120, "٢ س")]
    [InlineData(0, "أقل من دقيقة")]
    public void Duration(int minutes, string text) => Assert.Equal(text, ActivityText.Duration(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(1, "معي شغلة")]
    [InlineData(2, "معي شغلتين")]
    [InlineData(4, "معي ٤ شغلات")]
    [InlineData(11, "معي ١١ شغلة")]
    public void ShelfCount(int n, string text) => Assert.Equal(text, ActivityText.Shelf(n));

    [Theory]
    [InlineData(ClaudeStep.Editing, "Claude عم يعدّل")]
    [InlineData(ClaudeStep.Reading, "Claude عم يقرا")]
    [InlineData(ClaudeStep.Running, "Claude عم يشغّل أمر")]
    [InlineData(ClaudeStep.Searching, "Claude عم يدوّر")]
    [InlineData(ClaudeStep.Web, "Claude عم يدوّر عالنت")]
    [InlineData(ClaudeStep.None, "Claude عم يشتغل")]
    public void ClaudeStepText(ClaudeStep step, string text) => Assert.Equal(text, ActivityText.ClaudeStep(step));

    [Theory]
    [InlineData(WeatherAlert.Rain, "مطر بعد ساعة")]
    [InlineData(WeatherAlert.Storm, "عاصفة جاية")]
    [InlineData(WeatherAlert.Wind, "ريح قوية جاية")]
    [InlineData(WeatherAlert.Heat, "حر قوي اليوم")]
    [InlineData(WeatherAlert.Frost, "صقيع الليلة")]
    [InlineData(WeatherAlert.Air, "الهوا مو نضيف")]
    public void AlertTag(WeatherAlert a, string text) => Assert.Equal(text, ActivityText.AlertTag(a));

    [Fact]
    public void AlertBubble_Rain() => Assert.Equal("رح تشتي بعد ساعة تقريباً. خود شمسية!", ActivityText.AlertBubble(WeatherAlert.Rain));

    [Fact]
    public void Away_Bubble() => Assert.Equal("أهلين! صار ٤ شغلات وإنت غايب.", ActivityText.AwayBubble(4));

    [Fact]
    public void Percent_ArabicDigits() => Assert.Equal("٤٢٪", ActivityText.Percent(42));

    [Fact]
    public void ForCall_HidesTitlesAndNames()
    {
        var music = new Activity("music", ActivityKind.Music, "Secret Song", "Artist");
        Assert.Equal(("الموسيقى", (string?)null), (ActivityText.ForCall(music).Text, ActivityText.ForCall(music).Meta));
        var claude = new Activity("claude:1", ActivityKind.ClaudeWorking, "Claude عم يعدّل", "٤ د");
        Assert.Equal("Claude عم يشتغل", ActivityText.ForCall(claude).Text);
        var ask = new Activity("ask:1", ActivityKind.ClaudeAsk, "Claude ناطر جوابك", Asking: true);
        Assert.Equal(ask, ActivityText.ForCall(ask));
    }

    [Fact]
    public void Text_NoLatinDigits()
    {
        var f = new Forecast(24.4, 0, [new HourCast(DateTimeOffset.Now, -3, 1, 0, 65)], 38, 12, 81, DateTimeOffset.Now);
        string[] all =
        [
            ActivityText.Duration(TimeSpan.FromMinutes(321)), ActivityText.Shelf(17), ActivityText.Percent(99.6), ActivityText.WeatherToday(f),
            ActivityText.AwayBubble(12), ActivityText.More(3), ActivityText.Degrees(-7), ActivityText.AwayRow(ActivityKind.ClaudeDone, 3),
            .. Enum.GetValues<WeatherAlert>().Select(a => ActivityText.AlertDetail(a, f)),
        ];
        foreach (var t in all) Assert.False(t.Any(char.IsAsciiDigit), t);
    }
}
