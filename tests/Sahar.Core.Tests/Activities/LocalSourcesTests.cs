using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class LocalSourcesTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Pace_FastCopy_EtaShrinks()
    {
        var p = new CopyPace();
        p.Sample(0, T);
        p.Sample(10, T.AddSeconds(1));
        var a = p.Left!.Value;
        Assert.InRange(a.TotalSeconds, 5, 15);
        p.Sample(20, T.AddSeconds(2));
        Assert.True(p.Left < a);
        Assert.False(p.Done);
    }

    [Fact]
    public void Pace_Stall_NoEta()
    {
        var p = new CopyPace();
        p.Sample(0, T);
        p.Sample(10, T.AddSeconds(1));
        p.Sample(10, T.AddSeconds(7));
        Assert.Null(p.Left);
    }

    [Fact]
    public void Pace_FinishIsDone()
    {
        var p = new CopyPace();
        p.Sample(40, T);
        p.Sample(100, T.AddSeconds(1));
        Assert.True(p.Done);
        var q = new CopyPace();
        q.Sample(40, T);
        q.Gone(T.AddSeconds(2));
        Assert.True(q.Done);
        var none = new CopyPace();
        none.Gone(T);
        Assert.False(none.Done);   // a dialog that never showed progress finished nothing we saw
    }

    [Fact]
    public void Away_UnderTwoMinutes_Nothing()
    {
        var a = new AwayLog();
        a.Lock(T);
        a.Note(ActivityKind.ClaudeDone, T.AddSeconds(30));
        Assert.Null(a.Unlock(T.AddSeconds(110)));
    }

    [Fact]
    public void Away_NothingHappened_Nothing()
    {
        var a = new AwayLog();
        a.Lock(T);
        Assert.Null(a.Unlock(T.AddHours(1)));
    }

    [Fact]
    public void Away_NotLocked_NotesIgnored()
    {
        var a = new AwayLog();
        a.Note(ActivityKind.ClaudeDone, T);
        a.Lock(T.AddMinutes(1));
        Assert.Null(a.Unlock(T.AddMinutes(10)));
    }

    [Fact]
    public void Away_CountsKindsOnly_RowCapFive()
    {
        var a = new AwayLog();
        a.Lock(T);
        a.Note(ActivityKind.ClaudeDone, T.AddMinutes(1));
        a.Note(ActivityKind.ClaudeDone, T.AddMinutes(5));
        foreach (var (k, m) in new[] { (ActivityKind.CopyDone, 2), (ActivityKind.WeatherAlert, 3), (ActivityKind.Charging, 4),
                     (ActivityKind.Screenshot, 6), (ActivityKind.BatteryLow, 7), (ActivityKind.Back, 8) }) a.Note(k, T.AddMinutes(m));
        var s = a.Unlock(T.AddMinutes(70))!;
        Assert.Equal(TimeSpan.FromMinutes(70), s.Away);
        Assert.Equal(5, s.Rows.Count);
        Assert.Equal(2, s.More);
        Assert.Equal(8, s.Total);
        Assert.Equal((ActivityKind.Back, 1), (s.Rows[0].Kind, s.Rows[0].Count));
        Assert.Contains(s.Rows, r => r.Kind == ActivityKind.ClaudeDone && r.Count == 2);
        Assert.Null(a.Unlock(T.AddMinutes(80)));   // once
    }

    private static string Folder() => Directory.CreateTempSubdirectory("usage").FullName;

    [Fact]
    public void ScreenTime_AddsPerApp_Sorted()
    {
        var st = new ScreenTime(Folder());
        st.Add("Code", TimeSpan.FromMinutes(30), T);
        st.Add("Chrome", TimeSpan.FromMinutes(50), T);
        st.Add("Code", TimeSpan.FromMinutes(40), T);
        Assert.Equal([("Code", TimeSpan.FromMinutes(70)), ("Chrome", TimeSpan.FromMinutes(50))], st.Today(T));
        Assert.Equal(TimeSpan.FromMinutes(120), st.Total(T));
    }

    [Fact]
    public void ScreenTime_DayRollover_SavedAndReadBack()
    {
        var dir = Folder();
        var st = new ScreenTime(dir);
        st.Add("Code", TimeSpan.FromMinutes(30), T);
        st.Add("Code", TimeSpan.FromMinutes(5), T.AddDays(1));
        Assert.Equal(TimeSpan.FromMinutes(5), st.Total(T.AddDays(1)));
        Assert.True(File.Exists(Path.Combine(dir, "2026-11-13.json")));
        st.Save();
        var again = new ScreenTime(dir);
        Assert.Equal(TimeSpan.FromMinutes(5), again.Total(T.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(30), again.Total(T));
    }

    [Fact]
    public void ScreenTime_PrunesAfterSevenDays()
    {
        var dir = Folder();
        foreach (var d in new[] { 0, 6, 7, 30 }) File.WriteAllText(Path.Combine(dir, $"{DateOnly.FromDateTime(T.Date).AddDays(-d):yyyy-MM-dd}.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not ours");
        new ScreenTime(dir).Prune(T);
        var left = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["2026-11-07.json", "2026-11-13.json", "notes.txt"], left);
    }

    [Fact]
    public void ScreenTime_BrokenFile_StartsEmpty()
    {
        var dir = Folder();
        File.WriteAllText(Path.Combine(dir, "2026-11-13.json"), "{not json");
        Assert.Equal(TimeSpan.Zero, new ScreenTime(dir).Total(T));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]   // a playing video counts
    [InlineData(false, true, true, false)]
    public void ScreenTime_Counts(bool idle, bool locked, bool video, bool counts) => Assert.Equal(counts, ScreenTime.Counts(idle, locked, video));

    [Theory]
    [InlineData(@"C:\Program Files\Microsoft VS Code\Code.exe", "Code")]
    [InlineData(@"C:\x\chrome.exe", "Chrome")]
    [InlineData(@"C:\x\msedge.exe", "Edge")]
    [InlineData(@"C:\x\ms-teams.exe", "Teams")]
    [InlineData(@"C:\x\explorer.exe", "الملفات")]
    public void ScreenTime_AppName(string path, string name) => Assert.Equal(name, ScreenTime.AppName(path));

    [Fact]
    public void ToFull_FromReport()
    {
        Assert.Equal(TimeSpan.FromMinutes(40), SystemNow.ToFull(30000, 50000, 30000));
        Assert.Null(SystemNow.ToFull(30000, 50000, 0));
        Assert.Null(SystemNow.ToFull(30000, 50000, -5000));
        Assert.Null(SystemNow.ToFull(50000, 50000, 1000));
    }

    [Theory]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "WindowsCamera")]
    [InlineData("C:#Program Files#WindowsApps#MSTeams#ms-teams.exe", "Teams")]
    [InlineData("C:#Users#me#AppData#Local#Discord#app-1.0#Discord.exe", "Discord")]
    public void AppName_PackagedAndPath(string key, string name) => Assert.Equal(name, SystemNow.AppName(key));

    [Fact]
    public void Shelf_AddDedupe_SavedAndLoaded()
    {
        var path = Path.Combine(Folder(), "shelf.json");
        var s = new Shelf(path);
        s.Add([@"C:\a.png", @"C:\b.pdf", @"C:\a.png"], T);
        Assert.Equal([@"C:\a.png", @"C:\b.pdf"], s.Items.Select(i => i.Path));
        Assert.Equal([@"C:\a.png", @"C:\b.pdf"], new Shelf(path).Items.Select(i => i.Path));
        s.Clear();
        Assert.Empty(new Shelf(path).Items);
    }

    [Fact]
    public void Shelf_MissingFilesDropOut()
    {
        var s = new Shelf(Path.Combine(Folder(), "shelf.json"));
        s.Add([@"C:\here.txt", @"C:\gone.txt"], T);
        Assert.Equal(1, s.Prune(p => p.EndsWith("here.txt")));
        Assert.Equal([@"C:\here.txt"], s.Items.Select(i => i.Path));
    }

    [Fact]
    public void Shelf_BrokenFile_Empty()
    {
        var path = Path.Combine(Folder(), "shelf.json");
        File.WriteAllText(path, "[[[");
        Assert.Empty(new Shelf(path).Items);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(100, 2000, false)]
    [InlineData(2000, 2000, true)]
    public void Settle_GrowingFileWaits(long before, long now, bool stable) => Assert.Equal(stable, Settle.Stable(before, now));
}

public class SourceParsingTests
{
    [Theory]
    [InlineData("42% complete", 42.0)]
    [InlineData("اكتمل 42%", 42.0)]
    [InlineData("اكتمل ٤٢٪", 42.0)]
    [InlineData("7 % terminé", 7.0)]
    [InlineData("Copying 3 items", null)]
    [InlineData("", null)]
    [InlineData("150% complete", null)]
    public void CopyTitle_Percent(string title, double? percent) => Assert.Equal(percent, CopyTitle.Percent(title));

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\Screenshots\Screenshot 2026-11-13 141402.png", ShelfThing.Shot, "PNG")]
    [InlineData(@"C:\Users\me\Pictures\sea.jpg", ShelfThing.Photo, "JPG")]
    [InlineData(@"C:\Users\me\Documents\العقد.pdf", ShelfThing.Doc, "PDF")]
    [InlineData(@"C:\notes.TXT", ShelfThing.Doc, "TXT")]
    [InlineData(@"C:\folder", ShelfThing.Doc, "")]
    public void ShelfThings_Kind(string path, ShelfThing thing, string ext)
    {
        var (t, e) = ShelfThings.Of(path, @"C:\Users\me\Pictures\Screenshots");
        Assert.Equal((thing, ext), (t, e));
    }

    [Fact]
    public void ShelfThings_Label_NameWithoutExtension() => Assert.Equal("العقد", ShelfThings.Label(@"C:\x\العقد.pdf"));

    [Fact]
    public void ShelfThings_IsImage() => Assert.True(ShelfThings.IsImage(@"a\b.JPEG") && !ShelfThings.IsImage(@"a\b.pdf"));

    [Fact]
    public void ScreenTime_TotalOfADay()
    {
        var dir = Directory.CreateTempSubdirectory("usage2").FullName;
        File.WriteAllText(Path.Combine(dir, "2026-11-12.json"), """{"apps":{"Code":3600,"Chrome":600}}""");
        Assert.Equal(TimeSpan.FromSeconds(4200), ScreenTime.TotalOf(dir, new DateOnly(2026, 11, 12)));
        Assert.Null(ScreenTime.TotalOf(dir, new DateOnly(2026, 11, 11)));
    }

    [Theory]
    [InlineData("Screenshot.png", "lsd.zip", "Screenshot.zip")]
    public void Zip_NameFromTheFirst(string first, string _, string zip) => Assert.Equal(zip, ShelfThings.ZipName([first]));

    [Fact]
    public void Zip_Many_ShelfName() => Assert.Equal("رف سحر.zip", ShelfThings.ZipName(["a.png", "b.pdf"]));

    [Theory]
    [InlineData(4000, 3000, 1920, 1440)]
    [InlineData(1000, 3000, 640, 1920)]
    [InlineData(800, 600, 800, 600)]
    public void Shrink_LongSide1920(int w, int h, int ew, int eh) => Assert.Equal((ew, eh), ShelfThings.Shrunk(w, h));

    [Fact]
    public void Shrink_Name() => Assert.Equal(@"C:\x\sea-صغيرة.jpg", ShelfThings.ShrunkPath(@"C:\x\sea.png"));
}
