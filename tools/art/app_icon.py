"""Sahar's app icon from the rig (tools/art/design.py): src/Sahar.Engine/assets/sahar.ico and a preview sheet.

Run from the repo root:  python3 -I tools/art/app_icon.py   (needs rsvg-convert and Pillow; the outputs are committed)

Every size is drawn on its own (PNG entries): 40 px and up are the real Calm Sahar (body, leaf, inner night, face)
cropped to her, with a soft purple edge; 16–32 px keep her body shape and colours but give the eyes more room and a
dark one-pixel edge, so she reads on a white taskbar as well as a black one. One drawing for the engine, the setup,
the Start-menu shortcut, Settings → Apps and the tray.
"""
import os, re, shutil, struct, subprocess, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import design as D

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ICO = os.path.join(ROOT, "src", "Sahar.Engine", "assets", "sahar.ico")
SHEET = os.path.join(ROOT, "docs", "design", "icon", "icon-sheet.png")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
INK = "#2A2350"      # her eyes' colour: the small sizes' edge
EDGE = "#7A68C8"     # a soft purple edge for the large sizes
C = D.MOODS["calm"]


def still(svg):
    return re.sub(r"<animate(Transform|Motion)?\b[^>]*/>", "", svg)


def shared_defs():
    return D.SHARED.replace('<svg width="0" height="0" style="position: absolute" aria-hidden="true">', "").replace("</svg>", "")


def large(size):
    """The real rig, cropped to her body and leaf (canvas 48..252 × 38..242), no halo, moon or outer stars."""
    D._n[0] = 0
    art = still(D.sahar("calm", moon=False, stars=False, floaty=False, leaf_anim=False))
    art = re.sub(r'<circle cx="150" cy="150" r="120"[^>]*>(</circle>)?', "", art)   # the halo would fill the square
    art = re.sub(r"^<svg[^>]*>", "", art).replace("</svg>", "")
    w = 204 / size * (1.6 if size >= 128 else 1.2)
    edge = f'<path d="{D.BODY}" transform="translate(30 30)" fill="none" stroke="{EDGE}" stroke-width="{w:.2f}" opacity="0.9"/>'
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="48 38 204 204">'
            f'<defs>{shared_defs()}</defs>{art}{edge}</svg>')


def small(size):
    """Her body shape and colours at 16–32 px: bigger eyes, a 1 px dark edge; her leaf and mouth from 24 px."""
    px = 168 / size
    eye_rx, eye_ry = 13, 16
    face = (f'<ellipse cx="100" cy="128" rx="{eye_rx}" ry="{eye_ry}" fill="{INK}"/><ellipse cx="140" cy="128" rx="{eye_rx}" ry="{eye_ry}" fill="{INK}"/>')
    if size >= 24:
        face += '<circle cx="104" cy="121" r="4.6" fill="#FFFFFF"/><circle cx="144" cy="121" r="4.6" fill="#FFFFFF"/>'
        face += f'<path d="M111 152 q9 7 18 0" fill="none" stroke="{INK}" stroke-width="{max(4.0, px * 0.9):.2f}" stroke-linecap="round"/>'
    leaf = ""
    box = "36 48 168 168"
    if size >= 24:   # her leaf, solid (a gradient is mush at this size), from 24 px
        box, px = "20 10 200 200", 200 / size
        leaf = (f'<path d="M120 60 C 120 48, 121 38, 124 28" fill="none" stroke="#4F8F62" stroke-width="{px * 1.1:.2f}" stroke-linecap="round"/>'
                f'<path d="M123 38 C 106 40, 94 30, 92 16 C 108 14, 120 24, 123 38 Z" fill="#7CC08E" stroke="#3F7F52" stroke-width="{px * 0.6:.2f}"/>'
                f'<path d="M124 32 C 138 30, 150 20, 152 8 C 136 8, 126 18, 124 32 Z" fill="#7CC08E" stroke="#3F7F52" stroke-width="{px * 0.6:.2f}"/>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="{box}">'
            f'<defs><radialGradient id="b" cx="38%" cy="26%" r="90%"><stop offset="0" stop-color="{C["top"]}"/>'
            f'<stop offset="0.55" stop-color="{C["mid"]}"/><stop offset="1" stop-color="{C["bot"]}"/></radialGradient></defs>'
            f'{leaf}<path d="{D.BODY}" fill="url(#b)" stroke="{INK}" stroke-width="{px:.2f}" stroke-linejoin="round"/>{face}</svg>')


def render(svg, size, tmp):
    src, out = os.path.join(tmp, f"{size}.svg"), os.path.join(tmp, f"{size}.png")
    with open(src, "w", encoding="utf-8") as f:
        f.write(svg)
    subprocess.run([shutil.which("rsvg-convert"), "-w", str(size), "-h", str(size), "-o", out, src], check=True)
    return out


def write_ico(pngs, path):
    blobs = [open(p, "rb").read() for _s, p in pngs]
    head = struct.pack("<HHH", 0, 1, len(blobs))
    offset = 6 + 16 * len(blobs)
    table = b""
    for (s, _p), b in zip(pngs, blobs):
        table += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(b), offset)
        offset += len(b)
    with open(path, "wb") as f:
        f.write(head + table + b"".join(blobs))


def sheet(pngs, path):
    from PIL import Image, ImageDraw
    pad, zoom = 16, 6
    bgs = [("light taskbar", (243, 243, 243)), ("dark taskbar", (32, 32, 32))]
    big = [s for s in (16, 20, 24, 32)]
    w = pad + sum(s + pad for s, _ in pngs) + sum(s * zoom + pad for s in big) + 120
    row = 256 + 2 * pad + 18
    im = Image.new("RGB", (w, row * len(bgs)), (255, 255, 255))
    d = ImageDraw.Draw(im)
    for r, (name, bg) in enumerate(bgs):
        y0 = r * row
        d.rectangle([0, y0, w, y0 + row - 1], fill=bg)
        ink = (40, 40, 40) if r == 0 else (220, 220, 220)
        d.text((pad, y0 + 4), name, fill=ink)
        x = pad
        for s, p in pngs:
            icon = Image.open(p).convert("RGBA")
            im.paste(icon, (x, y0 + 22), icon)
            d.text((x, y0 + 26 + s), f"{s}", fill=ink)
            x += s + pad
        for s in big:   # the small ones enlarged, pixel for pixel
            icon = Image.open(dict(pngs)[s]).convert("RGBA").resize((s * zoom, s * zoom), Image.NEAREST)
            im.paste(icon, (x, y0 + 22), icon)
            d.text((x, y0 + 26 + s * zoom), f"{s} x{zoom}", fill=ink)
            x += s * zoom + pad
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path, optimize=True)


def main():
    if not shutil.which("rsvg-convert"):
        sys.exit("rsvg-convert not found")
    with tempfile.TemporaryDirectory() as tmp:
        pngs = [(s, render(small(s) if s <= 32 else large(s), s, tmp)) for s in SIZES]
        write_ico(pngs, ICO)
        sheet(pngs, SHEET)
    print("wrote", ICO, "and", SHEET)


if __name__ == "__main__":
    main()
