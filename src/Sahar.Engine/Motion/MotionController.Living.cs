using System.Numerics;
using Sahar.Core.Expressions;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.UI.Composition;

namespace Sahar.Engine.Motion;

/// <summary>
/// Motions 28–36 (0.8): she lives on the screen — walks on the taskbar, stretches, yawns, scratches her head, watches
/// your window, types along, hops along a window, tosses her moon, chases a butterfly.
/// </summary>
internal sealed partial class MotionController
{
    private static readonly Windows.UI.Color MoonColor = Windows.UI.Color.FromArgb(255, 0xFF, 0xE8, 0x9A);

    private bool PlayLiving(MotionKind k)
    {
        switch (k)
        {
            case MotionKind.WalkTaskbar: Reset(k); StartWalkTaskbar(); break;
            case MotionKind.Stretch: Reset(k); StartStretch(); break;
            case MotionKind.Yawn: Reset(k); StartYawn(); break;
            case MotionKind.ScratchHead: Reset(k); StartScratchHead(); break;
            case MotionKind.WatchYou: Reset(k); StartWatchYou(); break;
            case MotionKind.TypeAlong: Reset(k); StartTypeAlong(() => false); break;
            case MotionKind.HopWindows: Reset(k); StartHopWindows(); break;
            case MotionKind.MoonToss: Reset(k); StartMoonToss(); break;
            case MotionKind.ChaseButterfly: Reset(k); StartChaseButterfly(); break;
            default: return false;
        }
        return true;
    }

    // ---------- 28 WalkTaskbar ----------

    /// <summary>She lands on the taskbar's edge, walks a few steps with a bob, sits a moment, then floats up.</summary>
    private void StartWalkTaskbar()
    {
        var m = Monitor;
        var (_, edge, _) = Taskbar.Get(m.Handle);
        float dip = m.DpiScale, along = Math.Clamp(AlongBar(), 0.15f, 0.85f);
        var start = PerchTopLeft(along);
        bool horizontal = edge is not (Edge.Left or Edge.Right);
        var work = m.WorkF;
        float toward = horizontal
            ? (start.X + BodySize.X / 2 < work.X + work.Width / 2 ? 1 : -1)
            : (start.Y + BodySize.Y / 2 < work.Y + work.Height / 2 ? 1 : -1);
        var stride = (horizontal ? new Vector2(26, 0) : new Vector2(0, 26)) * dip * toward;
        int steps = _rng.Next(8, 15);
        HoldStill();
        _v.Root.CenterPoint = PivotBottom;
        int taken = 0;
        void Step()
        {
            if (taken == steps)
            {
                // A rest on the bar: settles down, a content face, then up again.
                Squash(horizontal: false);
                _face(Eyes.Happy, Mouth.Smile, 3f);
                Wait(3f, () => Play(MotionKind.Hover));
                return;
            }
            var from = TopLeft;
            var to = Clamp(from + stride);
            taken++;
            float t = 0;
            _step = dt =>
            {
                t = MathF.Min(1, t + dt / 0.35f);
                TopLeft = Vector2.Lerp(from, to, t);
                float bob = MathF.Sin(MathF.PI * t);
                _v.Root.Scale = new Vector3(1 + 0.04f * (1 - bob), 0.96f + 0.08f * bob, 1);   // a step: squash, then lift
                _v.Root.RotationAngleInDegrees = 4 * toward * (taken % 2 == 0 ? bob : -bob);
                if (t < 1) return true;
                _v.Root.Scale = Vector3.One;
                _v.Root.RotationAngleInDegrees = 0;
                if (to == from) { taken = steps; }   // reached the edge: rest here
                Step();
                return true;
            };
        }
        _step = Travel(start, 0.8f, EaseIn, done: () => { Squash(horizontal: false); Wait(0.3f, Step); });
    }

    // ---------- 29 Stretch, 30 Yawn ----------

    /// <summary>A cat stretch: long and low with closed eyes, held, then back with a little shake.</summary>
    private void StartStretch()
    {
        var c = _v.Root.Compositor;
        var stretch = c.CreateVector3KeyFrameAnimation();
        stretch.InsertKeyFrame(0.3f, new Vector3(1.18f, 0.84f, 1));
        stretch.InsertKeyFrame(0.7f, new Vector3(1.18f, 0.84f, 1));
        stretch.InsertKeyFrame(0.8f, new Vector3(0.95f, 1.06f, 1));
        stretch.InsertKeyFrame(0.9f, new Vector3(1.03f, 0.98f, 1));
        stretch.InsertKeyFrame(1f, Vector3.One);
        stretch.Duration = TimeSpan.FromSeconds(2.4);
        _v.Character.CenterPoint = new Vector3(BodyOffset.X + BodySize.X / 2, BodyOffset.Y + BodySize.Y, 0);
        _v.Character.StartAnimation("Scale", stretch);
        BodyScaleBusy = true;
        _face(Eyes.Closed, Mouth.Flat, 1.8f);
        Wait(2.5f, () => { BodyScaleBusy = false; _player.Reapply(); Play(MotionKind.Hover); });
    }

    /// <summary>A yawn: half-closed eyes, a wide mouth, and a little upward stretch.</summary>
    private void StartYawn()
    {
        var c = _v.Root.Compositor;
        var up = c.CreateVector3KeyFrameAnimation();
        up.InsertKeyFrame(0.4f, new Vector3(0.96f, 1.08f, 1));
        up.InsertKeyFrame(0.75f, new Vector3(0.96f, 1.08f, 1));
        up.InsertKeyFrame(1f, Vector3.One);
        up.Duration = TimeSpan.FromSeconds(1.9);
        _v.Character.CenterPoint = new Vector3(BodyOffset.X + BodySize.X / 2, BodyOffset.Y + BodySize.Y, 0);
        _v.Character.StartAnimation("Scale", up);
        BodyScaleBusy = true;
        _face(Eyes.Half, Mouth.Yawn, 1.8f);
        Wait(2.2f, () => { BodyScaleBusy = false; _player.Reapply(); Play(MotionKind.Hover); });
    }

    // ---------- 31 ScratchHead ----------

    /// <summary>Puzzled: she looks up and wiggles her head (her leaf scratching it) three quick times.</summary>
    private void StartScratchHead()
    {
        var c = _v.Root.Compositor;
        var wiggle = c.CreateScalarKeyFrameAnimation();
        float[] angles = [0, 9, 3, 9, 3, 9, 0];
        for (int i = 0; i < angles.Length; i++) wiggle.InsertKeyFrame(i / (angles.Length - 1f), angles[i]);
        wiggle.Duration = TimeSpan.FromSeconds(1.4);
        _v.Character.CenterPoint = PivotLeaf;
        _v.Character.StartAnimation("RotationAngleInDegrees", wiggle);
        _face(Eyes.Up, Mouth.Flat, 2f);
        Wait(2.2f, () => Play(MotionKind.Hover));
    }

    // ---------- 32 WatchYou, 33 TypeAlong ----------

    /// <summary>A spot beside the front window (outside it, never over it): its top or bottom corner, the side with room.</summary>
    private Vector2? BesideWindow(bool bottom)
    {
        var (hwnd, w, _, _) = ForegroundInfo.Current();
        if (hwnd == 0 || Win.Own.Contains(hwnd) || Native.IsZoomed(hwnd) != 0 || w.Right <= w.Left) return null;
        var work = Monitor.WorkF;
        float gap = 10 * Monitor.DpiScale;
        float y = bottom ? w.Bottom - BodySize.Y - 20 * Monitor.DpiScale : w.Top + 30 * Monitor.DpiScale;
        if (w.Right + gap + BodySize.X <= work.Right) return Clamp(new Vector2(w.Right + gap, y));
        if (w.Left - gap - BodySize.X >= work.X) return Clamp(new Vector2(w.Left - gap - BodySize.X, y));
        return null;   // no room beside it
    }

    /// <summary>She goes beside your window and watches it for a while, leaning toward it.</summary>
    private void StartWatchYou()
    {
        if (BesideWindow(bottom: false) is not { } spot) { StartCurious(); return; }
        var (_, w, _, _) = ForegroundInfo.Current();
        float lean = (w.Left + w.Right) / 2f < spot.X ? -TiltDegrees : TiltDegrees;
        _v.Root.CenterPoint = PivotBottom;
        _step = Travel(spot, 1.1f, Smooth, done: () =>
        {
            _v.Root.RotationAngleInDegrees = lean;
            _face(Eyes.Look, Mouth.Smile, 6f);
            Wait(6f, () => Play(MotionKind.Hover));
        });
    }

    /// <summary>"بتكتب معك": while you type she sits beside your window's bottom corner and taps along (max 20 s).</summary>
    public void TypeAlong(Func<bool> typing)
    {
        Reset(MotionKind.TypeAlong);
        StartTypeAlong(typing);
        UpdateTicker();
    }

    private void StartTypeAlong(Func<bool> typing)
    {
        var spot = BesideWindow(bottom: true) ?? TopLeft;
        HoldStill();
        _v.Root.CenterPoint = PivotBottom;
        _step = Travel(spot, 0.9f, Smooth, done: () =>
        {
            float t = 0, idle = 0;
            _step = dt =>
            {
                t += dt;
                idle = typing() ? 0 : idle + dt;
                // Quick little taps: a fast squash bob, like fingers on keys.
                float tap = MathF.Abs(MathF.Sin(t * MathF.PI / 0.12f));
                _v.Root.Scale = new Vector3(1 + 0.02f * tap, 1 - 0.04f * tap, 1);
                if (((int)(t / 1.5f)) != ((int)((t - dt) / 1.5f))) _face(Eyes.Focus, Mouth.Flat, 1.6f);
                if (t < 20 && idle < 1.2f) return true;
                _v.Root.Scale = Vector3.One;
                _step = null;
                Play(MotionKind.Hover);
                return _step != null;
            };
        });
    }

    // ---------- 34 HopWindows ----------

    /// <summary>Three hops along the top of the front window (or along the taskbar when there is none).</summary>
    private void StartHopWindows()
    {
        var (hwnd, w, _, perchable) = ForegroundInfo.Current();
        if (!perchable || hwnd == 0 || w.Right - w.Left < 300) { StartHopIcons(); return; }
        float dip = Monitor.DpiScale;
        float y = w.Top - BodySize.Y + 6 * dip;
        float span = w.Right - w.Left - BodySize.X - 40 * dip;
        bool rightward = Centre.X < (w.Left + w.Right) / 2f;
        var spots = Enumerable.Range(0, 4)
            .Select(i => Clamp(new Vector2(w.Left + 20 * dip + span * (rightward ? i / 3f : 1 - i / 3f), y))).ToList();
        HoldStill();
        int hop = 1;
        void Next()
        {
            if (hop == spots.Count) { Wait(0.6f, () => Play(MotionKind.Hover)); return; }
            var from = TopLeft;
            var to = spots[hop++];
            float t = 0, lift = 60 * dip;
            _step = dt =>
            {
                t = MathF.Min(1, t + dt / 0.55f);
                TopLeft = Vector2.Lerp(from, to, t) - new Vector2(0, lift * MathF.Sin(MathF.PI * t));
                if (t < 1) return true;
                Squash(horizontal: false);
                Wait(0.25f, Next);
                return true;
            };
        }
        _step = Travel(spots[0], 0.9f, Smooth, done: () => { Squash(horizontal: false); Wait(0.3f, Next); });
    }

    // ---------- 35 MoonToss ----------

    /// <summary>She tosses her moon up and catches it, twice (only while her moon is out, not the orb).</summary>
    private void StartMoonToss()
    {
        if (_v.MoonProps.TryGetScalar("moon", out float moon) != CompositionGetValueStatus.Succeeded || moon < 0.5f)
        {
            StartPlayWithMoon();
            return;
        }
        var c = _v.Root.Compositor;
        float s = _v.Scale, dpi = Monitor.DpiScale;
        _v.MoonProps.InsertScalar("moon", 0f);
        _moonTossed = true;
        var geometry = c.CreateEllipseGeometry();
        geometry.Radius = new Vector2(11 * s, 11 * s);
        var shape = c.CreateSpriteShape(geometry);
        shape.FillBrush = c.CreateColorBrush(MoonColor);
        shape.Offset = new Vector2(16 * s, 16 * s);
        var ball = c.CreateShapeVisual();
        ball.Size = new Vector2(32 * s, 32 * s);
        ball.Shapes.Add(shape);
        var top = new Vector2(BodyOffset.X + BodySize.X / 2 - 16 * s, BodyOffset.Y - 30 * s);
        ball.Offset = new Vector3(top, 0);
        _v.Root.Children.InsertAtTop(ball);
        _rings.Add(ball);
        var toss = c.CreateVector3KeyFrameAnimation();
        var ease = c.CreateCubicBezierEasingFunction(new(0.2f, 0.6f), new(0.4f, 1));
        var fallEase = c.CreateCubicBezierEasingFunction(new(0.6f, 0), new(0.8f, 0.4f));
        for (int i = 0; i < 2; i++)
        {
            float b = i / 2f;
            toss.InsertKeyFrame(b + 0.25f, new Vector3(top - new Vector2(0, 70 * dpi), 0), ease);
            toss.InsertKeyFrame(b + 0.5f, new Vector3(top, 0), fallEase);
        }
        toss.Duration = TimeSpan.FromSeconds(2.4);
        ball.StartAnimation("Offset", toss);
        _face(Eyes.Up, Mouth.Grin, 2.4f);
        Wait(0.6f, () => { Squash(horizontal: false); Wait(1.2f, () => { Squash(horizontal: false); Wait(0.7f, () =>
        {
            Play(MotionKind.Hover);   // Reset gives her moon back
        }); }); });
    }

    // ---------- 36 ChaseButterfly ----------

    private ShapeVisual? _butterfly;
    private bool _moonTossed;

    /// <summary>Living-move leftovers when any motion starts: the butterfly goes, her moon comes back.</summary>
    private void ResetLiving()
    {
        RemoveButterfly();
        _v.Character.CenterPoint = new Vector3(150 * _v.Scale, (SaharVisual.BodyUnits.Y + SaharVisual.BodyUnits.Height) * _v.Scale, 0);
        if (_moonTossed) { _moonTossed = false; _v.MoonProps.InsertScalar("moon", 1f); }
    }

    /// <summary>A small butterfly flutters along a wavy path for 5 s and she follows it; then it flies off.</summary>
    private void StartChaseButterfly()
    {
        var c = _v.Root.Compositor;
        float dip = Monitor.DpiScale;
        var work = Monitor.WorkF;
        var origin = _v.Origin;
        RemoveButterfly();
        _butterfly = c.CreateShapeVisual();
        _butterfly.Size = new Vector2(30 * dip, 24 * dip);
        _butterfly.CenterPoint = new Vector3(15 * dip, 12 * dip, 0);
        foreach (var (x, y, rx, ry, color) in new[]
        {
            (9f, 9f, 8f, 7f, Windows.UI.Color.FromArgb(235, 0xB0, 0x62, 0xC0)), (21f, 9f, 8f, 7f, Windows.UI.Color.FromArgb(235, 0xB0, 0x62, 0xC0)),
            (11f, 17f, 5f, 5f, Windows.UI.Color.FromArgb(235, 0xFF, 0xB0, 0x8A)), (19f, 17f, 5f, 5f, Windows.UI.Color.FromArgb(235, 0xFF, 0xB0, 0x8A)),
            (15f, 12f, 1.6f, 7f, Windows.UI.Color.FromArgb(255, 0x4A, 0x4E, 0x78)),
        })
        {
            var g = c.CreateEllipseGeometry();
            g.Radius = new Vector2(rx * dip, ry * dip);
            var sh = c.CreateSpriteShape(g);
            sh.FillBrush = c.CreateColorBrush(color);
            sh.Offset = new Vector2(x * dip, y * dip);
            _butterfly.Shapes.Add(sh);
        }
        _v.Host.Root.Children.InsertAtTop(_butterfly);
        var flap = IdleAnimations.Loop3(c, 0.25, (0f, Vector3.One), (0.5f, new Vector3(0.35f, 1, 1)), (1f, Vector3.One));
        _butterfly.StartAnimation("Scale", flap);

        // A wavy path across her screen, starting just ahead of her.
        var start = Centre + new Vector2((Centre.X < work.X + work.Width / 2 ? 1 : -1) * 90 * dip, -40 * dip);
        float dir = start.X > Centre.X ? 1 : -1;
        float t = 0;
        var lag = Centre;
        _eyes = false;
        _step = dt =>
        {
            t += dt;
            var b = start + new Vector2(dir * 70 * dip * t, MathF.Sin(t * 3.1f) * 50 * dip + MathF.Sin(t * 7.3f) * 12 * dip);
            b = new Vector2(Math.Clamp(b.X, work.X + 20 * dip, work.Right - 50 * dip), Math.Clamp(b.Y, work.Y + 20 * dip, work.Bottom - 60 * dip));
            if (t > 5) b += new Vector2(dir * 400 * dip * (t - 5), -500 * dip * (t - 5));   // flies off
            _butterfly.Offset = new Vector3(b - origin - new Vector2(15 * dip, 12 * dip), 0);
            if (t <= 5)
            {
                lag = Vector2.Lerp(lag, b + new Vector2(-dir * 55 * dip, 25 * dip), MathF.Min(1, dt * 2.2f));
                TopLeft = Clamp(lag - BodySize / 2);
                _v.Root.RotationAngleInDegrees = dir * 6 * MathF.Sin(t * 4);
            }
            if (t < 6.2f) return true;
            RemoveButterfly();
            _v.Root.RotationAngleInDegrees = 0;
            _face(Eyes.Happy, Mouth.Grin, 1.5f);
            _step = null;
            Play(MotionKind.Hover);
            return _step != null;
        };
    }

    private void RemoveButterfly()
    {
        if (_butterfly is null) return;
        _v.Host.Root.Children.Remove(_butterfly);
        _butterfly.Dispose();
        _butterfly = null;
    }
}
