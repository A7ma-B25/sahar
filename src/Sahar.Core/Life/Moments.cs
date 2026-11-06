using Sahar.Core.Expressions;

namespace Sahar.Core.Life;

/// <summary>Small face moments she has on her own.</summary>
public enum MomentKind { Wink, Smile, LookAround, Drowsy, Giggle }

public static class Moments
{
    public static (Eyes Eyes, Mouth Mouth, float Seconds) Face(MomentKind k) => k switch
    {
        MomentKind.Wink => (Eyes.Wink, Mouth.Smile, 1.6f),
        MomentKind.Smile => (Eyes.Happy, Mouth.Grin, 2f),
        MomentKind.LookAround => (Eyes.Look, Mouth.Flat, 2.4f),
        MomentKind.Drowsy => (Eyes.Half, Mouth.Yawn, 2.6f),
        _ => (Eyes.Happy, Mouth.Grin, 1.8f),   // Giggle (with a squish)
    };
}
