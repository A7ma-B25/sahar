using System.Globalization;
using System.Numerics;

namespace Sahar.Core.Art;

public enum PathOp { MoveTo, LineTo, CubicTo, QuadTo, ArcTo, Close }

/// <summary>
/// One absolute path command. CubicTo: P1, P2 controls, P3 end. QuadTo: P1 control, P3 end.
/// MoveTo, LineTo, ArcTo: P3 end. ArcTo also uses Radii, XRotation, LargeArc and Sweep.
/// </summary>
public readonly record struct PathCommand(
    PathOp Op, Vector2 P1, Vector2 P2, Vector2 P3,
    Vector2 Radii = default, float XRotation = 0, bool LargeArc = false, bool Sweep = false);

/// <summary>Parses SVG path data ("d") into absolute commands, normalizing H, V, S, T and relative forms.</summary>
public static class PathData
{
    public static IReadOnlyList<PathCommand> Parse(string d)
    {
        var r = new Reader(d);
        var list = new List<PathCommand>();
        Vector2 cur = default, start = default, lastCubicCtrl = default, lastQuadCtrl = default;
        char prevCmd = '\0';
        char cmd = '\0';

        while (true)
        {
            r.SkipSeparators();
            if (r.End) break;
            if (r.PeekIsCommand()) cmd = r.ReadCommand();
            else if (cmd == '\0') throw new FormatException($"Path data must start with a command: '{d}'");
            else if (cmd is 'M') cmd = 'L';      // implicit repeat after moveto is lineto
            else if (cmd is 'm') cmd = 'l';

            bool rel = char.IsLower(cmd);
            Vector2 Pt() { var p = new Vector2(r.ReadNumber(), r.ReadNumber()); return rel ? cur + p : p; }

            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    cur = Pt(); start = cur;
                    list.Add(new PathCommand(PathOp.MoveTo, default, default, cur));
                    break;
                case 'L':
                    cur = Pt();
                    list.Add(new PathCommand(PathOp.LineTo, default, default, cur));
                    break;
                case 'H':
                {
                    float x = r.ReadNumber(); cur = new Vector2(rel ? cur.X + x : x, cur.Y);
                    list.Add(new PathCommand(PathOp.LineTo, default, default, cur));
                    break;
                }
                case 'V':
                {
                    float y = r.ReadNumber(); cur = new Vector2(cur.X, rel ? cur.Y + y : y);
                    list.Add(new PathCommand(PathOp.LineTo, default, default, cur));
                    break;
                }
                case 'C':
                {
                    var c1 = Pt(); var c2 = Pt(); var e = Pt();
                    list.Add(new PathCommand(PathOp.CubicTo, c1, c2, e));
                    lastCubicCtrl = c2; cur = e;
                    break;
                }
                case 'S':
                {
                    var c1 = prevCmd is 'C' or 'S' ? 2 * cur - lastCubicCtrl : cur;
                    var c2 = Pt(); var e = Pt();
                    list.Add(new PathCommand(PathOp.CubicTo, c1, c2, e));
                    lastCubicCtrl = c2; cur = e;
                    break;
                }
                case 'Q':
                {
                    var c1 = Pt(); var e = Pt();
                    list.Add(new PathCommand(PathOp.QuadTo, c1, default, e));
                    lastQuadCtrl = c1; cur = e;
                    break;
                }
                case 'T':
                {
                    var c1 = prevCmd is 'Q' or 'T' ? 2 * cur - lastQuadCtrl : cur;
                    var e = Pt();
                    list.Add(new PathCommand(PathOp.QuadTo, c1, default, e));
                    lastQuadCtrl = c1; cur = e;
                    break;
                }
                case 'A':
                {
                    var radii = new Vector2(r.ReadNumber(), r.ReadNumber());
                    float rot = r.ReadNumber();
                    bool large = r.ReadFlag(), sweep = r.ReadFlag();
                    var e = Pt();
                    list.Add(new PathCommand(PathOp.ArcTo, default, default, e, radii, rot, large, sweep));
                    cur = e;
                    break;
                }
                case 'Z':
                    list.Add(new PathCommand(PathOp.Close, default, default, start));
                    cur = start;
                    break;
                default:
                    throw new FormatException($"Unknown path command '{cmd}'");
            }
            prevCmd = char.ToUpperInvariant(cmd);
            if (prevCmd == 'Z') cmd = '\0';
        }
        return list;
    }

    private sealed class Reader(string s)
    {
        private readonly string _s = s;
        private int _i;

        public bool End => _i >= _s.Length;

        public void SkipSeparators()
        {
            while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',')) _i++;
        }

        public bool PeekIsCommand() => !End && char.IsLetter(_s[_i]) && _s[_i] is not ('e' or 'E');

        public char ReadCommand() => _s[_i++];

        public bool ReadFlag()
        {
            SkipSeparators();
            if (End || (_s[_i] != '0' && _s[_i] != '1')) throw new FormatException("Expected arc flag 0 or 1");
            return _s[_i++] == '1';
        }

        public float ReadNumber()
        {
            SkipSeparators();
            int start = _i;
            if (_i < _s.Length && (_s[_i] is '+' or '-')) _i++;
            bool dot = false, digits = false;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (char.IsAsciiDigit(c)) { digits = true; _i++; }
                else if (c == '.' && !dot) { dot = true; _i++; }
                else break;
            }
            if (digits && _i < _s.Length && (_s[_i] is 'e' or 'E'))
            {
                _i++;
                if (_i < _s.Length && (_s[_i] is '+' or '-')) _i++;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) _i++;
            }
            if (!digits) throw new FormatException($"Expected a number at {start} in '{_s}'");
            return float.Parse(_s.AsSpan(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
