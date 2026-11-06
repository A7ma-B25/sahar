namespace Sahar.Core.Senses;

/// <summary>
/// How long you have been on the app in front, without a real break (spec §8.1 "وقتك على كل برنامج اليوم").
/// A switch shorter than a minute keeps the run; being away pauses it; a new local day starts over.
/// Password managers are never tracked. Kept in memory only.
/// </summary>
public sealed class AppTime
{
    public static readonly TimeSpan ShortSwitch = TimeSpan.FromMinutes(1);

    private string? _exe;
    private DateTimeOffset _since;
    private string? _left;
    private DateTimeOffset _leftAt, _leftRunSince;
    private DateOnly? _lastDay;

    public string? Current => _exe;

    public void Front(string exe, DateTimeOffset now)
    {
        if (PrivacyLists.IsPasswordManager(exe)) { _exe = null; return; }
        if (exe == _exe) return;
        if (exe == _left && now - _leftAt < ShortSwitch)
        {
            // Back after a quick look elsewhere: the run goes on.
            _exe = exe;
            _since = _leftRunSince;
            return;
        }
        if (_exe is not null) { _left = _exe; _leftAt = now; _leftRunSince = _since; }
        _exe = exe;
        _since = now;
    }

    public void Away(DateTimeOffset now)
    {
        _exe = null;
        _left = null;
    }

    public TimeSpan Continuous(DateTimeOffset now)
    {
        if (_exe is null) return TimeSpan.Zero;
        var midnight = new DateTimeOffset(now.Date, now.Offset);
        var from = _since > midnight ? _since : midnight;
        return now > from ? now - from : TimeSpan.Zero;
    }

    /// <summary>True the first time it is asked on a new local day.</summary>
    public bool FirstToday(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.Date);
        if (_lastDay == day) return false;
        _lastDay = day;
        return true;
    }

}
