using System.Globalization;

namespace Sahar.Core.Update;

/// <summary>A semantic version (semver 2.0): MAJOR.MINOR.PATCH, an optional "-prerelease", "+build" ignored, an
/// optional leading "v". Compared by semver §11 (a prerelease is older than its release).</summary>
public readonly record struct SemVersion(int Major, int Minor, int Patch, string? Pre) : IComparable<SemVersion>
{
    public static bool TryParse(string? s, out SemVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(s)) return false;
        if (s[0] is 'v' or 'V') s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
            if (pre.Length == 0 || pre.Split('.').Any(id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
                return false;
        }
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        var n = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var p = parts[i];
            if (p.Length == 0 || !p.All(char.IsAsciiDigit) || (p.Length > 1 && p[0] == '0')) return false;
            if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return false;
        }
        version = new SemVersion(n[0], n[1], n[2], pre);
        return true;
    }

    public int CompareTo(SemVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre == other.Pre) return 0;
        if (Pre is null) return 1;          // a release is newer than its prereleases
        if (other.Pre is null) return -1;
        var a = Pre.Split('.');
        var b = other.Pre.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            int d = an && bn
                ? (a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]))
                : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (d != 0) return Math.Sign(d);
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre is null ? "" : "-" + Pre);
}
