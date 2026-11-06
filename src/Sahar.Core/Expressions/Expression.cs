using Sahar.Core.Art;

namespace Sahar.Core.Expressions;

public enum Eyes { Open, Happy, Closed, Focus, Sparkle, Mystic, Worried, Wide, Look, Up, Wink, Half, Dizzy, Heart, Sad, Scan }

public enum Mouth { Smile, Grin, Flat, O, Sleepy, Mystic, Wobble, BigO, Frown, Tongue, Puff, Sing, Yawn, None }

[Flags]
public enum Extras
{
    None = 0, Headphones = 1, Notes = 2, Steam = 4, Hearts = 8, Rain = 16, Snow = 32, Confetti = 64,
    Zzz = 128, Sweat = 256, Stars = 512, Squish = 1024, Shiver = 2048, Grounded = 4096,
}

/// <summary>One of Sahar's expressions: mood colours, face, what her moon shows, and extras.</summary>
public sealed record ExpressionPreset(
    int Id, string Key, Mood Mood, Eyes Eyes, Mouth Mouth, string? OrbGlyph, Rgba? OrbTop, Rgba? OrbBottom, Extras Extras);
