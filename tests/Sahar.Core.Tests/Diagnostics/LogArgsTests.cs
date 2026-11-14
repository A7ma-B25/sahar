using Sahar.Core.Diagnostics;

namespace Sahar.Core.Tests.Diagnostics;

/// <summary>engine.log's start line names her switches, never their values (a path holds the user name; test run
/// 43595077286 found «--weather-stub C:\Users\…» in the log).</summary>
public sealed class LogArgsTests
{
    [Fact]
    public void Args_KeepSwitches_DropValues() =>
        Assert.Equal("--test-mode --capturable --weather-stub … --quran-base …",
            LogRules.Args(["--test-mode", "--capturable", "--weather-stub", @"C:\Users\him\AppData\Local\Temp\x", "--quran-base", "http://127.0.0.1:9/"]));

    [Fact]
    public void Args_None_IsEmpty() => Assert.Equal("", LogRules.Args([]));

    [Fact]
    public void Args_HookModeWord_IsAValue() => Assert.Equal("…", LogRules.Args(["hook"]));
}
