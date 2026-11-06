using Sahar.Core.Protocol;

namespace Sahar.Core.Tests.Protocol;

public class ProtocolTests
{
    [Fact]
    public void Parse_Event()
    {
        var r = ProtocolJson.ParseRequest("""{"type":"event","name":"claude.permission"}""");
        Assert.Equal(("event", "claude.permission"), (r.Type, r.Name));
        Assert.Null(r.Args);
    }

    [Fact]
    public void Parse_CommandWithArgs()
    {
        var r = ProtocolJson.ParseRequest("""{"type":"cmd","name":"motion","args":{"kind":"Throw","vx":"1600"}}""");
        Assert.Equal(("cmd", "motion", "Throw", "1600"), (r.Type, r.Name, r.Args!["kind"], r.Args["vx"]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"cmd"}""")]
    [InlineData("""{"name":"state"}""")]
    [InlineData("""{"type":"other","name":"x"}""")]
    public void Parse_Garbage_Throws(string line) => Assert.Throws<FormatException>(() => ProtocolJson.ParseRequest(line));

    [Fact]
    public void Write_IsSingleLine()
    {
        var s = ProtocolJson.Write(new PipeReply(false, "unknown expression"));
        Assert.DoesNotContain('\n', s);
        Assert.Contains("\"ok\":false", s);
    }

    [Fact]
    public void Write_StateRoundTrips()
    {
        var st = new EngineState("music", "Hover", false, false, true, 10.5f, 20, 80, 90, 42_000_000);
        var back = ProtocolJson.ParseReply(ProtocolJson.Write(new PipeReply(true, State: st)));
        Assert.True(back.Ok);
        Assert.Equal(st, back.State);
    }
}
