using System.Globalization;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Autonomy;

namespace Sahar.Core.Mind.Log;

/// <summary>
/// «شو عملت اليوم» for the settings page (mockup 40-today): the lines of a day (today, yesterday) or of the 30 days
/// kept, newest first, each with whether its undo still means something, and the chips' counts. The engine answers
/// the page with this (it knows what can still be undone); the settings app reads the box itself when she is off.
/// </summary>
public static class ActionLogView
{
    public static IReadOnlyList<ActionEntry> Pick(IActionLog log, string? day, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        return day switch
        {
            "yesterday" => log.Day(today.AddDays(-1)),
            "all" => log.All(),
            _ => log.Day(today),
        };
    }

    public static string Outcome(ActionOutcome o) => o switch
    {
        ActionOutcome.Done => "done",
        ActionOutcome.Asked => "asked",
        ActionOutcome.AskedYes => "asked-yes",
        ActionOutcome.AskedNo => "asked-no",
        ActionOutcome.Refused => "refused",
        ActionOutcome.Failed => "failed",
        _ => "undone",
    };

    public static string Json(IReadOnlyList<ActionEntry> entries, Func<ActionEntry, bool> canUndo)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartArray("entries");
            foreach (var e in entries)
            {
                w.WriteStartObject();
                w.WriteString("id", e.Id);
                w.WriteString("at", e.At.ToString("o", CultureInfo.InvariantCulture));
                w.WriteString("time", e.At.ToLocalTime().ToString("H:mm", CultureInfo.InvariantCulture));
                w.WriteString("day", e.At.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteString("tool", e.Tool);
                w.WriteString("summary", e.Summary);
                w.WriteString("source", e.Source == ActSource.User ? "user" : "self");
                if (e.Why is { Length: > 0 } why) w.WriteString("why", why);
                w.WriteString("outcome", Outcome(e.Outcome));
                w.WriteBoolean("undo", e.UndoToken is not null && e.Outcome is ActionOutcome.Done or ActionOutcome.AskedYes && canUndo(e));
                w.WriteNumber("feedback", e.Feedback);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartObject("counts");
            w.WriteNumber("all", entries.Count);
            w.WriteNumber("asked", entries.Count(e => e.Outcome is ActionOutcome.Asked or ActionOutcome.AskedYes or ActionOutcome.AskedNo));
            w.WriteNumber("refused", entries.Count(e => e.Outcome == ActionOutcome.Refused));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>The header fact once a day: «عملت ٣ شغلات اليوم» (what she did, not what she asked or refused).</summary>
    public static int DoneToday(IActionLog log, DateTimeOffset now) =>
        log.Day(DateOnly.FromDateTime(now.ToLocalTime().DateTime)).Count(e => e.Outcome is ActionOutcome.Done or ActionOutcome.AskedYes or ActionOutcome.Undone);

    public static string DoneLine(int n) => n switch
    {
        1 => "عملت شغلة وحدة اليوم",
        2 => "عملت شغلتين اليوم",
        <= 10 => $"عملت {Sahar.Core.Mind.Tools.ToolTemplates.Num(n)} شغلات اليوم",
        _ => $"عملت {Sahar.Core.Mind.Tools.ToolTemplates.Num(n)} شغلة اليوم",
    };
}
