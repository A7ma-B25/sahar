using System.Security.Cryptography;
using System.Text;
using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

public class UpdateRulesTests
{
    private static readonly SemVersion Current = new(0, 11, 0, null);
    private const string Base = "https://github.com/A7ma-B25/sahar/releases/download/test-build/";

    [Theory]
    [InlineData(UpdateChannel.TestBuild, null, "https://github.com/A7ma-B25/sahar/releases/download/test-build/")]
    [InlineData(UpdateChannel.Stable, null, "https://github.com/A7ma-B25/sahar/releases/latest/download/")]
    [InlineData(UpdateChannel.TestBuild, "http://127.0.0.1:8765", "http://127.0.0.1:8765/A7ma-B25/sahar/releases/download/test-build/")]
    [InlineData(UpdateChannel.TestBuild, "http://127.0.0.1:8765/", "http://127.0.0.1:8765/A7ma-B25/sahar/releases/download/test-build/")]
    public void DownloadBase(UpdateChannel channel, string? host, string expected) => Assert.Equal(expected, UpdateRules.DownloadBase(channel, host));

    [Fact]
    public void Offer_ANewerSignedVersion_PointsAtItsSetupBesideTheManifest()
    {
        var m = new UpdateManifest(new SemVersion(0, 12, 0, null), "Sahar-Setup-0.12.0.exe", new string('a', 64), UpdateChannel.TestBuild);
        var o = UpdateRules.Offer(m, Current, Base);
        Assert.NotNull(o);
        Assert.Equal("0.12.0", o.Version.ToString());
        Assert.Equal(Base + "Sahar-Setup-0.12.0.exe", o.SetupUrl);
        Assert.Equal("Sahar-Setup-0.12.0.exe", o.SetupName);
        Assert.Equal(new string('a', 64), o.Sha256);
    }

    [Theory]
    [InlineData("0.11.0")]
    [InlineData("0.10.1")]
    [InlineData("0.11.0-rc.1")]
    public void Offer_NothingWhenNotNewer_SoAnOldSignedBuildCannotBeReplayed(string version)
    {
        SemVersion.TryParse(version, out var v);
        Assert.Null(UpdateRules.Offer(new UpdateManifest(v, $"Sahar-Setup-{version}.exe", new string('a', 64), UpdateChannel.TestBuild), Current, Base));
    }

    [Theory]
    [InlineData("https://github.com/A7ma-B25/sahar/releases/download/test-build/Sahar-update.json", false, true)]
    [InlineData("https://github.com/A7ma-B25/sahar/releases/latest/download/Sahar-update.json", false, true)]
    [InlineData("https://github.com/someone/else/releases/download/test-build/Sahar-update.json", false, false)]
    [InlineData("https://github.com/A7ma-B25/sahar/archive/main.zip", false, false)]
    [InlineData("https://github.com/A7ma-B25/sahar/releases/download/../../../evil/x", false, false)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", false, false)]   // only a redirect may go there
    [InlineData("https://api.github.com/repos/A7ma-B25/sahar/releases/tags/test-build", false, false)]
    [InlineData("http://127.0.0.1:8765/A7ma-B25/sahar/releases/download/test-build/x", true, true)]
    [InlineData("http://127.0.0.1:8765/assets/x", true, false)]
    public void AllowedFirstHop(string url, bool loopback, bool expected) => Assert.Equal(expected, UpdateRules.AllowedFirstHop(url, loopback));

    [Theory]
    [InlineData("https://github.com/A7ma-B25/sahar/releases/download/test-build/a.exe", false, true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1", false, true)]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1", false, true)]
    [InlineData("https://githubusercontent.com.evil.org/x", false, false)]
    [InlineData("https://evilgithubusercontent.com/x", false, false)]
    [InlineData("https://github.com:8443/x", false, false)]
    [InlineData("http://github.com/x", false, false)]
    [InlineData("ftp://github.com/x", false, false)]
    [InlineData("file:///C:/x.exe", false, false)]
    [InlineData("not a url", false, false)]
    [InlineData("http://127.0.0.1:8765/assets/x", false, false)]
    [InlineData("http://127.0.0.1:8765/assets/x", true, true)]
    [InlineData("http://localhost:8765/x", true, true)]
    [InlineData("http://192.168.1.5:8765/x", true, false)]
    public void AllowedRedirectHop(string url, bool loopback, bool expected) => Assert.Equal(expected, UpdateRules.AllowedUrl(url, loopback));

    [Fact]
    public void Matches_TheSha256OfTheBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("سَحَر setup");
        var hex = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.True(UpdateRules.Matches(new MemoryStream(bytes), hex));
        Assert.True(UpdateRules.Matches(SHA256.HashData(bytes), hex.ToUpperInvariant()));
        Assert.False(UpdateRules.Matches(new MemoryStream(bytes), new string('0', 64)));
        Assert.False(UpdateRules.Matches(new MemoryStream(bytes), "not hex"));
    }

    [Fact]
    public void SetupArguments_SilentForAnInstalledSahar_TheWizardForAPortableOne()
    {
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"C:\\t\\setup.log\"", UpdateRules.SetupArguments(@"C:\t\setup.log", silent: true));
        Assert.Equal("/SP- /LOG=\"C:\\t\\setup.log\"", UpdateRules.SetupArguments(@"C:\t\setup.log", silent: false));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Sahar\", @"C:\Users\x\AppData\Local\Programs\Sahar\", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Sahar\", @"c:\users\x\appdata\local\programs\sahar", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Sahar", @"C:\Users\x\AppData\Local\Programs\Sahar\", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Sahar\", @"C:\Users\x\Downloads\Sahar-0.11.0-test-win-x64\", false)]
    [InlineData(null, @"C:\Users\x\Downloads\Sahar\", false)]
    [InlineData("", @"C:\Users\x\Downloads\Sahar\", false)]
    public void IsInstalled_WhenSheRunsFromTheUninstallEntrysFolder(string? installLocation, string exeDir, bool expected) =>
        Assert.Equal(expected, UpdateRules.IsInstalled(installLocation, exeDir));

    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MovedNote_SaidOnce_ByTheInstalledSahar_SoonAfterThePortableOneLeft()
    {
        var marker = UpdateRules.MoveMarker(T0);
        Assert.True(UpdateRules.SayMoved(marker, T0 + TimeSpan.FromMinutes(5), installed: true));
        Assert.False(UpdateRules.SayMoved(marker, T0 + TimeSpan.FromMinutes(5), installed: false));
        Assert.False(UpdateRules.SayMoved(marker, T0 + TimeSpan.FromHours(3), installed: true));   // an old marker (the wizard was cancelled)
        Assert.False(UpdateRules.SayMoved(null, T0, installed: true));
        Assert.False(UpdateRules.SayMoved("garbage", T0, installed: true));
        Assert.Contains(@"%LocalAppData%\Programs\Sahar", UpdateRules.MovedLine);   // never the expanded path: it holds the user name
    }
}
