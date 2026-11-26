using Sahar.Core.Surfaces;
using Xunit;

namespace Sahar.Core.Tests.Surfaces;

public class SurfaceMsgTests
{
    [Fact]
    public void RoundTrips_OneLine()
    {
        var m = new SurfaceMsg { T = "show", Surface = "panel", Kind = "surah", Seq = 7, Dark = true, Capturable = false,
            X = 10, Y = 20, W = 320, H = 600, Clasp = 400, Left = true, Model = """{"rows":[1,2]}""" };
        var line = SurfaceJson.Write(m);
        Assert.DoesNotContain('\n', line);
        var back = SurfaceJson.Read(line)!;
        Assert.Equal("show", back.T);
        Assert.Equal("surah", back.Kind);
        Assert.Equal(7, back.Seq);
        Assert.True(back.Dark);
        Assert.Equal("""{"rows":[1,2]}""", back.Model);
    }

    [Fact]
    public void Garbage_IsNull() => Assert.Null(SurfaceJson.Read("{not json"));
}
