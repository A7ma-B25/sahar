using System.Text.RegularExpressions;
using Sahar.Core.Design;

namespace Sahar.Core.Tests.Design;

/// <summary>The settings page's tokens are Core's (plan 0.10c Task 4): one source, both themes, no stray colours.</summary>
public partial class TokensCssTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sahar.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root (Sahar.slnx) not found");
    }

    private static string Web(params string[] parts) => File.ReadAllText(Path.Combine([Root(), "src", "Sahar.Settings", "web", .. parts]));

    private static readonly string[] Names =
        ["bg", "surface", "menu", "sunken", "hover", "pressed", "ink", "text-2", "text-3", "boundary", "accent", "on-accent",
         "accent-soft", "accent-ink", "moon", "leaf", "error", "warn"];

    private static Dictionary<string, string> Block(string css, string theme)
    {
        var m = Regex.Match(css, @":root\[data-theme=" + theme + @"\]\s*\{(?<body>[^}]*)\}");
        Assert.True(m.Success, $"no :root[data-theme={theme}] block");
        return PropertyRx().Matches(m.Groups["body"].Value).ToDictionary(p => p.Groups["name"].Value, p => p.Groups["value"].Value.Trim());
    }

    [Theory]
    [InlineData("light", false)]
    [InlineData("dark", true)]
    public void CssEqualsCoreTokens(string theme, bool dark)
    {
        var props = Block(Web("css", "tokens.css"), theme);
        var t = Tokens.For(dark);
        foreach (var name in Names)
        {
            Assert.True(props.ContainsKey(name), $"--{name} missing ({theme})");
            Assert.Equal(Tokens.Get(t, name).ToString(), props[name].ToUpperInvariant());
        }
        Assert.True(props.ContainsKey("e1"), $"--e1 missing ({theme})");
    }

    [Fact]
    public void TokensCss_HasNoOtherColourLiterals()
    {
        var css = Web("css", "tokens.css");
        var allowed = new HashSet<string>(Names.SelectMany(n => new[] { Tokens.Get(Tokens.Light, n), Tokens.Get(Tokens.Dark, n) })
            .Select(c => c.ToString()), StringComparer.OrdinalIgnoreCase);
        foreach (Match m in HexRx().Matches(css)) Assert.Contains(m.Value, allowed);
    }

    [Fact]
    public void AppCss_NoLetterSpacingAndMinSize()
    {
        var css = Web("css", "app.css");
        foreach (Match m in Regex.Matches(css, @"letter-spacing\s*:\s*([^;}\s]+)")) Assert.Equal("0", m.Groups[1].Value);
        foreach (Match m in Regex.Matches(css, @"font-size\s*:\s*([0-9.]+)px"))
            Assert.True(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 13, $"font-size {m.Value}");
        Assert.DoesNotMatch(@"transition\s*:\s*all", css);
    }

    [GeneratedRegex(@"--(?<name>[a-z0-9-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex PropertyRx();

    [GeneratedRegex(@"#[0-9A-Fa-f]{6}\b")]
    private static partial Regex HexRx();
}
