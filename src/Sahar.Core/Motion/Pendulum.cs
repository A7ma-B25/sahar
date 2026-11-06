namespace Sahar.Core.Motion;

/// <summary>Sway of the leaf/body while moving. Angle in degrees; positive leans right.</summary>
public sealed class Pendulum
{
    public const float Damping = 3.0f;
    public const float Stiffness = 30f;
    public const float MaxAngle = 25f;
    /// <summary>Degrees/s² of swing per px/s² of pivot acceleration.</summary>
    public const float Coupling = 0.01f;

    private float _velocity;

    public float Angle { get; private set; }

    public void Step(float dt, float pivotAccelX)
    {
        var accel = -Stiffness * Angle - Damping * _velocity - Coupling * pivotAccelX;
        _velocity += accel * dt;
        Angle += _velocity * dt;
        if (MathF.Abs(Angle) > MaxAngle) { Angle = MathF.CopySign(MaxAngle, Angle); _velocity = 0; }
    }
}
