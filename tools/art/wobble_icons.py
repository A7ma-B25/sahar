"""Bakes the mockup's hand-drawn wobble into the menu card's icons (plan 0.10b Ruling 7, owner ruling 2026-11-10).

The mockups draw every stroke icon through an SVG filter: feTurbulence (fractalNoise, baseFrequency 0.09, one octave,
seed 4) feeding feDisplacementMap (scale 1.1, R → x, G → y), in the icon's own 24-unit grid. The engine draws path
data with Direct2D, so the same kind of displacement is applied here once, seeded, to the path data itself:
each segment is sampled, every point is moved by 1.1 · (noise − 0.5) on x and y (smooth value noise at 0.09 per
unit, its own field per axis), and the moved points become cubic Béziers again (Catmull-Rom within a segment, so
corners stay corners). Circles become closed paths so they wobble too.

Usage: python3 -I tools/art/wobble_icons.py               → prints the C# dictionary body for MenuIcons.cs
       python3 -I tools/art/wobble_icons.py --activities  → the same for ActivityIcons.cs (0.13, from ActTag/ActCard)
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


# The activities' clean icons (0.13, copied from ActTag.dc.html / ActCard.dc.html; same filter).
ACT_ICONS = {
    "play": (["M8.6 6.4v11.2c0 .5.5.8.9.5l8.3-5.6c.4-.3.4-.8 0-1.1L9.5 5.9c-.4-.3-.9 0-.9.5z"], []),
    "pause": (["M9 6.2v11.6M15 6.2v11.6"], []),
    "prev": (["M6.8 6v12M17.8 6.6v10.8L10 12z"], []),
    "next": (["M17.2 6v12M6.2 6.6v10.8L14 12z"], []),
    "sun": (["M12 8.3a3.7 3.7 0 1 0 0 7.4 3.7 3.7 0 0 0 0-7.4zM12 3.2v1.9M12 18.9v1.9M3.2 12h1.9M18.9 12h1.9M5.8 5.8l1.3 1.3M16.9 16.9l1.3 1.3M18.2 5.8l-1.3 1.3M7.1 16.9l-1.3 1.3"], []),
    "cloud": (["M7.6 16.5h9a3.3 3.3 0 0 0 .4-6.6 5 5 0 0 0-9.6-1A3.8 3.8 0 0 0 7.6 16.5z"], []),
    "rain": (["M7.6 14h9a3.3 3.3 0 0 0 .4-6.6 5 5 0 0 0-9.6-1A3.8 3.8 0 0 0 7.6 14zM9 17.2l-.9 2.3M12.6 17.2l-.9 2.3M16.2 17.2l-.9 2.3"], []),
    "wind": (["M3.5 9.2h10a2.6 2.6 0 1 0-2.6-2.6M3.5 13h14.2a2.6 2.6 0 1 1-2.6 2.6M3.5 16.8h6.4"], []),
    "terminal": (["M4.4 5.4h15.2v13.2H4.4zM8 10l2.6 2.1L8 14.2M12.8 14.4h3.6"], []),
    "bell": (["M12 4.4a5.8 5.8 0 0 0-5.8 5.8v3.6L4.7 16.4h14.6l-1.5-2.6v-3.6A5.8 5.8 0 0 0 12 4.4zM10 19.2a2 2 0 0 0 4 0"], []),
    "check": (["M5.6 12.6l4 3.9 8.8-9"], []),
    "stack": (["M4.4 8.6 12 4.6l7.6 4L12 12.6zM4.4 12.6l7.6 4 7.6-4M4.4 16.4l7.6 4 7.6-4"], []),
    "copy": (["M8.6 8.6h10.2v11H8.6zM15.4 8.6V5.4H5.2v11h3.4"], []),
    "speaker": (["M4.8 9.6h3.1l4.6-4v12.8l-4.6-4H4.8zM15.6 9.4a3.6 3.6 0 0 1 0 5.2M18.1 6.9a7.2 7.2 0 0 1 0 10.2"], []),
    "battery": (["M3.8 8.2h14.4v7.6H3.8zM20.4 10.6v2.8M6.2 10.6v2.8"], []),
    "bolt": (["M13.2 3.6 6.6 13h5.1l-1 7.4L17.4 11h-5.2z"], []),
    "camera": (["M3.8 8.1h11.4v7.8H3.8zM15.2 11l5-3v8l-5-3"], []),
    "mic": (["M9 6.5a3 3 0 0 1 6 0v5a3 3 0 0 1-6 0zM6 11.5a6 6 0 0 0 12 0M12 17.5V21"], []),
    "hourglass": (["M7 4h10M7 20h10M8.2 4v1.6c0 2.6 3.8 4 3.8 6.4 0-2.4 3.8-3.8 3.8-6.4V4M8.2 20v-1.6c0-2.6 3.8-4 3.8-6.4 0 2.4 3.8 3.8 3.8 6.4V20"], []),
    "lock": (["M7.4 11V8.2a4.6 4.6 0 0 1 9.2 0V11M5.6 11h12.8v9H5.6z"], []),
    "share": (["M12 15.2V4.6M8.2 8.2 12 4.4l3.8 3.8M5.6 12.4v7.2h12.8v-7.2"], []),
    "text": (["M5.2 6.4V4.8h13.6v1.6M12 4.8v14.6M9.6 19.4h4.8"], []),
    "shrink": (["M14 4.6h5.4V10M10 19.4H4.6V14M19.4 4.6 14 10M4.6 19.4 10 14"], []),
    "zip": (["M6.2 3.6h8.4l3.4 3.4v13.4H6.2zM11.2 4v2.2M11.2 7.6v2.2M11.2 11.2v2.2M10.2 14.6h2v2.6h-2z"], []),
    "notes": (["M9.2 17.4V6.6l9-2v10.8M9.2 17.4a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0zM18.2 15.4a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0z"], []),
    "open": (["M14 4.6h5.4V10M19.4 4.6 11 13M17 13.6v5.8H4.6V7h5.8"], []),
    "sweep": (["M5.2 7.2h13.6M10 7.2V4.8h4v2.4M7 7.2l1 12.2h8l1-12.2"], []),
    "thermo": (["M10 14.4V5.2a2 2 0 0 1 4 0v9.2a3.5 3.5 0 1 1-4 0z"], []),
    "snow": (["M12 3.8v16.4M4.9 7.9l14.2 8.2M19.1 7.9 4.9 16.1"], []),
    "leaf": (["M12 19.5c-4.2-1.6-6.4-5.6-5.6-10.2 2.2.6 4.4 1.6 5.6 3.6 1.2-2 3.4-3 5.6-3.6.8 4.6-1.4 8.6-5.6 10.2zM12 19.5V22"], []),
    "file": (["M6.2 3.6h8.4l3.4 3.4v13.4H6.2z"], []),
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
    for key, (paths, circles) in (ACT_ICONS if "--activities" in sys.argv else ICONS).items():
        baked = [bake(segments(p)) for p in paths] + [bake(circle(*c)) for c in circles]
        body = ", ".join(f'"{b}"' for b in baked)
        lines.append(f'        ["{key}"] = new MenuIcon([{body}], []),')
    sys.stdout.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
