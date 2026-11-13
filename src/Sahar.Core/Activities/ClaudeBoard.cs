namespace Sahar.Core.Activities;

/// <summary>One Claude Code session as she sees it.</summary>
public sealed record ClaudeSession(string Id, string Project, ClaudeStep Step, string? Target, IReadOnlyList<ClaudeTodo> Todos,
    DateTimeOffset? Started, bool Asking, DateTimeOffset Last);

/// <summary>
/// Every Claude Code session from its hooks (spec 0.13 §2, owner ruling 3): working (the step, minutes since the turn
/// began), asking (until that same session moves on), and a done moment on Stop. Sessions are separate: one session's
/// Stop never clears another's ask. A session silent for 30 min is gone (Claude closed without SessionEnd).
/// </summary>
public sealed class ClaudeBoard
{
    public static readonly TimeSpan Silent = TimeSpan.FromMinutes(30);
    private readonly Dictionary<string, ClaudeSession> _sessions = [];
    private readonly List<Activity> _moments = [];

    public IReadOnlyCollection<ClaudeSession> Sessions => _sessions.Values;
    public bool AskingNow => _sessions.Values.Any(s => s.Asking);

    public void Apply(ClaudeSignal s, DateTimeOffset now)
    {
        if (s.Event == "SessionEnd") { _sessions.Remove(s.Session); return; }
        var cur = _sessions.TryGetValue(s.Session, out var c) ? c : new ClaudeSession(s.Session, s.Project, ClaudeStep.None, null, [], null, false, now);
        cur = cur with { Project = s.Project.Length > 0 ? s.Project : cur.Project, Last = now, Todos = s.Todos ?? cur.Todos };
        switch (s.Event)
        {
            case "UserPromptSubmit":
                cur = cur with { Started = now, Asking = false, Step = ClaudeStep.None, Target = null };
                break;
            case "PreToolUse":
                cur = cur with { Started = cur.Started ?? now, Asking = false, Step = s.Step, Target = s.Target };
                break;
            case "PostToolUse" or "PostToolUseFailure":
                cur = cur with { Started = cur.Started ?? now, Asking = false };
                break;
            case "PermissionRequest" or "Notification" when s.Asking:
                cur = cur with { Started = cur.Started ?? now, Asking = true };
                break;
            case "Stop":
                if (cur.Started is { } started)
                    _moments.Add(new Activity("claude-done:" + s.Session, ActivityKind.ClaudeDone, ActivityText.ClaudeDone, ActivityText.Duration(now - started), "check"));
                cur = cur with { Started = null, Asking = false, Step = ClaudeStep.None, Target = null };
                break;
        }
        _sessions[s.Session] = cur;
    }

    /// <summary>Done moments since the last call (each once).</summary>
    public IReadOnlyList<Activity> TakeMoments()
    {
        var list = _moments.ToList();
        _moments.Clear();
        return list;
    }

    public IReadOnlyList<Activity> Activities(DateTimeOffset now)
    {
        foreach (var gone in _sessions.Values.Where(s => now - s.Last > Silent).Select(s => s.Id).ToList()) _sessions.Remove(gone);
        var list = new List<Activity>();
        foreach (var s in _sessions.Values.OrderBy(s => s.Started))
        {
            if (s.Asking) list.Add(new Activity("claude-ask:" + s.Id, ActivityKind.ClaudeAsk, ActivityText.AskTag, null, "bell", Asking: true));
            else if (s.Started is { } started)
                list.Add(new Activity("claude:" + s.Id, ActivityKind.ClaudeWorking, ActivityText.ClaudeStep(s.Step), ActivityText.Duration(now - started), "terminal"));
        }
        return list;
    }
}
