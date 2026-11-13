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
}
