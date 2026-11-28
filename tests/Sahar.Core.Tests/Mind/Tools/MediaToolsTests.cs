using System.Text.Json;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>0.19 spec §8: the two built-in media tools and their lines.</summary>
public sealed class MediaToolsTests
{
    [Fact]
    public void Specs_AreMediaClassBWithCappedArgs()
    {
        Assert.Equal(("image.make", ToolSource.Media, ActionClass.B), (MediaTools.Image.Name, MediaTools.Image.Source, MediaTools.Image.Class));
        Assert.Equal(300, MediaTools.Image.Args.Single(a => a.Name == "prompt_en").MaxLength);
        Assert.Equal(("voice.make", ToolSource.Media), (MediaTools.Voice.Name, MediaTools.Voice.Source));
        Assert.Equal(200, MediaTools.VoiceMax);
    }

    [Fact]
    public void ImageUri_EscapesPrompt()
    {
        var u = MediaTools.ImageUri("a cat & a moon/sky?");
        Assert.StartsWith("https://image.pollinations.ai/prompt/", u.AbsoluteUri);
        Assert.Contains("a%20cat%20%26%20a%20moon%2Fsky%3F", u.AbsoluteUri);
    }

    [Fact]
    public void FileName_Pattern()
    {
        var at = new DateTimeOffset(2026, 11, 28, 9, 5, 7, TimeSpan.Zero);
        Assert.Equal("sahar-image-20261128-090507.png", MediaTools.FileName("image", at));
        Assert.Equal("sahar-voice-20261128-090507.wav", MediaTools.FileName("voice", at));
    }

    [Theory]
    [InlineData("sahar-image-20261128-090507.png", true)]
    [InlineData("sahar-voice-20261128-090507-2.wav", true)]
    [InlineData("../sahar-image-20261128-090507.png", false)]
    [InlineData("sahar-image-20261128-090507.png.exe", false)]
    [InlineData("sahar-voice-20261128-090507.png", false)]
    [InlineData("other.png", false)]
    public void IsMediaFile_RejectsPathsAndOtherNames(string name, bool want) => Assert.Equal(want, MediaTools.IsMediaFile(name));

    private static ToolSpec Mcp(bool sensitive = false) => new("mcp.files.read_file", "", [], ActionClass.B, Source: ToolSource.Mcp, Sensitive: sensitive);

    [Fact]
    public void Ask_McpNamesToolAndServer()
    {
        var q = ToolTemplates.Ask(Mcp());
        Assert.Contains("read_file", q);
        Assert.Contains("files", q);
        Assert.Contains("حساسة", ToolTemplates.Ask(Mcp(sensitive: true)));
    }

    [Fact]
    public void Ask_ImageSaysPollinations()
    {
        Assert.Contains("Pollinations", ToolTemplates.Ask(MediaTools.Image));
        Assert.Contains("Groq", ToolTemplates.Ask(MediaTools.Voice));
    }

    [Fact]
    public void Done_McpCall_NamesTheTool() =>
        Assert.Contains("read_file", ToolTemplates.Done(new ToolCall("mcp.files.read_file", JsonDocument.Parse("{}").RootElement.Clone())));

    [Fact]
    public void ImageWait_SaysSeconds() => Assert.Contains("٤٥", ToolTemplates.ImageWait(45));
}
