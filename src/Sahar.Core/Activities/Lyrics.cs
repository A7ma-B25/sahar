using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sahar.Core.Activities;

/// <summary>Synced lyrics in LRC ("[mm:ss.xx] line").</summary>
public static partial class Lrc
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex Stamp();

    public static IReadOnlyList<(TimeSpan At, string Line)> Parse(string text)
    {
        var list = new List<(TimeSpan, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var stamps = Stamp().Matches(raw);
            if (stamps.Count == 0) continue;
            var line = raw[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            foreach (Match m in stamps)
            {
                double sec = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                list.Add((TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) + TimeSpan.FromSeconds(sec), line));
            }
        }
        return list.OrderBy(l => l.Item1).ToList();
    }

    /// <summary>The line before and the line at <paramref name="t"/> (the card shows both).</summary>
    public static (string? Previous, string? Current) At(IReadOnlyList<(TimeSpan At, string Line)> lines, TimeSpan t)
    {
        int i = -1;
        while (i + 1 < lines.Count && lines[i + 1].At <= t) i++;
        return i < 0 ? (null, null) : (i > 0 ? lines[i - 1].Line : null, lines[i].Line);
    }
}

public enum LyricsStatus { Off, None, Found, Failed }

public sealed record LyricsResult(LyricsStatus Status, IReadOnlyList<(TimeSpan At, string Line)> Lines)
{
    public static LyricsResult Of(LyricsStatus s) => new(s, []);
}

/// <summary>
/// Lyrics from lrclib.net only (spec 0.13 §2): nothing goes out unless «الكلمات» is on and you said yes to the inline
/// question; only the song's title, artist, album and length are sent, with a generic User-Agent. Answers are cached in
/// <c>%LOCALAPPDATA%\Sahar\lyrics\</c> under a hash of the song, "none" too; a failed request is not cached.
/// </summary>
public sealed class LyricsClient(HttpMessageHandler handler, string cacheDir)
{
    public const string Host = "lrclib.net";
    private readonly HttpClient _http = MakeClient(handler);

    private static HttpClient MakeClient(HttpMessageHandler h)
    {
        var c = new HttpClient(h, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return c;
    }

    public async Task<LyricsResult> GetAsync(string title, string artist, string? album, TimeSpan duration, bool enabled, bool consented)
    {
        if (!enabled || !consented || string.IsNullOrWhiteSpace(title)) return LyricsResult.Of(LyricsStatus.Off);
        int seconds = (int)Math.Round(duration.TotalSeconds);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{artist}\n{title}\n{album}\n{seconds}")));
        var file = Path.Combine(cacheDir, key);
        try
        {
            if (File.Exists(file + ".none")) return LyricsResult.Of(LyricsStatus.None);
            if (File.Exists(file + ".lrc")) return new LyricsResult(LyricsStatus.Found, Lrc.Parse(await File.ReadAllTextAsync(file + ".lrc")));
        }
        catch (IOException) { }
        string? lrc;
        try
        {
            var q = $"artist_name={E(artist)}&track_name={E(title)}{(album is { Length: > 0 } ? "&album_name=" + E(album) : "")}" +
                (seconds > 0 ? $"&duration={seconds}" : "");
            lrc = Synced(await GetOrNull($"https://{Host}/api/get?{q}"));
            if (lrc is null)
            {
                var found = await GetOrNull($"https://{Host}/api/search?track_name={E(title)}&artist_name={E(artist)}");
                lrc = found is null ? null : FirstSynced(found);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return LyricsResult.Of(LyricsStatus.Failed);
        }
        try
        {
            Directory.CreateDirectory(cacheDir);
            if (lrc is null) await File.WriteAllTextAsync(file + ".none", "");
            else await File.WriteAllTextAsync(file + ".lrc", lrc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return lrc is null ? LyricsResult.Of(LyricsStatus.None) : new LyricsResult(LyricsStatus.Found, Lrc.Parse(lrc));
    }

    private static string E(string s) => Uri.EscapeDataString(s.Trim());

    private async Task<string?> GetOrNull(string url)
    {
        using var r = await _http.GetAsync(url);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync();
    }

    private static string? Synced(string? json)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("syncedLyrics", out var s)
            && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } t ? t : null;
    }

    private static string? FirstSynced(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } t) return t;
        return null;
    }
}
