using Sahar.Core.Expressions;

namespace Sahar.Core.Tests.Expressions;

public class FaceSwapTests
{
    [Fact]
    public void FaceSwap_ClosesThenSwapsThenOpens()
    {
        var p = FaceSwap.Plan(10, null, reduced: false);
        Assert.Equal((10.0, 1f), (p.Start, p.From));
        Assert.Equal(10.07, p.SwapAt, 6);
        Assert.Equal(10.16, p.End, 6);
        Assert.Equal(1f, FaceSwap.EyeScaleAt(p, 10));
        Assert.Equal(0f, FaceSwap.EyeScaleAt(p, p.SwapAt), 3);
        Assert.Equal(1f, FaceSwap.EyeScaleAt(p, p.End));
        // Closing only goes down, opening only up: the eyes never flutter.
        float last = 1f;
        for (double t = 10; t < p.SwapAt; t += 0.005) { var s = FaceSwap.EyeScaleAt(p, t); Assert.True(s <= last + 1e-6); last = s; }
        last = 0f;
        for (double t = p.SwapAt; t <= p.End; t += 0.005) { var s = FaceSwap.EyeScaleAt(p, t); Assert.True(s >= last - 1e-6); last = s; }
    }

    [Fact]
    public void FaceSwap_EasesIn_ThenOut()
    {
        var p = FaceSwap.Plan(0, null, false);
        Assert.True(FaceSwap.EyeScaleAt(p, 0.035) > 0.5f);   // ease-in: slow start
        Assert.True(FaceSwap.EyeScaleAt(p, p.SwapAt + 0.045) > 0.5f);   // ease-out: fast start
    }

    [Fact]
    public void FaceSwap_NewerFaceBeforeTheClosedMoment_KeepsTheSwap()
    {
        var first = FaceSwap.Plan(0, null, false);
        var second = FaceSwap.Plan(0.03, first, false);
        Assert.True(second.Retarget);
        Assert.Equal((first.Start, first.SwapAt, first.End), (second.Start, second.SwapAt, second.End));
    }

    [Fact]
    public void FaceSwap_NewerFaceWhileOpening_ClosesFromWhereTheEyesAre()
    {
        var first = FaceSwap.Plan(0, null, false);
        double now = first.SwapAt + 0.03;
        float at = FaceSwap.EyeScaleAt(first, now);
        var second = FaceSwap.Plan(now, first, false);
        Assert.False(second.Retarget);
        Assert.Equal((now, at), (second.Start, second.From));
        Assert.Equal(now + FaceSwap.CloseSeconds * at, second.SwapAt, 6);   // a shorter close: no jump back open
        Assert.Equal(at, FaceSwap.EyeScaleAt(second, now), 4);
    }

    [Fact]
    public void FaceSwap_AfterTheEnd_IsAFreshBlink()
    {
        var first = FaceSwap.Plan(0, null, false);
        var second = FaceSwap.Plan(first.End + 1, first, false);
        Assert.Equal((1f, false), (second.From, second.Retarget));
    }

    [Fact]
    public void FaceSwap_Reduced_IsInstant()
    {
        var p = FaceSwap.Plan(5, FaceSwap.Plan(4.99, null, false), reduced: true);
        Assert.True(p.Instant);
        Assert.Equal(5, p.SwapAt);
        Assert.Equal(1f, FaceSwap.EyeScaleAt(p, 5));
    }
}
