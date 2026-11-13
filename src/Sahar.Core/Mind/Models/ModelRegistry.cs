using System.Text.Json;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Models;

/// <summary>
/// The models Sahar knows (models.json under <see cref="MindPaths.Models"/>; not personal): the recommended pins
/// plus what he added. One is active. Writes go through a temp file and replace. Switching models never touches his
/// memory (it lives elsewhere). A broken file is kept aside as models.json.broken and the list starts fresh.
/// </summary>
public sealed class ModelRegistry
{
    private readonly string _path;
    private List<ModelEntry> _models;
    private string? _active;

    public ModelRegistry(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "models.json");
        (_models, _active) = Load();
    }

    public ModelEntry? Active => _models.FirstOrDefault(m => m.Id == _active && m.Installed);

    public IReadOnlyList<ModelEntry> List() => _models.Select(m => m with { Active = m.Id == _active && m.Installed }).ToList();

    public ModelEntry? Find(string id) => List().FirstOrDefault(m => m.Id == id);

    public bool Use(string id)
    {
        if (_models.FirstOrDefault(m => m.Id == id) is not { Installed: true }) return false;
        _active = id;
        Save();
        return true;
    }

    public void MarkInstalled(string id, string path) => Change(id, m => m with { Installed = true, LocalPath = path });

    /// <summary>The file was deleted («احذفي»): the entry stays (recommended) or goes (added), and is no longer active.</summary>
    public void MarkRemoved(string id)
    {
        Change(id, m => m with { Installed = false, LocalPath = m.Origin == ModelOrigin.Local ? m.LocalPath : null });
        if (_active == id) { _active = null; Save(); }
    }

    public void SetBench(string id, BenchResult bench) => Change(id, m => m with { Bench = bench, Ability = bench.Ability });

    public ModelEntry AddHf(string owner, string repo, string commit, string file, long bytes, string sha256, string? license)
    {
        var id = $"hf:{owner}/{repo}/{file}";
        if (_models.FirstOrDefault(m => m.Id == id) is { } had) return had;
        var e = new ModelEntry(id, NameOf(file), ModelOrigin.HuggingFace, "custom", bytes, sha256, $"{owner}/{repo}", commit, file,
            Quant: QuantOf(file), License: license ?? "");
        _models.Add(e);
        Save();
        return e;
    }

    public ModelEntry AddLocal(string path, string sha256, long bytes)
    {
        var id = "file:" + sha256[..16];
        if (_models.FirstOrDefault(m => m.Id == id) is { } had) return had;
        var file = Path.GetFileName(path.Replace('\\', '/'));
        var e = new ModelEntry(id, NameOf(file), ModelOrigin.Local, "custom", bytes, sha256, File: file, LocalPath: path,
            Quant: QuantOf(file), Installed: true);
        _models.Add(e);
        Save();
        return e;
    }

    /// <summary>Removes an added model from the list (recommended ones stay listed; delete their file instead).</summary>
    public bool Remove(string id)
    {
        var m = _models.FirstOrDefault(x => x.Id == id);
        if (m is null || m.Origin == ModelOrigin.Recommended) return false;
        _models.Remove(m);
        if (_active == id) _active = null;
        Save();
        return true;
    }

    /// <summary>A typed or pasted path to a .gguf file (quotes stripped), or null.</summary>
    public static string? LocalGguf(string? text)
    {
        var s = (text ?? "").Trim().Trim('"');
        bool rooted = s.Length > 3 && (char.IsLetter(s[0]) && s[1] == ':' && s[2] is '\\' or '/' || s.StartsWith(@"\\", StringComparison.Ordinal) || s.StartsWith('/'));
        return rooted && s.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    /// <summary>"Qwen_Qwen3.5-4B-Q4_K_M.gguf" → "Q4_K_M" (the last dash-part that looks like a quant), else "".</summary>
    public static string QuantOf(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        foreach (var part in stem.Split('-', '.').Reverse())
            if (part.Length >= 2 && part[0] is 'Q' or 'q' or 'I' or 'i' && char.IsDigit(part[^1]) | part.Contains('_') || part.Equals("F16", StringComparison.OrdinalIgnoreCase) || part.Equals("BF16", StringComparison.OrdinalIgnoreCase))
                return part.ToUpperInvariant();
        return "";
    }

    private static string NameOf(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var q = QuantOf(file);
        if (q.Length > 0 && stem.EndsWith(q, StringComparison.OrdinalIgnoreCase)) stem = stem[..^q.Length].TrimEnd('-', '.', '_');
        return stem.Replace('_', ' ');
    }

    private void Change(string id, Func<ModelEntry, ModelEntry> f)
    {
        int i = _models.FindIndex(m => m.Id == id);
        if (i < 0) return;
        _models[i] = f(_models[i]);
        Save();
    }

    private (List<ModelEntry>, string?) Load()
    {
        ModelManifest? file = null;
        if (File.Exists(_path))
        {
            try { file = JsonSerializer.Deserialize(File.ReadAllText(_path), MindJson.Default.ModelManifest); }
            catch (JsonException) { File.Copy(_path, _path + ".broken", overwrite: true); }
        }
        var saved = file?.Models ?? [];
        // The recommended pins always come from this build (a newer build's pins replace older ones); his state stays.
        var list = ModelCatalog.Recommended.Select(r => saved.FirstOrDefault(s => s.Id == r.Id) is { } s
            ? r with { Installed = s.Installed, LocalPath = s.LocalPath, Bench = s.Bench, Ability = s.Ability } : r).ToList();
        list.AddRange(saved.Where(s => s.Origin != ModelOrigin.Recommended && list.All(r => r.Id != s.Id)));
        return (list, file?.Active);
    }

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new ModelManifest(_models.Select(m => m with { Active = false }).ToList(), _active), MindJson.Default.ModelManifest));
        File.Move(tmp, _path, overwrite: true);
    }
}
