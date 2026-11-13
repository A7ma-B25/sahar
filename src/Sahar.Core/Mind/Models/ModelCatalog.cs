using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Models;

/// <summary>
/// The recommended models (spec §4.2 + owner answer: Gemma 4 E4B default, Jais 3 the challenger, a small fallback),
/// each pinned to a Hugging Face commit and the file's LFS SHA-256 (read from the HF API, 2026-11-13).
/// </summary>
public static class ModelCatalog
{
    public const string DefaultId = "gemma-4-e4b-q4km";

    public static IReadOnlyList<ModelEntry> Recommended { get; } =
    [
        Pin(DefaultId, "Gemma 4 E4B", "default", "unsloth/gemma-4-E4B-it-GGUF", "bfc15c382204943c3a8fff0c750b94ae2364d7a3",
            "gemma-4-E4B-it-Q4_K_M.gguf", 4_977_171_584, "85a896a047553e842f25297ee5b031d64ff30147d9c4af17b1e4b394cd1fab87", "Q4_K_M"),
        Pin("jais-3-7b-q4km", "Jais 3 · 7B", "challenger", "IFM/Jais-3-7B-GGUF", "e171351744258b67bb6eb445e76d9d122bf542c0",
            "Jais-3-7B-Q4_K_M.gguf", 5_592_226_016, "a8caa7fecf38d064f216f66d3d59641c1fbefdca9756af0703f4a872ab852eeb", "Q4_K_M"),
        Pin("gemma-4-e2b-q4km", "Gemma 4 E2B", "light", "unsloth/gemma-4-E2B-it-GGUF", "0314792d7f1f7e229411f620751375812bb9faf2",
            "gemma-4-E2B-it-Q4_K_M.gguf", 3_106_738_272, "740185b21d22ceb83a11c3aa62ad5842ef32c70f6096d756bbee85a1e4ec34b8", "Q4_K_M"),
        Pin("gemma-4-e4b-q5km", "Gemma 4 E4B · Q5", "gpu8", "unsloth/gemma-4-E4B-it-GGUF", "bfc15c382204943c3a8fff0c750b94ae2364d7a3",
            "gemma-4-E4B-it-Q5_K_M.gguf", 5_481_798_784, "a5d6e634db151368d2caa4270fd32ab604d96c2d1291950586989f46d8e17d36", "Q5_K_M"),
        Pin("jais-3-3.7b-q4km", "Jais 3 · 3.7B", "light-challenger", "IFM/Jais-3-3.7B-GGUF", "dbdeb53ba90fc21a20b39e35564dcad2e8f3955a",
            "Jais-3-3.7B-Q4_K_M.gguf", 3_156_606_176, "d2039e169049f0646f79bc27aec82ffc0842ff9af1bf75bfc16628929636e09a", "Q4_K_M"),
    ];

    /// <summary>The tiny CI model (plan Ruling 4): never offered on the page.</summary>
    public static ModelEntry CiTiny { get; } = Pin("ci-gemma-3-270m-q8", "Gemma 3 270M (CI)", "ci", "ggml-org/gemma-3-270m-it-GGUF",
        "e7647be17ae1108f2f605ed061ca0608b171afff", "gemma-3-270m-it-Q8_0.gguf", 291_545_600,
        "0ef57d2c838458a1952664260dcba38e5bdda37494f3af732f06e4add24068e3", "Q8_0", license: "Gemma");

    public static ModelEntry? Find(string id) => Recommended.FirstOrDefault(m => m.Id == id) ?? (id == CiTiny.Id ? CiTiny : null);

    /// <summary>The pinned download address: <c>huggingface.co/&lt;repo&gt;/resolve/&lt;commit&gt;/&lt;file&gt;</c>.</summary>
    public static Uri Url(ModelEntry m) => new($"https://huggingface.co/{m.Repo}/resolve/{m.Commit}/{Uri.EscapeDataString(m.File!)}");

    private static ModelEntry Pin(string id, string name, string role, string repo, string commit, string file, long bytes, string sha,
        string quant, string license = "Apache-2.0") =>
        new(id, name, ModelOrigin.Recommended, role, bytes, sha, repo, commit, file, Quant: quant, License: license);
}
