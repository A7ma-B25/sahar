using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Tools;

public sealed record Note(string Text, DateTimeOffset At);

/// <summary>
/// Her quick notes (spec §7.1), kept in notes.json on this PC only. One line each, at most 500 characters,
/// the newest 50 kept. Never throws: a bad file is copied to ".bad" and she starts empty; a failed save keeps
/// the note in memory (<see cref="SaveFailed"/>).
/// </summary>
public sealed class NotesStore
{
    public const int MaxLength = 500, MaxNotes = 50;

    private readonly string _path;
    private readonly List<Note> _notes;
    private bool _unread;   // the file exists but could not be read: never overwrite it

    public NotesStore(string path)
    {
        _path = path;
        _notes = Load(path, out _unread);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar", "notes.json");

    public bool SaveFailed { get; private set; }

    public IReadOnlyList<Note> Latest(int n)
    {
        var list = new List<Note>(Math.Min(n, _notes.Count));
        for (int i = _notes.Count - 1; i >= 0 && list.Count < n; i--) list.Add(_notes[i]);
        return list;
    }

    public bool Add(string text, DateTimeOffset now)
    {
        var clean = Clean(text);
        if (clean.Length == 0) return false;
        if (_unread)
        {
            // Try again; if the file can be read now, keep its notes and add ours after them.
            var onDisk = Load(_path, out _unread);
            if (!_unread) _notes.InsertRange(0, onDisk);
        }
        _notes.Add(new Note(clean, now));
        if (_notes.Count > MaxNotes) _notes.RemoveRange(0, _notes.Count - MaxNotes);
        if (_unread) { SaveFailed = true; return true; }   // kept in memory for now
        Save();
        return true;
    }

    /// <summary>One line: every run of whitespace becomes one space; trimmed; cut at 500 characters (not inside a surrogate pair).</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch)) { space = true; continue; }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(ch);
        }
        if (sb.Length <= MaxLength) return sb.ToString();
        int cut = char.IsHighSurrogate(sb[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return sb.ToString(0, cut);
    }

    private static List<Note> Load(string path, out bool unread)
    {
        unread = false;
        string text;
        try
        {
            if (!File.Exists(path)) return [];
            text = File.ReadAllText(path);
        }
        catch (IOException) { unread = true; return []; }
        catch (UnauthorizedAccessException) { unread = true; return []; }
        try
        {
            return JsonSerializer.Deserialize(text, NotesJson.Default.ListNote)?.Where(n => n?.Text is { Length: > 0 }).ToList() ?? [];
        }
        catch (JsonException)
        {
            try { File.WriteAllText(path + ".bad", text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_notes, NotesJson.Default.ListNote));
            if (File.Exists(_path)) File.Replace(tmp, _path, destinationBackupFileName: null);
            else File.Move(tmp, _path);
            SaveFailed = false;
        }
        catch (IOException) { SaveFailed = true; }
        catch (UnauthorizedAccessException) { SaveFailed = true; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(List<Note>))]
internal sealed partial class NotesJson : JsonSerializerContext;
