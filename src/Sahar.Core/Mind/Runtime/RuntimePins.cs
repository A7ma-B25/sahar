using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Mind.Runtime;

public sealed record PinnedAsset(string Variant, string Name, long Bytes, string? Sha256, string? Source);
public sealed record LlamaPins(string Build, string Base, string License, IReadOnlyList<PinnedAsset> Assets);
public sealed record CiModelPin(string Repo, string Commit, string File, long Bytes, string Sha256);

/// <summary>tools/mind/pins.json, embedded: the llama.cpp release Sahar ships (Vulkan bundled; CUDA downloaded in the
/// app on his yes) and the CI model. A null SHA-256 means "not pinned": nothing with it is ever downloaded in the app.</summary>
public sealed record RuntimePins(LlamaPins LlamaCpp, CiModelPin CiModel)
{
    public string Build => LlamaCpp.Build;
    public IReadOnlyList<PinnedAsset> Assets => LlamaCpp.Assets;
    public PinnedAsset? Asset(string variant) => Assets.FirstOrDefault(a => a.Variant == variant);

    public static Uri Url(RuntimePins p, PinnedAsset a) => new(p.LlamaCpp.Base + a.Name);

    private static RuntimePins? _loaded;

    public static RuntimePins Load()
    {
        if (_loaded is { } l) return l;
        using var s = typeof(RuntimePins).Assembly.GetManifestResourceStream("Sahar.mind-pins.json")
            ?? throw new InvalidOperationException("mind-pins.json is not embedded");
        return _loaded = JsonSerializer.Deserialize(s, PinsJson.Default.RuntimePins)!;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimePins))]
internal sealed partial class PinsJson : JsonSerializerContext;
