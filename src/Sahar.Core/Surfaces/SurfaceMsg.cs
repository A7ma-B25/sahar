using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Surfaces;

/// <summary>
/// One line on the <c>sahar-surfaces</c> pipe between the engine and the surface host (0.16). Engine → host:
/// <c>show</c> (Surface panel|chat, Kind, Model JSON, rect px, Clasp, Left, Dark, Capturable, Lang), <c>update</c>
/// (Model), <c>hide</c>, <c>theme</c>, <c>debug</c>, <c>eval</c> (test mode), <c>focus</c>. Host → engine: <c>event</c> (Kind = pick,
/// submit, action, closed, send, …; Value; Model), <c>ready</c>, <c>reply</c> (to a debug/eval, same Seq).
/// Never logged as is: it carries what he typed.
/// </summary>
public sealed class SurfaceMsg
{
    public string T { get; set; } = "";
    public string? Surface { get; set; }
    public string? Kind { get; set; }
    public int Seq { get; set; }
    public string? Model { get; set; }
    public string? Value { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float Clasp { get; set; }
    public bool Left { get; set; }
    public bool Dark { get; set; }
    public bool Capturable { get; set; }
    public string? Lang { get; set; }
    public float Dpi { get; set; }
    /// <summary>The work area of her monitor (px) for a show: the host places the measured card inside it.</summary>
    public float Wx { get; set; }
    public float Wy { get; set; }
    public float Ww { get; set; }
    public float Wh { get; set; }
    /// <summary>beside (pickers, inputs, «الدرج»: X..H = her body), above (ask bubbles: X..H = her body), anchor
    /// (the tray menu: X,Y = the point it opens above), window (the chat: X..H = her body).</summary>
    public string? Mode { get; set; }
    /// <summary>Reduced motion: no enter/exit animation, no typing dots bounce.</summary>
    public bool Still { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(SurfaceMsg))]
internal sealed partial class SurfaceContext : JsonSerializerContext;

public static class SurfaceJson
{
    public static string Write(SurfaceMsg m) => JsonSerializer.Serialize(m, SurfaceContext.Default.SurfaceMsg);

    public static SurfaceMsg? Read(string line)
    {
        try { return JsonSerializer.Deserialize(line, SurfaceContext.Default.SurfaceMsg); }
        catch (JsonException) { return null; }
    }
}
