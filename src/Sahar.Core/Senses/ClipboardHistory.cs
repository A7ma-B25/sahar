using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Senses;

public sealed record ClipItem(ClipKind Kind, string? Text, string? ImageFile, int Width, int Height, DateTimeOffset At);

/// <summary>
/// The last 20 things you copied (spec §7.2), on this PC only: clipboard.json plus a .bmp per image.
/// Consecutive duplicates collapse. Never throws; a bad file goes to ".bad", an unreadable one is never overwritten.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 20, MaxTextChars = 100_000, MaxImageBytes = 16 * 1024 * 1024;

    private readonly string _folder;
    private readonly List<ClipItem> _items;   // oldest first
    private bool _unread;

    public ClipboardHistory(string folder)
    {
        _folder = folder;
        _items = Load(out _unread);
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar");

    private string FilePath => Path.Combine(_folder, "clipboard.json");

    public IReadOnlyList<ClipItem> Items => Enumerable.Reverse(_items).ToList();

    public bool AddText(string text, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextChars) return false;
        if (_items.Count > 0 && _items[^1].Text == text) return false;
        return Add(new ClipItem(ClipboardRules.KindOf(text), text, null, 0, 0, at));
    }

    /// <param name="dib">The clipboard's CF_DIB bytes (BITMAPINFOHEADER + pixels).</param>
    public bool AddImage(byte[] dib, int w, int h, DateTimeOffset at)
    {
        if (dib.Length == 0 || dib.Length > MaxImageBytes) return false;
        try
        {
            Directory.CreateDirectory(_folder);
            var file = Path.Combine(_folder, $"clip-{at.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.bmp");
            using (var f = File.Create(file))
            {
                // BITMAPFILEHEADER: "BM", size, 0, 0, offset to pixels (14 + header + palette; header size is the DIB's first DWORD).
                int headerSize = dib.Length >= 4 ? BitConverter.ToInt32(dib, 0) : 40;
                var fh = new byte[14];
                fh[0] = (byte)'B'; fh[1] = (byte)'M';
                BitConverter.GetBytes(14 + dib.Length).CopyTo(fh, 2);
                BitConverter.GetBytes(14 + headerSize).CopyTo(fh, 10);
                f.Write(fh);
                f.Write(dib);
            }
            return Add(new ClipItem(ClipKind.Image, null, file, w, h, at));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public void Clear()
    {
        foreach (var i in _items) DeleteImage(i);
        _items.Clear();
        if (!_unread) Save();
    }

    private bool Add(ClipItem item)
    {
        if (_unread)
        {
            var onDisk = Load(out _unread);
            if (!_unread) _items.InsertRange(0, onDisk);
        }
        _items.Add(item);
        while (_items.Count > MaxItems) { DeleteImage(_items[0]); _items.RemoveAt(0); }
        if (!_unread) Save();
        return true;
    }

    private static void DeleteImage(ClipItem i)
    {
        if (i.ImageFile is null) return;
        try { File.Delete(i.ImageFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private List<ClipItem> Load(out bool unread)
    {
        unread = false;
        string text;
        try
        {
            if (!File.Exists(FilePath)) return [];
            text = File.ReadAllText(FilePath);
        }
        catch (IOException) { unread = true; return []; }
        catch (UnauthorizedAccessException) { unread = true; return []; }
        try
        {
            return JsonSerializer.Deserialize(text, ClipJson.Default.ListClipItem)?.Where(i => i is not null).ToList() ?? [];
        }
        catch (JsonException)
        {
            try { File.WriteAllText(FilePath + ".bad", text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, ClipJson.Default.ListClipItem));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, destinationBackupFileName: null);
            else File.Move(tmp, FilePath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<ClipItem>))]
internal sealed partial class ClipJson : JsonSerializerContext;
