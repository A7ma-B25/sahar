using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Games;

/// <summary>One jump away from you: where to, how long the jump takes, and how long she rests after it.</summary>
public sealed record ChaseJump(Vector2 To, float Seconds, float Rest);

/// <summary>
/// 🏃 الحقني gets catchable (the owner caught her once in six games): she tires as the game goes on. Fresh, she darts
/// far and fast and sideways; after each dart she rests 0.6–1.2 s, still dodging a pointer that comes right onto her
/// while she is fresh; tired, her darts get shorter and slower, she notices you later, and she rests longer and
/// sits still — a typical player catches her in about 10–20 s.
/// </summary>
public sealed class ChaseStamina
{
    public const float TireSeconds = 20f, MinRest = 0.6f, MaxRest = 1.2f;
    /// <summary>Resting, she still dodges a pointer inside this share of her alert radius, times her freshness.</summary>
    public const float RestDodge = 0.7f;
    /// <summary>Her darts turn 0.5–1.1 rad off "straight away from you", so your hand's momentum doesn't carry onto her.</summary>
    public const float MinTurn = 0.5f, MaxTurn = 1.1f;

    private readonly Random _rng;
    private float _restUntil = float.NegativeInfinity;

    public ChaseStamina(Random rng) => _rng = rng;

    public int Flees { get; private set; }

    /// <summary>0 fresh … 1 tired, from the game clock.</summary>
    public static float Fatigue(float clock) => Math.Clamp(clock / TireSeconds, 0f, 1f);
    public float NearScale(float clock) => 1f - 0.45f * Fatigue(clock);
    public float JumpScale(float clock) => 1f - 0.5f * Fatigue(clock);
    public float MoveSeconds(float clock) => 0.28f + 0.22f * Fatigue(clock);
    public bool Resting(float clock) => clock < _restUntil;

    /// <summary>Call when she is standing (not mid-jump): a jump away from the pointer, or null to stay.</summary>
    public ChaseJump? Flee(float clock, Vector2 her, Vector2 pointer, Vector2 body, RectF work, float dpi)
    {
        float f = Fatigue(clock);
        float near = Resting(clock) ? RestDodge * (1f - f) : NearScale(clock);
        if (near <= 0) return null;
        if (Chase.Flee(her, pointer, body, work, dpi, _rng, near, JumpScale(clock), MinTurn, MaxTurn) is not { } to) return null;
        float seconds = MoveSeconds(clock);
        float rest = Math.Clamp(MinRest + (MaxRest - MinRest) * f + ((float)_rng.NextDouble() - 0.5f) * 0.2f, MinRest, MaxRest);
        _restUntil = clock + seconds + rest;
        Flees++;
        return new ChaseJump(to, seconds, rest);
    }
}
