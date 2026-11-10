"""Bakes the mockup's hand-drawn wobble into the menu card's icons (plan 0.10b Ruling 7, owner ruling 2026-11-10).

The mockups draw every stroke icon through an SVG filter: feTurbulence (fractalNoise, baseFrequency 0.09, one octave,
seed 4) feeding feDisplacementMap (scale 1.1, R → x, G → y), in the icon's own 24-unit grid. The engine draws path
data with Direct2D, so the same kind of displacement is applied here once, seeded, to the path data itself:
each segment is sampled, every point is moved by 1.1 · (noise − 0.5) on x and y (smooth value noise at 0.09 per
unit, its own field per axis), and the moved points become cubic Béziers again (Catmull-Rom within a segment, so
corners stay corners). Circles become closed paths so they wobble too.

Usage: python3 -I tools/art/wobble_icons.py            → prints the C# dictionary body for MenuIcons.cs
No dependencies.
"""
import math
import re
import sys

SCALE = 1.1
FREQ = 0.09
SEED = 4
STEP = 1.6  # sample spacing in grid units (bumps are ~5.5 units wide)

# The clean icons (plan Task 1, copied from PMenuSoul.dc.html): (paths, circles (cx, cy, r)).
ICONS = {
    "ball": (["M4.6 9.4c4.6 1.6 10.2 1.6 14.8 0M4.8 14.8c4.6-1.4 9.8-1.4 14.4 0"], [(12, 12, 8)]),
    "bud": (["M12 19.5c-4.2-1.6-6.4-5.6-5.6-10.2 2.2.6 4.4 1.6 5.6 3.6 1.2-2 3.4-3 5.6-3.6.8 4.6-1.4 8.6-5.6 10.2z", "M12 19.5V22"], []),
    "moonZ": (["M14.5 5.2a7.6 7.6 0 1 0 4.8 12.4 6.6 6.6 0 0 1-4.8-12.4z", "M17 3.5h3.2l-3.2 3.4h3.2"], []),
    "clock": (["M12 9.5v3.8l2.4 1.6M10 3h4"], [(12, 13, 7.5)]),
    "page": (["M6 3.5h8.5l3.5 3.5v13.5H6z", "M9 11.5h6M9 15h4"], []),
    "headphones": (["M4.5 15.5v-3a7.5 7.5 0 0 1 15 0v3", "M4.5 14.5h3v5.5h-3zM16.5 14.5h3v5.5h-3z"], []),
    "board": (["M7 5h10v15.5H7z", "M9.5 3.5h5v3h-5z"], []),
    "gear": (["M12 3.5v2.5M12 18v2.5M3.5 12H6M18 12h2.5M6 6l1.8 1.8M16.2 16.2 18 18M18 6l-1.8 1.8M7.8 16.2 6 18"], [(12, 12, 3)]),
    "chevronStart": (["M15 5l-7 7 7 7"], []),
    "back": (["M9 5l7 7-7 7"], []),
    "mic": (["M9 6.5a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0z", "M6 11.5a6 6 0 0 0 12 0M12 17.5V21"], []),
    "send": (["M19 12H6M11 7l-5 5 5 5"], []),
}


# ---- smooth seeded value noise (one field per axis) ----
def _hash(ix, iy, ch):
    h = (ix * 374761393 + iy * 668265263 + (SEED * 31 + ch) * 2147483647) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF) / 65535.0


def noise(x, y, ch):
    # Value noise peaks at its lattice points, gradient (Perlin) noise between them: a lattice at twice the
    # frequency gives bumps about as wide as feTurbulence's at 0.09 (≈ 5.5 units).
    x, y = x * FREQ * 2, y * FREQ * 2
    ix, iy = math.floor(x), math.floor(y)
    fx, fy = x - ix, y - iy
    sx, sy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = _hash(ix, iy, ch), _hash(ix + 1, iy, ch)
    c, d = _hash(ix, iy + 1, ch), _hash(ix + 1, iy + 1, ch)
    return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy


def displace(p):
    return (p[0] + SCALE * (noise(p[0], p[1], 0) - 0.5), p[1] + SCALE * (noise(p[0], p[1], 1) - 0.5))


# ---- a small SVG path reader: M L H V C A Z, absolute and relative ----
def tokens(d):
    for m in re.finditer(r"[MmLlHhVvCcAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:e-?\d+)?", d):
        yield m.group(0)


def segments(d):
    """Yields subpaths: lists of segments, each a function t → point plus its length estimate, and a closed flag."""
    toks = list(tokens(d))
    i, cmd, cur, start = 0, None, (0.0, 0.0), (0.0, 0.0)
    subs, segs = [], []

    def num():
        nonlocal i
        v = float(toks[i]); i += 1
        return v

    while i < len(toks):
        if re.match(r"[A-Za-z]", toks[i]):
            cmd = toks[i]; i += 1
        elif cmd in "Mm":
            cmd = "L" if cmd == "M" else "l"
        rel = cmd.islower()
        C = cmd.upper()

        def pt():
            x, y = num(), num()
            return (cur[0] + x, cur[1] + y) if rel else (x, y)

        if C == "M":
            if segs: subs.append((segs, False)); segs = []
            cur = pt(); start = cur
        elif C == "Z":
            if cur != start: segs.append(line(cur, start))
            subs.append((segs, True)); segs = []; cur = start
        elif C == "L":
            e = pt(); segs.append(line(cur, e)); cur = e
        elif C == "H":
            x = num(); e = (cur[0] + x if rel else x, cur[1]); segs.append(line(cur, e)); cur = e
        elif C == "V":
            y = num(); e = (cur[0], cur[1] + y if rel else y); segs.append(line(cur, e)); cur = e
        elif C == "C":
            c1, c2, e = pt(), pt(), pt(); segs.append(cubic(cur, c1, c2, e)); cur = e
        elif C == "A":
            rx, ry, rot, large, sweep = num(), num(), num(), num(), num()
            e = pt(); segs.extend(arc(cur, rx, ry, rot, large, sweep, e)); cur = e
        else:
            raise ValueError(cmd)
    if segs: subs.append((segs, False))
    return subs


def line(a, b):
    return (lambda t: (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)), math.dist(a, b)


def cubic(p0, p1, p2, p3):
    def f(t):
        u = 1 - t
        return tuple(u**3 * p0[k] + 3 * u * u * t * p1[k] + 3 * u * t * t * p2[k] + t**3 * p3[k] for k in (0, 1))
    n = 16
    length = sum(math.dist(f(j / n), f((j + 1) / n)) for j in range(n))
    return f, length


def arc(p0, rx, ry, rot, large, sweep, p1):
    """SVG endpoint arc → centre form (rotation is 0 in every icon), as one segment function."""
    assert rot == 0
    x1, y1 = (p0[0] - p1[0]) / 2, (p0[1] - p1[1]) / 2
    lam = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry)
    if lam > 1: rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num_ = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1
    den = rx * rx * y1 * y1 + ry * ry * x1 * x1
    co = math.sqrt(max(0.0, num_ / den)) * (-1 if large == sweep else 1)
    cx_, cy_ = co * rx * y1 / ry, -co * ry * x1 / rx
    cx, cy = cx_ + (p0[0] + p1[0]) / 2, cy_ + (p0[1] + p1[1]) / 2
    a0 = math.atan2((y1 - cy_) / ry, (x1 - cx_) / rx)
    a1 = math.atan2((-y1 - cy_) / ry, (-x1 - cx_) / rx)
    da = a1 - a0
    if sweep and da < 0: da += 2 * math.pi
    if not sweep and da > 0: da -= 2 * math.pi
    f = lambda t: (cx + rx * math.cos(a0 + da * t), cy + ry * math.sin(a0 + da * t))
    return [(f, abs(da) * (rx + ry) / 2)]


def circle(cx, cy, r):
    f = lambda t: (cx + r * math.cos(-math.pi / 2 + 2 * math.pi * t), cy + r * math.sin(-math.pi / 2 + 2 * math.pi * t))
    return [([(f, 2 * math.pi * r)], True)]


def fmt(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


def bake(subs):
    out = []
    for segs, closed in subs:
        first = True
        for f, length in segs:
            n = max(2, math.ceil(length / STEP))
            pts = [displace(f(j / n)) for j in range(n + 1)]
            if first:
                out.append(f"M{fmt(pts[0][0])} {fmt(pts[0][1])}")
                first = False
            for j in range(n):
                p0, p1 = pts[j], pts[j + 1]
                pm = pts[j - 1] if j > 0 else (2 * p0[0] - p1[0], 2 * p0[1] - p1[1])
                p2 = pts[j + 2] if j + 2 <= n else (2 * p1[0] - p0[0], 2 * p1[1] - p0[1])
                c1 = (p0[0] + (p1[0] - pm[0]) / 6, p0[1] + (p1[1] - pm[1]) / 6)
                c2 = (p1[0] - (p2[0] - p0[0]) / 6, p1[1] - (p2[1] - p0[1]) / 6)
                out.append(f"C{fmt(c1[0])} {fmt(c1[1])} {fmt(c2[0])} {fmt(c2[1])} {fmt(p1[0])} {fmt(p1[1])}")
        if closed: out.append("Z")
    return "".join(out)


def main():
    lines = []
    for key, (paths, circles) in ICONS.items():
        baked = [bake(segments(p)) for p in paths] + [bake(circle(*c)) for c in circles]
        body = ", ".join(f'"{b}"' for b in baked)
        lines.append(f'        ["{key}"] = new MenuIcon([{body}], []),')
    sys.stdout.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
