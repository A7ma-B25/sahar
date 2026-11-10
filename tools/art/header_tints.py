"""Proposes the menu card's header tints, one per mood and theme, and draws the 16-mood sheet for the owner's review
(spec §0.3 / §5: a reviewed hex table, literal values in Core's Tokens.HeaderTints; no colour maths in C#).

Proposal (UI study §5.1): light = palette[mood].mid, mixed with white until `ink` and `moon` both read ≥ 4.5 : 1
(Sleepy, Mystic, Night need it); dark = 16 % palette[mood].bottom over `menu` (#2A2630). Calm is fixed by the spec:
#EEE8FF / #3A3450.

Usage: python3 -I tools/art/header_tints.py <palette.json> <out-dir>
Prints the C# table and writes header-tints.svg (render it to PNG with rsvg-convert).
"""
import json
import os
import sys

INK = {"light": "#24212B", "dark": "#EFECF4"}
MOON = {"light": "#7A5A1C", "dark": "#F2C86A"}
MENU = {"light": "#FFFFFF", "dark": "#2A2630"}
STAR = {"light": "#B9A4F0", "dark": "#E9D9A6"}
FIXED = {"Calm": ("#EEE8FF", "#3A3450")}
ORDER = ["Calm", "Happy", "Focus", "Excited", "Sleepy", "Mystic", "Alert", "Dim", "Error", "Online", "Hot", "Cold", "Love", "Night", "Sick", "Angry"]


def rgb(h): return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))
def hexc(c): return "#%02X%02X%02X" % tuple(max(0, min(255, round(v))) for v in c)
def mix(a, b, t): return tuple(x + (y - x) * t for x, y in zip(a, b))


def lum(c):
    def ch(v):
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (ch(v) for v in c)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a, b):
    la, lb = lum(a), lum(b)
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)


def reads(tint, theme):
    return min(contrast(rgb(INK[theme]), tint), contrast(rgb(MOON[theme]), tint))


def propose(palette):
    out = {}
    for mood in ORDER:
        if mood in FIXED:
            out[mood] = FIXED[mood]
            continue
        p = palette[mood]
        light = rgb(p["mid"])
        t = 0.0
        while reads(mix(light, (255, 255, 255), t), "light") < 4.5 or lum(mix(light, (255, 255, 255), t)) < 0.70:
            t += 0.05
        light = mix(light, (255, 255, 255), t)
        dark = mix(rgb(MENU["dark"]), rgb(p["bottom"]), 0.16)
        while reads(dark, "dark") < 4.5:
            dark = mix(dark, rgb(MENU["dark"]), 0.2)
        out[mood] = (hexc(light), hexc(dark))
    return out


def sheet(tints, path):
    w, h, cols = 220, 96, 4
    rows = (len(ORDER) + cols - 1) // cols
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{cols * w * 2 + 40}" height="{rows * h + 60}" font-family="Rubik, sans-serif">',
             f'<rect width="100%" height="100%" fill="#DCD9E3"/>']
    for half, theme in enumerate(("light", "dark")):
        x0 = 20 + half * cols * w
        parts.append(f'<rect x="{x0 - 8}" y="8" width="{cols * w}" height="{rows * h + 44}" rx="22" fill="{MENU[theme]}"/>')
        parts.append(f'<text x="{x0 + 6}" y="32" font-size="14" fill="{INK[theme]}">{theme}</text>')
        for i, mood in enumerate(ORDER):
            x, y = x0 + (i % cols) * w, 44 + (i // cols) * h
            tint = tints[mood][half]
            parts.append(f'<rect x="{x}" y="{y}" width="{w - 12}" height="{h - 12}" rx="14" fill="{tint}"/>')
            for sx, sy, r in ((18, 16, 1.5), (34, 30, 1), (26, 50, 1.25)):
                parts.append(f'<circle cx="{x + sx}" cy="{y + sy}" r="{r}" fill="{STAR[theme]}"/>')
            parts.append(f'<text x="{x + w - 26}" y="{y + 34}" font-size="17" font-weight="600" text-anchor="start" direction="rtl" fill="{INK[theme]}">شو بدك نعمل؟</text>')
            parts.append(f'<circle cx="{x + w - 30}" cy="{y + 55}" r="4" fill="#F2C86A"/>')
            parts.append(f'<text x="{x + w - 40}" y="{y + 60}" font-size="14" font-weight="500" text-anchor="start" direction="rtl" fill="{MOON[theme]}">العصر بعد ٤٢ دقيقة</text>')
            parts.append(f'<text x="{x + 10}" y="{y + h - 20}" font-size="11" fill="{INK[theme]}">{mood} {tint} ink {contrast(rgb(INK[theme]), rgb(tint)):.1f} moon {contrast(rgb(MOON[theme]), rgb(tint)):.1f}</text>')
    parts.append("</svg>")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(parts))


def main():
    palette = json.load(open(sys.argv[1], encoding="utf-8"))
    tints = propose(palette)
    sheet(tints, os.path.join(sys.argv[2], "header-tints.svg"))
    for mood in ORDER:
        l, d = tints[mood]
        print(f'        [Mood.{mood}] = (Rgba.Parse("{l}"), Rgba.Parse("{d}")),')


if __name__ == "__main__":
    main()
