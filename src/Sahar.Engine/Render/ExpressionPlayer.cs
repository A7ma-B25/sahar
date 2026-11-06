using System.Numerics;
using Sahar.Core.Expressions;
using Windows.UI.Composition;
using ExtrasFlags = Sahar.Core.Expressions.Extras;

namespace Sahar.Engine.Render;

/// <summary>
/// Plays one of the 25 expressions: mood colours and face cross-fade (250 ms), the moon gives way to the orb
/// with the event's glyph, extras start, and body motions (squish, shiver, grounded) replace the idle bob.
/// </summary>
internal sealed class ExpressionPlayer
{
    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(250);
    public const float GroundedDip = 12f;
    public const float ShiverDip = 2f;

    private readonly SaharVisual _v;
    private readonly Extras _extras;
    private SwapSurface? _orbSurface;
    private SpriteVisual? _orbSprite;

    public ExpressionPreset Current { get; private set; } = ExpressionCatalog.Get("idle");
    /// <summary>True while a motion keeps her still (sitting, sleeping, dragged): no idle bob.</summary>
    public Func<bool>? Still { get; set; }

    public ExpressionPlayer(SaharVisual v)
    {
        _v = v;
        _extras = new Extras(v);
        _v.Orb.Opacity = 0;
    }

    public void Play(ExpressionPreset p)
    {
        bool orbChanged = p.OrbGlyph != Current.OrbGlyph || p.OrbTop != Current.OrbTop;
        Current = p;
        _v.SetMood(p.Mood, Fade);
        _v.SetFace(p.Eyes, p.Mouth, Fade);
        if (orbChanged) ShowOrb(fade: true);
        _extras.Set(p.Extras);
        ApplyMotion();
    }

    /// <summary>Re-applies the current expression after a size, DPI or device change (pixel values changed).</summary>
    public void Reapply()
    {
        ShowOrb(fade: false);
        _extras.Rebuild();
        ApplyMotion();
    }

    private void ShowOrb(bool fade)
    {
        var c = _v.Root.Compositor;
        bool show = Current.OrbGlyph != null;
        if (show)
        {
            var glyph = Current.OrbGlyph!;
            var top = Current.OrbTop!.Value;
            var bottom = Current.OrbBottom ?? top;
            float s = _v.Scale;
            var size = _v.SmallPx(OrbRenderer.Units);
            if (_orbSurface == null || _orbSurface.Size != size)
            {
                _orbSurface?.Dispose();
                _orbSurface = new SwapSurface(_v.Host, size);
            }
            _orbSurface.DrawCurrent(dc => OrbRenderer.Draw(dc, s, glyph, top, bottom));
            if (_orbSprite == null)
            {
                _orbSprite = c.CreateSpriteVisual();
                _orbSprite.AnchorPoint = new Vector2(0.5f);
                _v.Orb.Children.InsertAtTop(_orbSprite);
            }
            _orbSprite.Brush = _orbSurface.Brushes[_orbSurface.Current];
            _orbSprite.Size = new Vector2(size.Width, size.Height);
            var at = OrbRenderer.Centre * s;
            _orbSprite.Offset = new Vector3(at, 0);
            // The orb bobs 6 units over 3.2 s.
            _orbSprite.StartAnimation("Offset.Y", IdleAnimations.Loop(c, 3.2, (0f, at.Y), (0.5f, at.Y - 6 * s), (1f, at.Y)));
        }
        Animate(_v.Orb, "Opacity", show ? 1f : 0f, fade);
        Animate(_v.MoonProps, "moon", show ? 0f : 1f, fade);
    }

    private void ApplyMotion()
    {
        var c = _v.Root.Compositor;
        var x = Current.Extras;
        var ch = _v.Character;
        float dip = _v.DpiScale;

        if (x.HasFlag(ExtrasFlags.Grounded))
        {
            ch.StopAnimation("Offset.Y");
            Animate(ch, "Offset.Y", GroundedDip * dip, fade: true);
        }
        else if (Still?.Invoke() != true) IdleAnimations.StartBob(_v);

        if (x.HasFlag(ExtrasFlags.Squish))
            ch.StartAnimation("Scale", IdleAnimations.Loop3(c, 2.4,
                (0f, Vector3.One), (0.12f, new Vector3(1.14f, 0.86f, 1)), (0.2f, new Vector3(0.94f, 1.08f, 1)),
                (0.25f, Vector3.One), (1f, Vector3.One)));
        else { ch.StopAnimation("Scale"); ch.Scale = Vector3.One; }

        if (x.HasFlag(ExtrasFlags.Shiver))
            ch.StartAnimation("Offset.X", IdleAnimations.Loop(c, 0.3,
                (0f, 0f), (0.25f, -ShiverDip * dip), (0.75f, ShiverDip * dip), (1f, 0f)));
        else { ch.StopAnimation("Offset.X"); ch.Offset = ch.Offset with { X = 0 }; }
    }

    private static void Animate(CompositionObject target, string property, float to, bool fade)
    {
        var c = target.Compositor;
        if (!fade)
        {
            target.StopAnimation(property);
            if (target is CompositionPropertySet ps) ps.InsertScalar(property, to);
            else if (property == "Opacity" && target is Visual v) v.Opacity = to;
            else if (property == "Offset.Y" && target is Visual w) w.Offset = w.Offset with { Y = to };
            return;
        }
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, to);
        a.Duration = Fade;
        target.StartAnimation(property, a);
    }
}
