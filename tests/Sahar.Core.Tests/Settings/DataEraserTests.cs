using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>«امسح كل شي عني» (plan 0.10c Ruling 9): everything inside %AppData%\Sahar goes; the folder stays.</summary>
public sealed class DataEraserTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-erase-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void EraseAll_RemovesFilesAndFolders_KeepsTheFolder()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "notes.json"), "[]");
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "clipboard"));
        File.WriteAllText(Path.Combine(sub.FullName, "1.txt"), "x");
        Assert.Equal(3, DataEraser.EraseAll(_dir));
        Assert.True(Directory.Exists(_dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));
    }

    [Fact]
    public void EraseAll_MissingFolder_IsZero() => Assert.Equal(0, DataEraser.EraseAll(Path.Combine(_dir, "nope")));

    /// <summary>The clean-start marker is not her data: without it her next start would also empty the local folder
    /// (logs and the settings app's WebView2 folder, in use while the app restarts her).</summary>
    [Fact]
    public void EraseAll_KeepsWhatItIsAskedToKeep()
    {
        File.WriteAllText(Path.Combine(_dir, CleanStart.MarkerName), "2");
        File.WriteAllText(Path.Combine(_dir, "notes.json"), "[]");
        Assert.Equal(1, DataEraser.EraseAll(_dir, CleanStart.MarkerName));
        Assert.Equal([CleanStart.MarkerName], Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void EraseActivityData_RemovesUsageShelfLyrics_KeepsTheLog()
    {
        foreach (var d in new[] { "usage", "Shelf", "lyrics" })
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, d)).FullName, "x"), "x");
        File.WriteAllText(Path.Combine(_dir, "shelf.json"), "[]");
        File.WriteAllText(Path.Combine(_dir, "engine.log"), "log");
        Assert.Equal(4, DataEraser.EraseActivityData(_dir));
        Assert.Equal(["engine.log"], Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName));
    }
}
