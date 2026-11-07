using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

public class CleanStartTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sahar-clean-{Guid.NewGuid():N}");
    private string Roaming => Path.Combine(_root, "Roaming", "Sahar");
    private string Local => Path.Combine(_root, "Local", "Sahar");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Old()
    {
        Directory.CreateDirectory(Path.Combine(Roaming, "sub"));
        File.WriteAllText(Path.Combine(Roaming, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Roaming, "notes.json"), "[]");
        File.WriteAllText(Path.Combine(Roaming, "clip-1.bmp"), "x");
        File.WriteAllText(Path.Combine(Roaming, "sub", "x"), "x");
        Directory.CreateDirectory(Local);
        File.WriteAllText(Path.Combine(Local, "engine.log"), "old");
    }

    [Fact]
    public void FirstRunOfTheCleanVersion_WipesOldDataOnce()
    {
        Old();
        Assert.True(CleanStart.Run(Roaming, Local));
        Assert.Equal(["data-version"], Directory.GetFileSystemEntries(Roaming).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFileSystemEntries(Local));
        Assert.Equal(CleanStart.DataVersion.ToString(), File.ReadAllText(Path.Combine(Roaming, "data-version")).Trim());

        // Later runs keep what the clean version saved.
        File.WriteAllText(Path.Combine(Roaming, "notes.json"), "[\"new\"]");
        Assert.False(CleanStart.Run(Roaming, Local));
        Assert.True(File.Exists(Path.Combine(Roaming, "notes.json")));
    }

    [Fact]
    public void FreshPc_NothingToWipe_JustMarks()
    {
        Assert.False(CleanStart.Run(Roaming, Local));
        Assert.True(File.Exists(Path.Combine(Roaming, "data-version")));
    }

    [Fact]
    public void AFileInUse_DoesNotStopTheRest()
    {
        Old();
        using var locked = new FileStream(Path.Combine(Local, "engine.log"), FileMode.Open, FileAccess.Read, FileShare.None);
        CleanStart.Run(Roaming, Local);   // never throws
        Assert.False(File.Exists(Path.Combine(Roaming, "notes.json")));
        Assert.True(File.Exists(Path.Combine(Roaming, "data-version")));
    }

    [Fact]
    public void DataThisVersionSaved_WithoutAMarker_IsNeverWiped()
    {
        // The marker lost (its write failed, or the folder was emptied while she ran): settings that only the clean
        // version writes ("helloDone") show the data is new, so nothing goes.
        Directory.CreateDirectory(Roaming);
        File.WriteAllText(Path.Combine(Roaming, "settings.json"), "{\n  \"helloDone\": true\n}");
        File.WriteAllText(Path.Combine(Roaming, "notes.json"), "[\"mine\"]");
        Assert.False(CleanStart.Run(Roaming, Local));
        Assert.True(File.Exists(Path.Combine(Roaming, "notes.json")));
        Assert.True(File.Exists(Path.Combine(Roaming, "data-version")));
    }
}
