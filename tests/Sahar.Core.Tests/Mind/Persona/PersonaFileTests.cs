using Sahar.Core.Mind.Persona;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Persona;

/// <summary>0.16 Task 7 (owner ruling 8): her personality as a plain file he can edit, with the default one step away.</summary>
public sealed class PersonaFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-personafile-" + Guid.NewGuid().ToString("N"));
    private PersonaFile File() => new(Path.Combine(_dir, PersonaFile.FileName));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void NoFile_LoadsTheDefault_NotEdited()
    {
        var f = File();
        Assert.Equal(PersonaFile.Default, f.Load());
        Assert.False(f.Edited);
        Assert.False(System.IO.File.Exists(f.Path));
    }

    [Fact]
    public void UserPath_IsNextToSettings()
    {
        var p = PersonaFile.ForUser().Path;
        Assert.Equal(Path.GetDirectoryName(SettingsStore.DefaultPath), Path.GetDirectoryName(p));
        Assert.Equal("persona.md", Path.GetFileName(p));
    }

    [Fact]
    public void Default_IsSyrianArabic_HerIdentity_EmojiWelcome()
    {
        Assert.Contains("سَحَر", PersonaFile.Default);
        Assert.Contains("شامي", PersonaFile.Default);
        Assert.InRange(PersonaFile.Default.Length, 200, PersonaFile.MaxChars);
        // Owner correction 2026-11-26: he wants emoji in her lines; the persona says so and never forbids them.
        Assert.Contains("إيموجي", PersonaFile.Default);
        Assert.DoesNotContain("بلا إيموجي", PersonaFile.Default);
        Assert.DoesNotContain("لا تستعملي إيموجي", PersonaFile.Default);
    }

    [Fact]
    public void Save_ThenLoad_GivesHisText_AndEdited()
    {
        var f = File();
        Assert.Equal(PersonaSave.Ok, f.Save("إنتِ سَحَر، بتحبي النكت.\n"));
        Assert.Equal("إنتِ سَحَر، بتحبي النكت.", f.Load());
        Assert.True(f.Edited);
        Assert.True(System.IO.File.Exists(f.Path));
    }

    [Fact]
    public void Save_RefusesEmptyAndTooLong_FileUntouched()
    {
        var f = File();
        f.Save("نسخة أولى");
        Assert.Equal(PersonaSave.Empty, f.Save("   \n "));
        Assert.Equal(PersonaSave.TooLong, f.Save(new string('ب', PersonaFile.MaxChars + 1)));
        Assert.Equal("نسخة أولى", f.Load());
    }

    [Fact]
    public void Save_TheDefaultText_IsNotAnEdit()
    {
        var f = File();
        f.Save("شي تاني");
        Assert.Equal(PersonaSave.Ok, f.Save(PersonaFile.Default + "\r\n"));
        Assert.False(f.Edited);
        Assert.False(System.IO.File.Exists(f.Path));
    }

    [Fact]
    public void Restore_RemovesHisFile_AndGivesTheDefault()
    {
        var f = File();
        f.Save("شخصية تانية");
        Assert.Equal(PersonaFile.Default, f.Restore());
        Assert.False(System.IO.File.Exists(f.Path));
        Assert.Equal(PersonaFile.Default, f.Load());
        Assert.False(f.Edited);
        Assert.Equal(PersonaFile.Default, f.Restore());   // twice: still fine
    }

    [Fact]
    public void BlankOrHugeFileOnDisk_IsReadSafely()
    {
        var f = File();
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(f.Path, "  \n\t ");
        Assert.Equal(PersonaFile.Default, f.Load());
        System.IO.File.WriteAllText(f.Path, new string('س', PersonaFile.MaxChars * 3));
        Assert.Equal(PersonaFile.MaxChars, f.Load().Length);
    }

    [Fact]
    public void UnreadableFile_GivesTheDefault_NeverThrows()
    {
        var f = new PersonaFile(_dir);   // a folder where the file should be
        Directory.CreateDirectory(_dir);
        Assert.Equal(PersonaFile.Default, f.Load());
        Assert.Equal(PersonaSave.Io, f.Save("x"));
    }

    [Fact]
    public void ForGender_PicksTheWordForHimOrHer()
    {
        const string text = "إذا {بدو|بدها} حل، {ساعديه|ساعديها}. {مو|هاد} {نص بلا فاصلة} و a|b.";
        Assert.Equal("إذا بدو حل، ساعديه. مو {نص بلا فاصلة} و a|b.", PersonaFile.ForGender(text, Gender.Male));
        Assert.Equal("إذا بدها حل، ساعديها. هاد {نص بلا فاصلة} و a|b.", PersonaFile.ForGender(text, Gender.Female));
        Assert.Equal(PersonaFile.ForGender(text, Gender.Male), PersonaFile.ForGender(text, Gender.Unknown));   // Arabic's default
    }

    [Fact]
    public void ForGender_DefaultLeavesNoBraces()
    {
        foreach (var g in Enum.GetValues<Gender>())
        {
            var t = PersonaFile.ForGender(PersonaFile.Default, g);
            Assert.DoesNotContain("{", t);
            Assert.DoesNotContain("|", t);
        }
    }
}
