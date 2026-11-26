using System.Globalization;
using System.Text;

namespace Sahar.Core.Surfaces;

/// <summary>
/// A tiny AOT-safe JSON writer for surface models (the engine has no reflection serializer): S/N/B/T write a property,
/// O/A open an object/array, Item opens an object inside an array, V writes a string inside an array, End closes the
/// innermost, ToString closes the rest. &lt; &gt; &amp; and line separators are escaped.
/// </summary>
public sealed class JModel
{
    private readonly StringBuilder _b = new();
    private readonly Stack<(char Close, bool First)> _open = new();

    public JModel() { _b.Append('{'); _open.Push(('}', true)); }

    /// <summary>A model with its <c>kind</c> set (the views pick the view by it).</summary>
    public static JModel Of(string kind) => new JModel().S("kind", kind);

    private void Sep()
    {
        var top = _open.Pop();
        if (!top.First) _b.Append(',');
        _open.Push((top.Close, false));
    }

    private void Name(string key) { Sep(); Str(key); _b.Append(':'); }

    public JModel S(string key, string? value) { if (value is null) return this; Name(key); Str(value); return this; }
    public JModel N(string key, double value) { Name(key); _b.Append(value.ToString("0.###", CultureInfo.InvariantCulture)); return this; }
    public JModel B(string key, bool value) { Name(key); _b.Append(value ? "true" : "false"); return this; }
    /// <summary>Only when true (a missing flag reads as false).</summary>
    public JModel T(string key, bool value) => value ? B(key, true) : this;
    public JModel O(string key) { Name(key); _b.Append('{'); _open.Push(('}', true)); return this; }
    public JModel A(string key) { Name(key); _b.Append('['); _open.Push((']', true)); return this; }
    public JModel Item() { Sep(); _b.Append('{'); _open.Push(('}', true)); return this; }
    public JModel V(string value) { Sep(); Str(value); return this; }
    public JModel End() { _b.Append(_open.Pop().Close); return this; }

    private void Str(string s)
    {
        _b.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': _b.Append("\\\""); break;
                case '\\': _b.Append("\\\\"); break;
                case '\n': _b.Append("\\n"); break;
                case '\r': _b.Append("\\r"); break;
                case '\t': _b.Append("\\t"); break;
                case '<': _b.Append("\\u003C"); break;
                case '>': _b.Append("\\u003E"); break;
                case '&': _b.Append("\\u0026"); break;
                case '\u2028': _b.Append("\\u2028"); break;
                case '\u2029': _b.Append("\\u2029"); break;
                default:
                    if (c < 0x20) _b.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else _b.Append(c);
                    break;
            }
        }
        _b.Append('"');
    }

    public override string ToString()
    {
        var copy = new StringBuilder(_b.ToString());
        foreach (var (close, _) in _open) copy.Append(close);
        return copy.ToString();
    }
}
