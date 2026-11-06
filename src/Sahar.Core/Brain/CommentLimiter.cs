namespace Sahar.Core.Brain;

/// <summary>Sahar starts a comment at most once per gap; ignoring her doubles the gap (cap 4 h).</summary>
public sealed class CommentLimiter
{
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(4);

    private DateTimeOffset? _last;

    public TimeSpan Gap { get; private set; } = MinGap;

    public bool TryComment(DateTimeOffset now)
    {
        if (_last is { } last && now - last < Gap) return false;
        _last = now;
        return true;
    }

    public void Dismissed(DateTimeOffset now)
    {
        _last ??= now;
        var doubled = Gap * 2;
        Gap = doubled > MaxGap ? MaxGap : doubled;
    }

    public void Accepted() => Gap = MinGap;
}
