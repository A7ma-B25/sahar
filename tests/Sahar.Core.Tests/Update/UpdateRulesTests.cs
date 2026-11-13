using System.Security.Cryptography;
using System.Text;
using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

public class UpdateRulesTests
{
    private static readonly SemVersion Current = new(0, 11, 0, null);
    private const string Gh = "https://github.com/A7ma-B25/sahar/releases/download/test-build/";

    private static string Asset(string name, string? url = null) =>
        $$"""{"name":"{{name}}","browser_download_url":"{{url ?? Gh + name}}","size":123,"content_type":"application/octet-stream"}""";

    private static string Json(bool draft = false, params string[] assets) =>
        $$"""{"tag_name":"test-build","name":"Sahar test build","draft":{{(draft ? "true" : "false")}},"prerelease":true,"assets":[{{string.Join(",", assets)}}]}""";

    private static Release R(string json) => Release.Parse(json) ?? throw new InvalidOperationException("did not parse");

    [Fact]
    public void Picks_TheNewerSetup_WithItsChecksum()
    {
        var r = R(Json(false, Asset("Sahar-0.12.0-test-win-x64.zip"), Asset("Sahar-Setup-0.12.0.exe"), Asset("Sahar-Setup-0.12.0.exe.sha256"),
            Asset("gallery.png")));
        var offer = UpdateRules.Pick(r, Current, allowLoopback: false);
        Assert.NotNull(offer);
        Assert.Equal("0.12.0", offer.Version.ToString());
        Assert.Equal("Sahar-Setup-0.12.0.exe", offer.Setup.Name);
        Assert.Equal(Gh + "Sahar-Setup-0.12.0.exe", offer.Setup.Url);
        Assert.Equal("Sahar-Setup-0.12.0.exe.sha256", offer.Checksum.Name);
        Assert.Equal(123, offer.Setup.Size);
    }

    [Fact]
    public void Picks_TheHighestOfSeveralSetups()
    {
        var r = R(Json(false, Asset("Sahar-Setup-0.12.0.exe"), Asset("Sahar-Setup-0.12.0.exe.sha256"),
            Asset("Sahar-Setup-0.13.0-rc.1.exe"), Asset("Sahar-Setup-0.13.0-rc.1.exe.sha256")));
        Assert.Equal("0.13.0-rc.1", UpdateRules.Pick(r, Current, false)?.Version.ToString());
    }

    [Theory]
    [InlineData("0.11.0")]
    [InlineData("0.10.1")]
    public void Nothing_WhenNotNewer(string version) =>
        Assert.Null(UpdateRules.Pick(R(Json(false, Asset($"Sahar-Setup-{version}.exe"), Asset($"Sahar-Setup-{version}.exe.sha256"))), Current, false));

    [Fact]
    public void Nothing_WithoutAChecksum() =>
        Assert.Null(UpdateRules.Pick(R(Json(false, Asset("Sahar-Setup-0.12.0.exe"))), Current, false));

    [Fact]
    public void Nothing_WhenTheChecksumIsForAnotherFile() =>
        Assert.Null(UpdateRules.Pick(R(Json(false, Asset("Sahar-Setup-0.12.0.exe"), Asset("Sahar-Setup-0.11.0.exe.sha256"))), Current, false));

    [Fact]
    public void Nothing_ForADraft() =>
        Assert.Null(UpdateRules.Pick(R(Json(true, Asset("Sahar-Setup-0.12.0.exe"), Asset("Sahar-Setup-0.12.0.exe.sha256"))), Current, false));

    [Theory]
    [InlineData("Sahar-Setup-0.12.exe")]
    [InlineData("Sahar-Setup-latest.exe")]
    [InlineData("sahar-setup-0.12.0.exe")]
    [InlineData("Sahar-Setup-0.12.0.exe.zip")]
    public void Nothing_ForOtherNames(string name) =>
        Assert.Null(UpdateRules.Pick(R(Json(false, Asset(name), Asset(name + ".sha256"))), Current, false));

    [Theory]
    [InlineData("http://github.com/x/Sahar-Setup-0.12.0.exe")]
    [InlineData("https://evil.com/Sahar-Setup-0.12.0.exe")]
    [InlineData("https://github.com.evil.com/Sahar-Setup-0.12.0.exe")]
    [InlineData("http://127.0.0.1:8080/Sahar-Setup-0.12.0.exe")]
    public void Nothing_WhenTheSetupUrlIsNotAllowed(string url) =>
        Assert.Null(UpdateRules.Pick(R(Json(false, Asset("Sahar-Setup-0.12.0.exe", url), Asset("Sahar-Setup-0.12.0.exe.sha256"))), Current, false));

    [Fact]
    public void Loopback_OnlyWhenAllowed()
    {
        var r = R(Json(false, Asset("Sahar-Setup-0.12.0.exe", "http://127.0.0.1:8765/Sahar-Setup-0.12.0.exe"),
            Asset("Sahar-Setup-0.12.0.exe.sha256", "http://127.0.0.1:8765/Sahar-Setup-0.12.0.exe.sha256")));
        Assert.Null(UpdateRules.Pick(r, Current, false));
        Assert.NotNull(UpdateRules.Pick(r, Current, true));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/A7ma-B25/sahar/releases/tags/test-build", false, true)]
    [InlineData("https://github.com/A7ma-B25/sahar/releases/download/test-build/a.exe", false, true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", false, true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1", false, true)]
    [InlineData("https://githubusercontent.com.evil.org/x", false, false)]
    [InlineData("https://evilgithubusercontent.com/x", false, false)]
    [InlineData("http://api.github.com/x", false, false)]
    [InlineData("ftp://github.com/x", false, false)]
    [InlineData("file:///C:/x.exe", false, false)]
    [InlineData("not a url", false, false)]
    [InlineData("http://127.0.0.1:8765/x", false, false)]
    [InlineData("http://127.0.0.1:8765/x", true, true)]
    [InlineData("http://localhost:8765/x", true, true)]
    [InlineData("http://192.168.1.5:8765/x", true, false)]
    public void AllowedUrl(string url, bool loopback, bool expected) => Assert.Equal(expected, UpdateRules.AllowedUrl(url, loopback));

    private const string Hex = "a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c";

    [Theory]
    [InlineData(Hex + "  Sahar-Setup-0.12.0.exe\n")]
    [InlineData(Hex + " *Sahar-Setup-0.12.0.exe\r\n")]
    [InlineData(Hex + "\n")]
    [InlineData(Hex)]
    [InlineData("A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C  Sahar-Setup-0.12.0.exe")]
    public void ParseChecksum_Accepts(string text) => Assert.Equal(Hex, UpdateRules.ParseChecksum(text, "Sahar-Setup-0.12.0.exe"));

    [Theory]
    [InlineData(Hex + "  Sahar-Setup-0.11.0.exe")]
    [InlineData("a5499293  Sahar-Setup-0.12.0.exe")]
    [InlineData("")]
    [InlineData("zz49929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c")]
    public void ParseChecksum_Rejects(string text) => Assert.Null(UpdateRules.ParseChecksum(text, "Sahar-Setup-0.12.0.exe"));

    [Fact]
    public void Matches_TheSha256OfTheBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("سَحَر setup");
        var hex = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.True(UpdateRules.Matches(new MemoryStream(bytes), hex));
        Assert.True(UpdateRules.Matches(new MemoryStream(bytes), hex.ToUpperInvariant()));
        Assert.False(UpdateRules.Matches(new MemoryStream(bytes), Hex));
        Assert.False(UpdateRules.Matches(new MemoryStream(bytes), "not hex"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Not Found\"}")]
    public void Parse_BadJson_IsNull(string json) => Assert.Null(Release.Parse(json));

    [Fact]
    public void SetupArguments_AreTheSilentUpgradeFlags() =>
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"C:\\t\\setup.log\"", UpdateRules.SetupArguments(@"C:\t\setup.log"));
}
