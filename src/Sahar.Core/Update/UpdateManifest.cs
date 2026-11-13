using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Update;

/// <summary>An offer to update: a newer setup, where it is, and its SHA-256 from the signed manifest.</summary>
public sealed record UpdateOffer(SemVersion Version, string SetupName, string SetupUrl, string Sha256);

/// <summary>
/// "Sahar-update.json" beside the setup in a release: its version, its file name and its SHA-256, for one channel,
/// signed (ECDSA P-256 over the file's bytes, "Sahar-update.json.sig" = base64 of r‖s). The signature makes the setup
/// authentic, not only intact; the signed version and channel mean an older signed build cannot be passed off as new.
/// </summary>
public sealed record UpdateManifest(SemVersion Version, string Setup, string Sha256, UpdateChannel Channel)
{
    public const string FileName = "Sahar-update.json";
    public const string SignatureName = "Sahar-update.json.sig";

    public static string ChannelName(UpdateChannel c) => c == UpdateChannel.Stable ? "stable" : "test-build";

    /// <summary>The manifest when <paramref name="signature"/> is the key's signature of exactly these bytes and every
    /// field holds (the setup is "Sahar-Setup-&lt;version&gt;.exe", 64 hex digits, the expected channel); else null.</summary>
    public static UpdateManifest? Verify(ReadOnlySpan<byte> manifest, string signature, string publicKeySpki, UpdateChannel channel)
    {
        byte[] sig;
        try { sig = Convert.FromBase64String(signature.Trim('﻿', ' ', '\t', '\r', '\n')); }
        catch (FormatException) { return null; }
        if (sig.Length != 64) return null;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpki), out _);
            if (!key.VerifyData(manifest, sig, HashAlgorithmName.SHA256)) return null;
        }
        catch (CryptographicException) { return null; }

        ManifestDto? dto;
        try { dto = JsonSerializer.Deserialize(Encoding.UTF8.GetString(manifest).TrimStart('﻿'), ManifestJson.Default.ManifestDto); }
        catch (JsonException) { return null; }
        if (dto is not { Version: { } v, Setup: { } setup, Sha256: { } sha, Channel: { } ch }) return null;
        if (!SemVersion.TryParse(v, out var version) || setup != $"Sahar-Setup-{version}.exe") return null;
        if (sha.Length != 64 || !sha.All(char.IsAsciiHexDigit) || ch != ChannelName(channel)) return null;
        return new UpdateManifest(version, setup, sha.ToLowerInvariant(), channel);
    }
}

internal sealed class ManifestDto
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("setup")] public string? Setup { get; set; }
    [JsonPropertyName("sha256")] public string? Sha256 { get; set; }
    [JsonPropertyName("channel")] public string? Channel { get; set; }
}

[JsonSerializable(typeof(ManifestDto))]
internal sealed partial class ManifestJson : JsonSerializerContext;
