using System.Numerics;
using Sahar.Core.Games;
using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Core.Text;

namespace Sahar.Core.Tests.Games;

/// <summary>0.16 «الغميضة» (spec §5, plan Task 1, Review Focus 5): smart hiding spots, warmer/colder, the whisper gate.</summary>
public class HideSeekTests
{
    private static readonly RectF Mon = new(0, 0, 1920, 1080);
    private static readonly RectF Bar = new(0, 1032, 1920, 48);           // a bottom taskbar
    private static readonly RectF Work = new(0, 0, 1920, 1032);
    private static readonly Vector2 Body = new(80, 77);

    private static SeekWorld World(IReadOnlyList<SeekWindow>? windows = null, float dpi = 1f, RectF? bar = null, int barEdge = 3,
        IReadOnlyList<RectF>? monitors = null, Vector2? body = null) =>
        new(Mon, bar is { } b && barEdge == 3 ? Work with { Height = b.Y - Mon.Y } : Work, dpi, monitors ?? [Mon], bar ?? Bar, barEdge,
            windows ?? [], (body ?? Body) * dpi);

    private static readonly SeekWindow Editor = new(1, new RectF(300, 150, 900, 600));
    private static readonly SeekWindow Browser = new(2, new RectF(1000, 300, 700, 500));

    private static float Dist(RectF r, Vector2 p)
    {
        float dx = MathF.Max(MathF.Max(r.X - p.X, 0), p.X - r.Right), dy = MathF.Max(MathF.Max(r.Y - p.Y, 0), p.Y - r.Bottom);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static RectF BodyAt(HideSpot s, SeekWorld w) => new(s.TopLeft.X, s.TopLeft.Y, w.Body.X, w.Body.Y);

    // ---- warmer or colder ----

    [Theory]
    [InlineData(0f, Warmth.Hot)] [InlineData(199.9f, Warmth.Hot)]
    [InlineData(200f, Warmth.Warm)] [InlineData(449.9f, Warmth.Warm)]
    [InlineData(450f, Warmth.Cold)] [InlineData(899.9f, Warmth.Cold)]
    [InlineData(900f, Warmth.Far)] [InlineData(5000f, Warmth.Far)]
    public void Warmth_Thresholds(float dip, Warmth expected) => Assert.Equal(expected, HideSeek.Warmth(dip));

    [Fact]
    public void Tremble_OnlyWithin120Dip()
    {
        Assert.True(HideSeek.Trembles(119.9f));
        Assert.False(HideSeek.Trembles(120f));
    }

    [Fact]
    public void DistanceDip_IsToWhatShowsOfHer_InDip()
    {
        var s = new HideSpot(SpotKind.Edge, Vector2.Zero, new RectF(100, 100, 30, 60), 0, default, 0);
        Assert.Equal(0f, HideSeek.DistanceDip(s, new Vector2(110, 120), 1.5f));
        Assert.Equal(100f, HideSeek.DistanceDip(s, new Vector2(130 + 150, 130), 1.5f), 3);
    }

    // ---- spots: the rules that always hold ----

    public static TheoryData<float> Dpis => [1f, 1.5f];

    [Theory]
    [MemberData(nameof(Dpis))]
    public void Spots_NeverNearThePointer_NeverOffTheMonitor(float dpi)
    {
        var rng = new Random(7);
        var windows = new[] { Scale(Editor, dpi), Scale(Browser, dpi) };
        var w = World(windows, dpi, bar: new RectF(0, 1032 * dpi, 1920 * dpi, 48 * dpi));
        w = w with { Monitor = new RectF(0, 0, 1920 * dpi, 1080 * dpi), Work = new RectF(0, 0, 1920 * dpi, 1032 * dpi), Monitors = [new RectF(0, 0, 1920 * dpi, 1080 * dpi)] };
        for (int i = 0; i < 300; i++)
        {
            var pointer = new Vector2((float)rng.NextDouble() * w.Monitor.Width, (float)rng.NextDouble() * w.Monitor.Height);
            foreach (var s in HideSeek.Spots(w, pointer, new Random(i)))
            {
                Assert.True(Dist(s.Visible, pointer) >= HideSeek.PointerDip * dpi, $"{s.Kind} {s.Visible} is {Dist(s.Visible, pointer) / dpi:0} DIP from {pointer}");
                Assert.True(s.Visible.Width > 0 && s.Visible.Height > 0, $"{s.Kind}: nothing shows");
                Assert.True(w.Work.Contains(s.Visible), $"{s.Kind} {s.Visible} leaves the work area");
            }
        }
    }

    private static SeekWindow Scale(SeekWindow w, float k) => w with { Rect = new RectF(w.Rect.X * k, w.Rect.Y * k, w.Rect.Width * k, w.Rect.Height * k) };

    [Fact]
    public void Spots_NoHiddenPartSpillsOntoAnotherMonitor()
    {
        var right = new RectF(1920, 0, 1920, 1080);
        var below = new RectF(0, 1080, 1920, 1080);
        var w = World([Editor, Browser], monitors: [Mon, right, below]);
        var spots = HideSeek.Spots(w, new Vector2(10, 10), new Random(3));
        Assert.NotEmpty(spots);
        foreach (var s in spots)
        {
            var body = BodyAt(s, w);
            Assert.False(body.Intersects(right) || body.Intersects(below), $"{s.Kind} {body} reaches another screen");
        }
        Assert.DoesNotContain(spots, s => s.Kind == SpotKind.Taskbar);   // a screen below the bar: her body would show there
    }

    [Fact]
    public void Spots_NeverUnderAFullscreenApp()
    {
        var video = new SeekWindow(9, new RectF(0, 0, 1920, 1080), Fullscreen: true);
        Assert.Empty(HideSeek.Spots(World([video, Editor]), new Vector2(5, 5), new Random(1)));

        var half = new SeekWindow(9, new RectF(960, 0, 960, 1080), Fullscreen: true);   // only the right half is taken
        var spots = HideSeek.Spots(World([half, Editor]), new Vector2(5, 5), new Random(1));
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.False(s.Visible.Intersects(half.Rect), $"{s.Kind} {s.Visible} is under the fullscreen app"));
        Assert.DoesNotContain(spots, s => s.WindowId == 9);
    }

    // ---- spots: each kind ----

    [Fact]
    public void WindowSpot_StraddlesASideEdge_OnlyAThirdShows_InsideTheWindowHeight()
    {
        var w = World([Editor]);
        var spots = HideSeek.Spots(w, new Vector2(1900, 20), new Random(2)).Where(s => s.Kind == SpotKind.Window).ToList();
        Assert.NotEmpty(spots);
        foreach (var s in spots)
        {
            Assert.Equal(1, s.WindowId);
            Assert.Equal(Editor.Rect, s.Behind);
            Assert.Contains(s.Side, new[] { 0, 2 });
            var body = BodyAt(s, w);
            float edgeX = s.Side == 0 ? Editor.Rect.X : Editor.Rect.Right;
            Assert.True(body.X < edgeX && body.Right > edgeX, $"she does not straddle the edge: {body}");
            Assert.InRange(s.Visible.Width / w.Body.X, 0.3f, 0.45f);
            Assert.False(s.Visible.Intersects(Editor.Rect), "what shows of her must be outside the window");
            Assert.True(body.Y >= Editor.Rect.Y && body.Bottom <= Editor.Rect.Bottom, $"her body leaves the window's height: {body}");
        }
    }

    [Fact]
    public void WindowSpot_NotWhereAWindowAboveCoversWhatShows()
    {
        // The browser sits above the editor and covers its right edge from y 300 to 800.
        var w = World([Browser, Editor]);
        var spots = HideSeek.Spots(w, new Vector2(5, 1000), new Random(4)).Where(s => s.WindowId == 1).ToList();
        Assert.All(spots, s => Assert.False(s.Visible.Intersects(Browser.Rect), $"shows over the browser: {s.Visible}"));
        Assert.Contains(spots, s => s.Side == 0);   // the left edge is free
    }

    [Fact]
    public void WindowSpot_NoneForATinyWindow()
    {
        var tiny = new SeekWindow(5, new RectF(800, 400, 120, 90));
        Assert.DoesNotContain(HideSeek.Spots(World([tiny]), new Vector2(5, 5), new Random(1)), s => s.Kind == SpotKind.Window);
    }

    [Fact]
    public void TaskbarSpot_OnlyHerLeafAndCrownShowAboveTheBar()
    {
        var w = World();
        var spots = HideSeek.Spots(w, new Vector2(5, 5), new Random(1)).Where(s => s.Kind == SpotKind.Taskbar).ToList();
        Assert.NotEmpty(spots);
        foreach (var s in spots)
        {
            Assert.Equal(Bar, s.Behind);
            Assert.Equal(Bar.Y, s.Visible.Bottom, 3);
            var body = BodyAt(s, w);
            Assert.True(body.Y < Bar.Y && body.Bottom > Bar.Y, $"not behind the bar: {body}");
            Assert.True(Bar.Y - body.Y <= w.Body.Y * 0.2f, "more than her crown shows above the bar");
        }
    }

    [Fact]
    public void TaskbarSpot_NoneWhenTheBarAutoHidesOrIsNotAtTheBottom()
    {
        Assert.DoesNotContain(HideSeek.Spots(World(bar: new RectF(0, 1080, 1920, 0)), new Vector2(5, 5), new Random(1)), s => s.Kind == SpotKind.Taskbar);
        var leftBar = new RectF(0, 0, 60, 1080);
        var w = World(bar: leftBar, barEdge: 0) with { Work = new RectF(60, 0, 1860, 1080) };
        Assert.DoesNotContain(HideSeek.Spots(w, new Vector2(1900, 5), new Random(1)), s => s.Kind == SpotKind.Taskbar);
    }

    [Fact]
    public void EdgeSpot_AQuarterShows_NeverOnTheTaskbarEdge()
    {
        var w = World();
        var spots = HideSeek.Spots(w, new Vector2(960, 540), new Random(1)).Where(s => s.Kind == SpotKind.Edge).ToList();
        Assert.NotEmpty(spots);
        foreach (var s in spots)
        {
            Assert.NotEqual(3, s.Side);
            Assert.InRange(s.Visible.Width * s.Visible.Height / (w.Body.X * w.Body.Y), 0.2f, 0.35f);
        }
    }

    // ---- the pick: weights, no repeats, fallbacks ----

    [Fact]
    public void Spots_FirstPickIsWeighted_50_30_20()
    {
        var w = World([Editor]);
        var pointer = new Vector2(1500, 150);
        var counts = new Dictionary<SpotKind, int>();
        const int n = 6000;
        for (int seed = 0; seed < n; seed++)
        {
            var k = HideSeek.Spots(w, pointer, new Random(seed))[0].Kind;
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        Assert.InRange(counts[SpotKind.Window] / (float)n, 0.47f, 0.53f);
        Assert.InRange(counts[SpotKind.Taskbar] / (float)n, 0.27f, 0.33f);
        Assert.InRange(counts[SpotKind.Edge] / (float)n, 0.17f, 0.23f);
    }

    [Fact]
    public void Spots_NoWindows_TheOtherKindsShareTheWeight()
    {
        var w = World();
        int bar = 0;
        for (int seed = 0; seed < 4000; seed++) if (HideSeek.Spots(w, new Vector2(960, 300), new Random(seed))[0].Kind == SpotKind.Taskbar) bar++;
        Assert.InRange(bar / 4000f, 0.57f, 0.63f);   // 30 / (30 + 20)
    }

    [Fact]
    public void Spots_RankedByKindThenShuffled_AllCandidatesKept()
    {
        var w = World([Editor]);
        var a = HideSeek.Spots(w, new Vector2(1500, 150), new Random(11));
        var b = HideSeek.Spots(w, new Vector2(1500, 150), new Random(12));
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(a.ToHashSet(), b.ToHashSet());
        // One kind's spots come together.
        var kinds = a.Select(s => s.Kind).ToList();
        Assert.Equal(kinds.Distinct().Count(), kinds.Zip(kinds.Skip(1)).Count(p => p.First != p.Second) + 1);
    }

    [Fact]
    public void Spots_NeverRepeatedInTheSameGame()
    {
        var w = World([Editor, Browser]);
        var used = new List<HideSpot>();
        var rng = new Random(5);
        for (int round = 0; round < 3; round++)
        {
            var pick = HideSeek.Spots(w, new Vector2(5, 5), rng, used)[0];
            foreach (var u in used)
                Assert.True(Vector2.Distance(Centre(pick.Visible), Centre(u.Visible)) >= HideSeek.RepeatDip, $"round {round} reused {u.Visible}");
            used.Add(pick);
        }
    }

    private static Vector2 Centre(RectF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    [Fact]
    public void Spots_PointerOnTheOnlyWindow_FallsBackToEdges()
    {
        var w = World([new SeekWindow(1, new RectF(760, 300, 400, 400))], bar: new RectF(0, 1080, 1920, 0));   // no taskbar spots
        var spots = HideSeek.Spots(w, new Vector2(960, 500), new Random(1));
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.Equal(SpotKind.Edge, s.Kind));
    }

    [Fact]
    public void Spots_PointerEverywhereNear_ReturnsNothingRatherThanANearSpot()
    {
        var small = new RectF(0, 0, 500, 400);
        var w = new SeekWorld(small, small, 1f, [small], new RectF(0, 400, 500, 0), 3, [], Body);
        Assert.Empty(HideSeek.Spots(w, new Vector2(250, 200), new Random(1)));
    }

    // ---- mid-round: the window moves or closes ----

    [Fact]
    public void StillThere_WindowMovedClosedOrCovered_IsFalse()
    {
        var w = World([Editor]);
        var spot = HideSeek.Spots(w, new Vector2(1900, 20), new Random(2)).First(s => s.Kind == SpotKind.Window);
        Assert.True(HideSeek.StillThere(spot, w));
        Assert.False(HideSeek.StillThere(spot, World([Editor with { Rect = Editor.Rect with { X = 340 } }])));
        Assert.False(HideSeek.StillThere(spot, World()));
        var cover = new SeekWindow(7, new RectF(spot.Visible.X - 20, spot.Visible.Y - 20, 300, 300));
        Assert.False(HideSeek.StillThere(spot, World([cover, Editor])));
        Assert.False(HideSeek.StillThere(spot, World([Editor with { Fullscreen = true }])));
        Assert.True(HideSeek.StillThere(spot, World([Editor with { Rect = Editor.Rect with { X = 301 } }])));   // a pixel of jitter
    }

    [Fact]
    public void StillThere_TaskbarAndEdge()
    {
        var w = World();
        var bar = HideSeek.Spots(w, new Vector2(5, 5), new Random(1)).First(s => s.Kind == SpotKind.Taskbar);
        Assert.True(HideSeek.StillThere(bar, w));
        Assert.False(HideSeek.StillThere(bar, World(bar: new RectF(0, 1080, 1920, 0))));
        var edge = HideSeek.Spots(w, new Vector2(960, 540), new Random(1)).First(s => s.Kind == SpotKind.Edge);
        Assert.True(HideSeek.StillThere(edge, World([new SeekWindow(4, new RectF(500, 500, 300, 300))])));
    }

    [Fact]
    public void Fallback_TheNearestEdgeSpot_FarFromThePointer()
    {
        var w = World();
        var from = new Vector2(1500, 400);   // where she was, behind a window that closed
        var f = HideSeek.Fallback(w, new Vector2(100, 900), from);
        Assert.NotNull(f);
        Assert.Equal(SpotKind.Edge, f!.Value.Kind);
        Assert.Equal(2, f.Value.Side);   // the right edge is nearest
        var all = HideSeek.Spots(w, new Vector2(100, 900), new Random(1)).Where(s => s.Kind == SpotKind.Edge);
        Assert.Equal(all.Min(s => Vector2.Distance(s.TopLeft, from)), Vector2.Distance(f.Value.TopLeft, from), 3);
    }

    [Fact]
    public void Fallback_PointerOnTheNearestEdge_TakesAnotherEdge()
    {
        var w = World();
        var f = HideSeek.Fallback(w, new Vector2(1910, 400), new Vector2(1880, 400));
        Assert.NotNull(f);
        Assert.True(Dist(f!.Value.Visible, new Vector2(1910, 400)) >= HideSeek.PointerDip);
    }

    // ---- a click finds her ----

    [Fact]
    public void Hit_OnWhatShows_NotOnTheHiddenPart()
    {
        var w = World([Editor]);
        var s = HideSeek.Spots(w, new Vector2(1900, 20), new Random(2)).First(x => x.Kind == SpotKind.Window);
        Assert.True(HideSeek.Hit(s, Centre(s.Visible), 1f));
        var hidden = s.Side == 2 ? new Vector2(s.Visible.X - 30, Centre(s.Visible).Y) : new Vector2(s.Visible.Right + 30, Centre(s.Visible).Y);
        Assert.False(HideSeek.Hit(s, hidden, 1f));
    }

    // ---- the whisper chip: when it may show ----

    [Fact]
    public void Whisper_NotInTheFirst5s_NeedsTheMouseStill400ms_AtMostEvery3s()
    {
        var g = new WhisperGate();
        var p = new Vector2(500, 500);
        // The first 5 s of hiding: nothing, however still the mouse is.
        for (double t = 0; t < 5; t += 0.05) Assert.False(g.Update(t, p, 1f));
        // Still since before 5 s: shows right at 5 s.
        Assert.True(g.Update(5.0, p, 1f));
        // Once per stop.
        Assert.False(g.Update(5.5, p, 1f));
        // Moves, stops again at 6.0: 0.4 s later is only 6.4, but 3 s since the last is 8.0.
        Assert.False(g.Update(6.0, p + new Vector2(40, 0), 1f));
        Assert.False(g.Update(6.5, p + new Vector2(40, 0), 1f));
        Assert.True(g.Update(8.0, p + new Vector2(40, 0), 1f));
        // A moving mouse never gets one.
        for (int i = 1; i < 40; i++) Assert.False(g.Update(8.0 + i * 0.1, p + new Vector2(40 + i * 10, 0), 1f));
        // A stop of only 0.3 s: none; 0.4 s: one.
        var q = p + new Vector2(500, 0);
        Assert.False(g.Update(12.0, q, 1f));
        Assert.False(g.Update(12.3, q, 1f));
        Assert.True(g.Update(12.4, q, 1f));
    }

    [Fact]
    public void Whisper_ATinyJitterStillCountsAsStill()
    {
        var g = new WhisperGate();
        Assert.False(g.Update(5.0, new Vector2(100, 100), 1.5f));
        Assert.True(g.Update(5.4, new Vector2(103, 102), 1.5f));
    }

    [Fact]
    public void Giggle_Every8PlusMinus2s()
    {
        var rng = new Random(3);
        var all = Enumerable.Range(0, 500).Select(_ => HideSeek.NextGiggle(rng)).ToList();
        Assert.All(all, s => Assert.InRange(s, 6.0, 10.0));
        Assert.InRange(all.Average(), 7.7, 8.3);
    }

    // ---- her words: short, Syrian, no emoji ----

    [Theory]
    [InlineData(Warmth.Hot, "دافي… دافي كتير!")]
    [InlineData(Warmth.Warm, "دافي…")]
    [InlineData(Warmth.Cold, "برد…")]
    [InlineData(Warmth.Far, "برد… بعيد كتير")]
    public void Text_Whisper(Warmth w, string line) => Assert.Equal(line, HideSeekText.Whisper(w));

    [Fact]
    public void Text_FoundWithSeconds_InArabicIndic()
    {
        Assert.Equal("لقيتني! بـ ١٢ ثانية", HideSeekText.Found(12.4));
        Assert.Equal("لقيتني! بـ ١ ثانية", HideSeekText.Found(0.3));
        Assert.Equal("لقيتيني! بـ ١٢ ثانية", Address.For(Gender.Female, HideSeekText.Found(12)));
    }

    [Theory]
    [InlineData(0, "ما لقيتني ولا مرة. منعيدها؟")]
    [InlineData(1, "لقيتني مرة من تلاتة. منعيدها؟")]
    [InlineData(2, "لقيتني مرتين من تلاتة. منعيدها؟")]
    [InlineData(3, "لقيتني بالتلاتة! منعيدها؟")]
    public void Text_End(int found, string line) => Assert.Equal(line, HideSeekText.End(found));

    [Fact]
    public void Text_NoEmojiAnywhere()
    {
        var lines = new[] { HideSeekText.GaveUp, HideSeekText.Giggle, HideSeekText.Again, HideSeekText.Done, HideSeekText.Found(9) }
            .Concat(Enum.GetValues<Warmth>().Select(HideSeekText.Whisper)).Concat(Enumerable.Range(0, 4).Select(HideSeekText.End));
        foreach (var l in lines)
            Assert.DoesNotContain(l.EnumerateRunes(), r => r.Value >= 0x2190 && !(r.Value is >= 0x2010 and <= 0x2027));
        Assert.Equal("هون كنت!", HideSeekText.GaveUp);
        Assert.Equal("هِهِ", HideSeekText.Giggle);
        Assert.Equal(Digits.ToArabicIndic("3"), HideSeekText.Count(3));
    }
}
