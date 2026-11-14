using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Mind.Tools;

/// <summary>One reminder: his words (encrypted at rest), when, and whether she set it on her own.</summary>
public sealed record Reminder(string Id, string Text, DateTimeOffset Due, bool Self);

/// <summary>
/// Her reminders (reminder.set), kept in the encrypted box so they survive a restart. One she set on her own speaks
/// only when the initiative gate is open (it waits otherwise); one he asked for speaks on time, like his timer.
/// Never throws: a box that cannot be read or written starts empty or keeps them in memory.
/// </summary>
public sealed class ReminderBook(ISecretBox box, Func<DateTimeOffset> now)
{
    public const string File = "reminders";
    private List<Reminder>? _list;

    public IReadOnlyList<Reminder> Pending() => List().OrderBy(r => r.Due).ToList();

    public IReadOnlyList<Reminder> Due() => List().Where(r => r.Due <= now()).OrderBy(r => r.Due).ToList();

    public Reminder Add(string text, DateTimeOffset due, bool self)
    {
        var r = new Reminder(Guid.NewGuid().ToString("n"), text, due, self);
        List().Add(r);
        Save();
        return r;
    }

    public bool Remove(string id)
    {
        if (List().RemoveAll(r => r.Id == id) == 0) return false;
        Save();
        return true;
    }

    public bool Has(string id) => List().Any(r => r.Id == id);

    public static bool MaySpeak(Reminder r, bool gateOpen) => !r.Self || gateOpen;

    private List<Reminder> List()
    {
        if (_list is not null) return _list;
        try
        {
            var bytes = box.Read(File);
            _list = bytes is { Length: > 0 } ? JsonSerializer.Deserialize(bytes, ReminderJson.Default.ListReminder) ?? [] : [];
        }
        catch (Exception) { _list = []; }
        return _list;
    }

    private void Save()
    {
        try { box.Write(File, JsonSerializer.SerializeToUtf8Bytes(List(), ReminderJson.Default.ListReminder)); }
        catch (Exception) { /* kept in memory until the next save */ }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<Reminder>))]
public sealed partial class ReminderJson : JsonSerializerContext;
