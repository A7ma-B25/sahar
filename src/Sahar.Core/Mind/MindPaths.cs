namespace Sahar.Core.Mind;

/// <summary>SEAM: where the Mind keeps things (spec §7.8). Personal data (encrypted, stream B's box) under
/// %AppData%\Sahar\mind; models and the runtime (not personal) under %LocalAppData%\Sahar\models|runtime.
/// <c>SAHAR_MIND_HOME</c> (tests and CI) puts all three under one folder.</summary>
public static class MindPaths
{
    private static string? Home => Environment.GetEnvironmentVariable("SAHAR_MIND_HOME") is { Length: > 0 } h ? h : null;

    public static string Personal => Home is { } h ? Path.Combine(h, "mind")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar", "mind");
    public static string Models => Home is { } h ? Path.Combine(h, "models")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar", "models");
    public static string Runtime => Home is { } h ? Path.Combine(h, "runtime")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar", "runtime");
}
