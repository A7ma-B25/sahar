using System.Security.Cryptography;
using System.Text;
using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

public class UpdateManifestTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string Public = Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());
    private static readonly string Hex = new('c', 64);

    private static string Json(string version = "0.12.0", string? setup = null, string? sha = null, string channel = "test-build") =>
        $$"""{"version":"{{version}}","setup":"{{setup ?? $"Sahar-Setup-{version}.exe"}}","sha256":"{{sha ?? Hex}}","channel":"{{channel}}"}""";

    private static string Sign(byte[] bytes, ECDsa? key = null) => Convert.ToBase64String((key ?? Key).SignData(bytes, HashAlgorithmName.SHA256));

    private static UpdateManifest? Verify(string json, string? sig = null, UpdateChannel channel = UpdateChannel.TestBuild)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return UpdateManifest.Verify(bytes, sig ?? Sign(bytes), Public, channel);
    }

    [Fact]
    public void ASignedManifest_IsRead()
    {
        var m = Verify(Json());
        Assert.NotNull(m);
        Assert.Equal("0.12.0", m.Version.ToString());
        Assert.Equal("Sahar-Setup-0.12.0.exe", m.Setup);
        Assert.Equal(Hex, m.Sha256);
        Assert.Equal(UpdateChannel.TestBuild, m.Channel);
    }

    [Fact]
    public void ASignatureFile_WithABomAndANewline_IsRead()
    {
        var bytes = Encoding.UTF8.GetBytes(Json());
        Assert.NotNull(UpdateManifest.Verify(bytes, "\uFEFF" + Sign(bytes) + "\r\n", Public, UpdateChannel.TestBuild));
    }

    [Fact]
    public void AChangedByte_FailsTheSignature()
    {
        var bytes = Encoding.UTF8.GetBytes(Json());
        var sig = Sign(bytes);
        var other = Encoding.UTF8.GetBytes(Json(sha: new string('d', 64)));
        Assert.Null(UpdateManifest.Verify(other, sig, Public, UpdateChannel.TestBuild));
    }

    [Fact]
    public void AnotherKey_FailsTheSignature()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Encoding.UTF8.GetBytes(Json());
        Assert.Null(UpdateManifest.Verify(bytes, Sign(bytes, other), Public, UpdateChannel.TestBuild));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void ABadSignatureFile_IsRefused(string sig) => Assert.Null(Verify(Json(), sig));

    [Theory]
    [InlineData("0.12.0", "Sahar-Setup-0.13.0.exe", null, "test-build")]   // the version is bound to the setup's name
    [InlineData("0.12.0", "Sahar-Setup-0.12.0.exe.bat", null, "test-build")]
    [InlineData("0.12.0", "..\\Sahar-Setup-0.12.0.exe", null, "test-build")]
    [InlineData("0.12", "Sahar-Setup-0.12.exe", null, "test-build")]
    [InlineData("0.12.0", null, "abc", "test-build")]
    [InlineData("0.12.0", null, "zzcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "test-build")]
    [InlineData("0.12.0", null, null, "stable")]   // signed for another channel
    public void ASignedButWrongManifest_IsRefused(string version, string? setup, string? sha, string channel) =>
        Assert.Null(Verify(Json(version, setup, sha, channel)));

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"version\":null,\"setup\":null,\"sha256\":null,\"channel\":null}")]
    public void ASignedNonManifest_IsRefused(string json) => Assert.Null(Verify(json));

    [Fact]
    public void TheProductionKey_IsAP256PublicKey()
    {
        using var k = ECDsa.Create();
        k.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateKeys.Production), out _);
        Assert.Equal(256, k.KeySize);
    }

    [Fact]
    public void Names() => Assert.Equal(("Sahar-update.json", "Sahar-update.json.sig"), (UpdateManifest.FileName, UpdateManifest.SignatureName));
}
