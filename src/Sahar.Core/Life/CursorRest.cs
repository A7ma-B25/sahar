using System.Numerics;

namespace Sahar.Core.Life;

/// <summary>Is the mouse pointer resting near her (still for 2 s, within 300 DIP)? Then she may circle it.</summary>
public sealed class CursorRest
{
    public static readonly TimeSpan Still = TimeSpan.FromSeconds(2);
    public const float NearDip = 300f, JitterDip = 4f;

    private Vector2? _anchor;
    private DateTimeOffset _since;

    /// <summary>Positions in physical pixels; <paramref name="dpiScale"/> turns DIPs into pixels.</summary>
    public bool Update(DateTimeOffset now, Vector2 cursor, Vector2 herCentre, float dpiScale)
    {
        if (_anchor is not { } a || Vector2.Distance(a, cursor) > JitterDip * dpiScale)
        {
            _anchor = cursor;
            _since = now;
        }
        return now - _since >= Still && Vector2.Distance(cursor, herCentre) <= NearDip * dpiScale;
    }
}
