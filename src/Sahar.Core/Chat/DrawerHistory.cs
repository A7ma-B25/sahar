using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Chat;

/// <summary>One line of «الدرج»'s «شو صار اليوم».</summary>
public sealed record DrawerItem(DateTimeOffset At, string Text, string Icon);

/// <summary>
/// «الدرج»'s history across restarts (owner, 0.16 review): the last 24 h (40 lines at most), sealed in her personal
/// SecretBox like the chat (AES-GCM, DPAPI key) as <c>drawer-history</c>, so «انسي كل شي» shreds it with the rest.
/// A box that refuses keeps the session going in memory. Never logs.
/// </summary>
public sealed class DrawerHistory(ISecretBox box, Func<DateTimeOffset> now)
{
    public const string FileName = "drawer-history";
    public const int Max = 40;
    public static readonly TimeSpan Keep = TimeSpan.FromHours(24);
    private List<DrawerItem>? _items;

    /// <summary>Newest first, nothing older than 24 h.</summary>
    public IReadOnlyList<DrawerItem> Items
    {
        get
        {
            var cut = now() - Keep;
            return [.. All().Where(i => i.At >= cut)];
        }
    }

    private List<DrawerItem> All()
    {
        if (_items is not null) return _items;
        _items = [];
        try
        {
            if (box.Read(FileName) is { } data) _items = JsonSerializer.Deserialize(data, DrawerJson.Default.ListDrawerItem) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return _items;
    }

    public void Add(string text, string icon)
    {
        var all = All();
        all.Insert(0, new DrawerItem(now(), text, icon));
        var cut = now() - Keep;
        all.RemoveAll(i => i.At < cut);
        if (all.Count > Max) all.RemoveRange(Max, all.Count - Max);
        try { box.Write(FileName, JsonSerializer.SerializeToUtf8Bytes(all, DrawerJson.Default.ListDrawerItem)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }   // this session only
    }

    /// <summary>«انسي كل شي»: the box was shredded; the session copy goes too.</summary>
    public void Forget() => _items = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<DrawerItem>))]
internal sealed partial class DrawerJson : JsonSerializerContext;
