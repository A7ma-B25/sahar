using Sahar.Core.Expressions;
using Sahar.Core.Motion;

namespace Sahar.Core.Life;

public enum ShowcaseKind { Motion, Flight, Expression }

/// <summary>One step of "فرجيني حركاتك": a motion, a flight pattern or an expression, with its caption.</summary>
public sealed record ShowcaseStep(ShowcaseKind Kind, string Id, string Caption, float Seconds);

/// <summary>Everything she can do, in board order (motions 1–25 but the drag, the 10 flights, expressions 1–25).</summary>
public static class Showcase
{
    // Board "حركة سَحَر", motions 1–25.
    private static readonly string[] MotionNames =
    [
        "بتطير بالشاشة", "بتحوم بمكانها", "بتطل من ورا التاسك بار", "عيونها بتلحق الماوس", "بتسحبها بالماوس",
        "بترميها", "بتنزل وبتقعد", "بتغفى", "بتصحى", "بتختفي وقت الشاشة الكاملة",
        "بتقعد على حافة نافذة", "بتعمل لفّة من الفرحة", "بتلحق الماوس", "بتختبي ورا نافذة", "بتلعب مع قمرها",
        "فضولية", "بتستحي", "أول ما تشغّل الجهاز", "لما تطفي الجهاز", "بتنط على الأيقونات",
        "بترقص مع الموسيقى", "بتتنفّس معك", "بتلفت انتباهك", "بتدور حول الماوس", "بتغفى على أيقونة",
    ];

    // Spec §5, expressions 1–25.
    private static readonly string[] ExpressionNames =
    [
        "موسيقى", "Claude شغال", "Claude بيطلب إذن", "Claude خلّص", "المؤقت خلص", "هدف / احتفال", "مطر أو تنبيه طقس",
        "بطارية ضعيفة", "عم تشحن", "تنزيل أو نسخ ملفات", "نوم", "رجعت", "إشعار جديد", "لا تزعجني", "الإنترنت انقطع",
        "الإنترنت رجع", "لقطة شاشة", "نسخت شي", "تذكير بموعد", "صار في خطأ", "الجهاز سخن", "كبست عليها", "سحبتها",
        "وقت استراحة", "سهرانين بعد نص الليل",
    ];

    private static readonly Dictionary<FlightPattern, string> FlightNames = new()
    {
        [FlightPattern.Zigzag] = "زيك زاك", [FlightPattern.FigureEight] = "رقم 8", [FlightPattern.SpiralUp] = "لولب لفوق",
        [FlightPattern.Dive] = "غطسة", [FlightPattern.Wave] = "موجة", [FlightPattern.GlideHigh] = "تزحلق لفوق",
        [FlightPattern.BigCircle] = "دايرة كبيرة", [FlightPattern.DashAndStop] = "ركضة ووقفة",
        [FlightPattern.FallingLeaf] = "ورقة عم توقع", [FlightPattern.TaskbarPatrol] = "دورية فوق التاسك بار",
    };

    private static float MotionSeconds(MotionKind k) => k switch
    {
        MotionKind.Hover => 2.5f, MotionKind.EyesFollow => 3f, MotionKind.Wake => 3f, MotionKind.FullscreenHide => 3f,
        MotionKind.Goodbye => 3f, MotionKind.Shy => 3.5f, MotionKind.Throw or MotionKind.LandAndSit or MotionKind.StartupEntry
            or MotionKind.Attention or MotionKind.CelebrateLoop => 4f,
        MotionKind.HopIcons => 4.5f, MotionKind.PlayWithMoon or MotionKind.Curious or MotionKind.OrbitCursor => 5f,
        MotionKind.PeekTaskbar or MotionKind.SleepInCorner or MotionKind.NapOnIcon => 6f,
        MotionKind.Wander or MotionKind.PerchWindow or MotionKind.FollowCursor => 7f,
        MotionKind.HideBehindWindow => 8f, MotionKind.Breathe => 9f, MotionKind.Dance => 13f,
        _ => 4f,
    };

    public static IReadOnlyList<ShowcaseStep> Steps { get; } = Build();

    /// <summary>"العبي": moves that make sense on their own, any time.</summary>
    public static IReadOnlyList<MotionKind> PlayPicks { get; } =
    [
        MotionKind.Wander, MotionKind.PeekTaskbar, MotionKind.PerchWindow, MotionKind.CelebrateLoop, MotionKind.FollowCursor,
        MotionKind.HideBehindWindow, MotionKind.PlayWithMoon, MotionKind.Curious, MotionKind.Shy, MotionKind.HopIcons,
        MotionKind.Dance, MotionKind.Breathe, MotionKind.OrbitCursor,
    ];

    private static List<ShowcaseStep> Build()
    {
        var steps = new List<ShowcaseStep>();
        foreach (var k in Enum.GetValues<MotionKind>())
        {
            if (k == MotionKind.Drag) continue;
            int n = (int)k;
            steps.Add(new(ShowcaseKind.Motion, k.ToString(), $"{n} · {MotionNames[n - 1]}", MotionSeconds(k)));
            if (k == MotionKind.Wander)
                foreach (var f in Enum.GetValues<FlightPattern>())
                    steps.Add(new(ShowcaseKind.Flight, f.ToString(), $"1 · طيرة: {FlightNames[f]}", FlightPath.Seconds(f) + 1));
        }
        foreach (var e in ExpressionCatalog.All)
        {
            if (e.Key == "idle") continue;
            var name = e.Id is >= 1 and <= 25 ? ExpressionNames[e.Id - 1] : e.Key;
            steps.Add(new(ShowcaseKind.Expression, e.Key, $"تعبير {e.Id} · {name}", 2.5f));
        }
        return steps;
    }
}
