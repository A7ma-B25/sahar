"""The 16-mood sheet: Sahar in every mood with the night wash and inner stars, for the owner to approve the wash table.

Run from the repo root:  python3 -I tools/art/moods_sheet.py [--out DIR] [--no-png]
Writes 06-moods-sheet.svg (and .png via rsvg-convert) into docs/design/character/ by default.
The SVG is a still frame (animations are left out), so it renders the same everywhere.
"""
import argparse, os, re, shutil, subprocess, sys

sys.path.insert(0, os.path.dirname(__file__))
import design as D

OUT = os.path.join("docs", "design", "character")
ORDER = ["calm", "happy", "focus", "excited", "sleepy", "hot", "mystic", "alert",
         "dim", "error", "online", "cold", "love", "night", "sick", "angry"]
CELL_W, CELL_H, COLS = 300, 372, 4
FONT = "'Rubik', 'IBM Plex Sans Arabic', 'Segoe UI', sans-serif"


def _rgb(h):
    h = h.lstrip("#")
    return [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]


def _lum(c):
    lin = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]


def _lerp(a, b, t):
    return [x + (y - x) * t for x, y in zip(a, b)]


def white_star_contrast(mood):
    """WCAG contrast of a white inner star at the wash centre (120,196): body gradient there, the wash over it."""
    c = D.MOODS[mood]
    inner, mid = D.WASH_STOPS[mood]
    body = _lerp(_rgb(c["mid"]), _rgb(c["bot"]), 0.436)   # the body's radial gradient sampled at (120,196)
    wash = _lerp(_rgb(inner), _rgb(mid), 0.555)            # 1/3 of the way out of the wash gradient, toward 0.6
    alpha = 0.55 + (0.28 - 0.55) * 0.555
    under = [w * alpha + b * (1 - alpha) for w, b in zip(wash, body)]
    l1, l2 = 1.0, _lum(under)
    return (l1 + 0.05) / (l2 + 0.05)


def still(svg):
    """Drop every SMIL animation, so the sheet is one frame."""
    return re.sub(r"<animate(Transform|Motion)?\b[^>]*/>", "", svg)


def cell(mood, i):
    x, y = (i % COLS) * CELL_W, (i // COLS) * CELL_H
    art = still(D.sahar(mood, moon=False, floaty=False, leaf_anim=False))
    art = re.sub(r"^<svg[^>]*>", f'<svg x="{x}" y="{y + 6}" width="300" height="300" viewBox="0 0 300 300" overflow="visible">', art, count=1)
    inner, mid = D.WASH_STOPS[mood]
    bot = D.MOODS[mood]["bot"]
    ty = y + 318
    chips = "".join(f'<rect x="{x + 24 + k * 86}" y="{ty + 12}" width="18" height="18" rx="5" fill="{col}"/>'
                    f'<text x="{x + 46 + k * 86}" y="{ty + 26}" font-size="11" fill="#5C5867">{col}</text>'
                    for k, col in enumerate((inner, mid, bot)))
    return (f'{art}<text x="{x + 150}" y="{ty}" text-anchor="middle" font-size="17" font-weight="600" fill="#24212B">{mood.capitalize()}'
            f'{"  (spec L2)" if mood == "calm" else ""}</text>{chips}'
            f'<text x="{x + 150}" y="{ty + 48}" text-anchor="middle" font-size="11" fill="#6B6776">white star at the centre {white_star_contrast(mood):.2f}:1</text>')


def sheet():
    D._n[0] = 0   # stable ids, so the file is the same on every run
    w, h = CELL_W * COLS, CELL_H * (len(ORDER) // COLS) + 70
    cells = "".join(cell(m, i) for i, m in enumerate(ORDER))
    shared = D.SHARED.replace('<svg width="0" height="0" style="position: absolute" aria-hidden="true">', "").replace("</svg>", "")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{w}" height="{h}" viewBox="0 0 {w} {h}" font-family="{FONT}">'
            f'{shared}<rect width="{w}" height="{h}" fill="#F5F4F8"/>{cells}'
            f'<text x="{w / 2}" y="{h - 24}" text-anchor="middle" font-size="13" fill="#5C5867">The night inside her, per mood: inner · mid · outer (= body bottom, transparent). '
            f'Calm is the approved spec; the other 15 are a proposal — one table in tools/art/design.py (WASH_STOPS).</text></svg>\n')


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--no-png", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    svg_path = os.path.join(a.out, "06-moods-sheet.svg")
    with open(svg_path, "w", encoding="utf-8") as f:
        f.write(sheet())
    if not a.no_png:
        rsvg = shutil.which("rsvg-convert")
        if not rsvg:
            sys.exit("rsvg-convert not found (the SVG was written)")
        subprocess.run([rsvg, "-w", "1200", "-o", os.path.join(a.out, "06-moods-sheet.png"), svg_path], check=True)
    print("wrote", svg_path)
