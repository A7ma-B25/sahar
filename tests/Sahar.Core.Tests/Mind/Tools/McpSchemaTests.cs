using System.Text.Json;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>0.19 spec §5: an MCP inputSchema becomes the ArgSpec subset or is not announced; names never collide; the
/// descriptor hash pins what he approved.</summary>
public sealed class McpSchemaTests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void ToArgs_FitsIntStringBoolEnum()
    {
        var args = McpSchema.ToArgs(J("""
            {"type":"object","properties":{
              "n":{"type":"integer","minimum":1,"maximum":9},
              "q":{"type":"string","maxLength":80},
              "on":{"type":"boolean"},
              "unit":{"type":"string","enum":["c","f"]}},
             "required":["q"]}
            """))!;
        Assert.Equal(4, args.Count);
        var n = args.Single(a => a.Name == "n");
        Assert.Equal((ArgType.Int, 1, 9, false), (n.Type, n.Min, n.Max, n.Required));
        var q = args.Single(a => a.Name == "q");
        Assert.Equal((ArgType.String, 80, true), (q.Type, q.MaxLength, q.Required));
        Assert.Equal(ArgType.Bool, args.Single(a => a.Name == "on").Type);
        var unit = args.Single(a => a.Name == "unit");
        Assert.Equal(ArgType.Enum, unit.Type);
        Assert.Equal(["c", "f"], unit.Values!);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"x":{"type":"number"}}}""")]
    [InlineData("""{"type":"object","properties":{"x":{"type":"array","items":{"type":"string"}}}}""")]
    [InlineData("""{"type":"object","properties":{"x":{"type":"object"}}}""")]
    [InlineData("""{"type":"object","properties":{"x":{"$ref":"#/defs/a"}}}""")]
    [InlineData("""{"type":"object","properties":{"x":{"oneOf":[{"type":"string"}]}}}""")]
    [InlineData("""{"type":"object","properties":{"x y":{"type":"string"}}}""")]
    [InlineData("""{"type":"string"}""")]
    public void ToArgs_NumberArrayObjectRef_IsNull(string schema) => Assert.Null(McpSchema.ToArgs(J(schema)));

    [Fact]
    public void ToArgs_NoProperties_IsEmpty() => Assert.Empty(McpSchema.ToArgs(J("""{"type":"object"}"""))!);

    [Fact]
    public void ToArgs_MoreThanEight_IsNull()
    {
        var props = string.Join(',', Enumerable.Range(1, 9).Select(i => $"\"a{i}\":{{\"type\":\"string\"}}"));
        Assert.Null(McpSchema.ToArgs(J("{\"type\":\"object\",\"properties\":{" + props + "}}")));
    }

    [Fact]
    public void ToArgs_StringWithoutMax_Caps500() =>
        Assert.Equal(500, McpSchema.ToArgs(J("""{"type":"object","properties":{"s":{"type":"string"}}}"""))!.Single().MaxLength);

    [Fact]
    public void Slug_ArabicOnlyName_IsServer() => Assert.Equal("server", McpSchema.Slug("سيرفر الملفات", []));

    [Fact]
    public void Slug_LowercaseUnderscoresAndCut()
    {
        Assert.Equal("my_files", McpSchema.Slug("  My Files!! ", []));
        Assert.Equal(20, McpSchema.Slug("a very very long server name here", []).Length);
    }

    [Fact]
    public void Slug_Collision_AddsSuffix()
    {
        Assert.Equal("files_2", McpSchema.Slug("Files", ["files"]));
        Assert.Equal("files_3", McpSchema.Slug("files", ["files", "files_2"]));
    }

    [Theory]
    [InlineData("read_file", "mcp.files.read_file")]
    [InlineData("get-weather", "mcp.files.get-weather")]
    [InlineData("a.b", null)]
    [InlineData("with space", null)]
    [InlineData("", null)]
    public void Qualified_DotOrSpace_IsNull(string tool, string? want) => Assert.Equal(want, McpSchema.Qualified("files", tool));

    [Fact]
    public void Hash_KeyOrderIrrelevant_DescriptionChangeDetected()
    {
        var a = McpSchema.Hash("t", "d", J("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"integer"}}}"""), null);
        var b = McpSchema.Hash("t", "d", J("""{ "properties": { "b": {"type":"integer"}, "a": {"type":"string"} }, "type": "object" }"""), null);
        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.NotEqual(a, McpSchema.Hash("t", "d!", J("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"integer"}}}"""), null));
        Assert.NotEqual(a, McpSchema.Hash("t", "d", J("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"integer"}}}"""), J("""{"readOnlyHint":true}""")));
    }
}
