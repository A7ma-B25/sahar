using Sahar.Core.Senses;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

public class AddressTests
{
    [Theory]
    [InlineData("اكتب ملاحظة…", "اكتبي ملاحظة…")]
    [InlineData("📖 اختار سورة", "📖 اختاري سورة")]
    [InlineData("اختار القارئ", "اختاري القارئ")]
    [InlineData("ما في نت هلق، جرّب بعدين — جرّب مدينة تانية", "ما في نت هلق، جرّبي بعدين — جرّبي مدينة تانية")]
    [InlineData("المدينة: دمشق — غيّر", "المدينة: دمشق — غيّري")]
    [InlineData("لسا ما نسخت شي", "لسا ما نسختي شي")]
    [InlineData("📋 آخر شي نسخته", "📋 آخر شي نسختيه")]
    [InlineData("امسح الكل", "امسحي الكل")]
    [InlineData("ساعة كاملة على كروم… خود نفس 🌿", "ساعة كاملة على كروم… خدي نفس 🌿")]
    [InlineData("وقّف المؤقت", "وقّفي المؤقت")]
    [InlineData("وقّف القرآن", "وقّفي القرآن")]
    [InlineData("لحد ما وقّفها", "لحد ما توقّفيها")]
    [InlineData("5 · بتسحبها بالماوس", "5 · بتسحبيها بالماوس")]
    [InlineData("18 · أول ما تشغّل الجهاز", "18 · أول ما تشغّلي الجهاز")]
    [InlineData("تعبير 12 · رجعت", "تعبير 12 · رجعتي")]
    [InlineData("تعبير 18 · نسخت شي", "تعبير 18 · نسختي شي")]
    [InlineData("تعبير 22 · كبست عليها", "تعبير 22 · كبستي عليها")]
    [InlineData("تعبير 23 · سحبتها", "تعبير 23 · سحبتيها")]
    [InlineData("وقّف العرض / Stop the show", "وقّفي العرض / Stop the show")]
    [InlineData("الطقس: دمشق — وقّف / Weather: دمشق — off", "الطقس: دمشق — وقّفي / Weather: دمشق — off")]
    [InlineData("افتح مجلد سَحَر / Open Sahar folder", "افتحي مجلد سَحَر / Open Sahar folder")]
    [InlineData("الحقني إذا بتقدر! 😆", "الحقيني إذا بتقدري! 😆")]
    [InlineData("غمّض عيونك… 🙈", "غمّضي عيونك… 🙈")]
    [InlineData("اشلف الطابة وأنا بجيبلك ياها! ⚽", "اشلفي الطابة وأنا بجيبلك ياها! ⚽")]
    [InlineData("لقيتني بـ 14 ثانية بالمعدل 🏆", "لقيتيني بـ 14 ثانية بالمعدل 🏆")]
    [InlineData("تصبح على خير", "تصبحي على خير")]
    [InlineData("منيحة! وإنت كيفك؟", "منيحة! وإنتي كيفك؟")]
    [InlineData("ما بعرف مدينتك لسا. خبّرني ياها.", "ما بعرف مدينتك لسا. خبّريني ياها.")]
    [InlineData("اكتبلي ياها هون.", "اكتبيلي ياها هون.")]
    public void ToAWoman_TheFeminineForm(string male, string female)
    {
        Assert.Equal(female, Address.For(Gender.Female, male));
        Assert.Equal(male, Address.For(Gender.Male, male));
        Assert.Equal(male, Address.For(Gender.Unknown, male));
    }

    [Fact]
    public void Feminine_IsNeverAppliedTwice() =>
        Assert.Equal("رجعتي", Address.For(Gender.Female, Address.For(Gender.Female, "رجعت")));

    [Theory]
    [InlineData("شو مدينتك؟ (لأوقات الصلاة)")]
    [InlineData("ما قدرت شغّل التلاوة هلق")]   // she speaks of herself
    [InlineData("العبي")]                        // you speak to her
    [InlineData("رجّعي")]
    public void NeutralOrAboutHer_Unchanged(string text) => Assert.Equal(text, Address.For(Gender.Female, text));

    [Fact]
    public void MorningHello_UsesYourName()
    {
        Assert.Equal("صباح الخير ☀️", CommentRules.Pick(null, TimeSpan.Zero, 8, true));
        Assert.Equal("صباح الخير يا سامي ☀️", CommentRules.Pick(null, TimeSpan.Zero, 8, true, "سامي"));
    }

    [Fact]
    public void Settings_WhoYouAre_DefaultsUnknown()
    {
        var s = new SaharSettings();
        Assert.Equal(Gender.Unknown, s.UserGender);
        Assert.Null(s.UserName);
        Assert.False(s.HelloDone);
    }

    [Theory]
    [InlineData("  سامي  ", "سامي")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("اسم طويل كتير كتير كتير كتير كتير", "اسم طويل كتير كتير ك")]   // 20 characters at most
    public void Name_IsTrimmedAndShort(string typed, string? expected) => Assert.Equal(expected, Address.CleanName(typed));

    [Fact]
    public void Pairs_AreTheWholeWordTable()
    {
        Assert.Contains(("اكتب", "اكتبي"), Address.Pairs);
        foreach (var (male, female) in Address.Pairs) Assert.Equal(female, Address.For(Gender.Female, male));
    }
}
