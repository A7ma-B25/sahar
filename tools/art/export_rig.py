"""Exports Sahar's rig, palette and her 30 expressions from design.py to the Core assets.

Run from the repo root:  python3 tools/art/export_rig.py [--out DIR]   (default src/Sahar.Core/assets)
"""
import argparse, json, os, re, sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(__file__))
import design as D

OUT = os.path.join("src", "Sahar.Core", "assets")
SCAN_EYES = ('<ellipse cx="100" cy="124" rx="10" ry="12" fill="#2A2350"/><ellipse cx="140" cy="124" rx="10" ry="12" fill="#2A2350"/>'
             '<circle cx="100" cy="124" r="5" fill="#7FE3D8"/><circle cx="140" cy="124" r="5" fill="#7FE3D8"/>'
             '<circle cx="102" cy="120" r="2" fill="#FFFFFF"/><circle cx="142" cy="120" r="2" fill="#FFFFFF"/>')

def shapes(fragment, inherited=None):
    """SVG fragment -> list of rig shapes (animations stripped, group paint inherited)."""
    root = ET.fromstring(f'<g xmlns="http://www.w3.org/2000/svg">{fragment}</g>')
    out = []
    def walk(el, paint):
        tag = el.tag.split('}')[-1]
        p = dict(paint)
        for k in ("fill", "stroke", "stroke-width", "stroke-linecap", "opacity"):
            if k in el.attrib: p[k] = el.attrib[k]
        if tag in ("animate", "animateTransform", "animateMotion"): return
        if tag in ("g", "svg"):
            for c in el: walk(c, p)
            return
        s = {"kind": tag}
        if tag == "path": s["d"] = el.attrib["d"]
        elif tag == "ellipse": s.update(cx=float(el.attrib["cx"]), cy=float(el.attrib["cy"]), rx=float(el.attrib["rx"]), ry=float(el.attrib["ry"]))
        elif tag == "circle": s.update(cx=float(el.attrib["cx"]), cy=float(el.attrib["cy"]), r=float(el.attrib["r"]))
        else: return
        fill = p.get("fill"); stroke = p.get("stroke")
        if fill and fill != "none": s["fill"] = fill
        if stroke and stroke != "none":
            s["stroke"] = stroke
            s["strokeWidth"] = float(p.get("stroke-width", "1"))
            s["lineCap"] = p.get("stroke-linecap", "butt")
        s["opacity"] = float(p.get("opacity", "1"))
        out.append(s)
    for c in root: walk(c, inherited or {})
    return out

EYES = {"Open": "open", "Happy": "happy", "Closed": "closed", "Focus": "focus", "Sparkle": "sparkle", "Mystic": "mystic",
        "Worried": "worried", "Wide": "wide", "Look": "look", "Up": "up", "Wink": "wink", "Half": "half", "Dizzy": "dizzy",
        "Heart": "heart", "Sad": "sad"}
MOUTHS = {"Smile": "smile", "Grin": "grin", "Flat": "flat", "O": "o", "Sleepy": "sleepy", "Mystic": "mystic",
          "Wobble": "wobble", "BigO": "bigo", "Frown": "frown", "Tongue": "tongue", "Puff": "puff", "Sing": "sing", "Yawn": "yawn"}

def rig():
    eyes = {k: shapes(D.eyes(v)) for k, v in EYES.items()}
    eyes["Scan"] = shapes(SCAN_EYES)
    mouths = {k: shapes(D.mouth(v)) for k, v in MOUTHS.items()}
    mouths["None"] = []
    parts = {
        "body": [{"kind": "path", "d": D.BODY, "fill": "$body", "opacity": 1.0}],
        "bodyHem2": [{"kind": "path", "d": D.BODY2, "fill": "$body", "opacity": 1.0}],
        "pleats": shapes('<g fill="none" stroke="$bottom" stroke-width="1.8" stroke-linecap="round" opacity="0.4"><path d="M120 62 q-9 3 -16 10"/><path d="M120 62 q9 3 16 10"/></g>'),
        "highlight": shapes('<path d="M58 104 C 70 78, 92 66, 112 64" fill="none" stroke="#FFFFFF" stroke-width="7" stroke-linecap="round" opacity="0.75"/>'),
        "blushL": [{"kind": "ellipse", "cx": 78, "cy": 142, "rx": 12, "ry": 6.5, "fill": "$blush", "opacity": 0.55}],
        "blushR": [{"kind": "ellipse", "cx": 162, "cy": 142, "rx": 12, "ry": 6.5, "fill": "$blush", "opacity": 0.55}],
        "leafStem": shapes('<path d="M120 60 C 120 48, 121 38, 124 28" fill="none" stroke="#5E9E70" stroke-width="4" stroke-linecap="round"/>'),
        "leafA": [{"kind": "path", "d": "M123 38 C 106 40, 94 30, 92 16 C 108 14, 120 24, 123 38 Z", "fill": "$leaf", "opacity": 1.0}],
        "leafVein": shapes('<path d="M123 38 C 112 34, 102 26, 96 18" fill="none" stroke="#8CC795" stroke-width="1.2"/>'),
        "leafB": [{"kind": "path", "d": "M124 32 C 138 30, 150 20, 152 8 C 136 8, 126 18, 124 32 Z", "fill": "$leaf", "opacity": 1.0}],
        "bud": [{"kind": "circle", "cx": 124, "cy": 27, "r": 4, "fill": "$bud", "opacity": 1.0},
                {"kind": "circle", "cx": 123, "cy": 26, "r": 1.4, "fill": "#FFFFFF", "opacity": 1.0}],
        "headphones": shapes('<path d="M50 128 C 48 74, 192 74, 190 128" fill="none" stroke="#2F3363" stroke-width="7" stroke-linecap="round"/>'
                             '<path d="M36 123 a9 9 0 0 1 9 -9 h2 a9 9 0 0 1 9 9 v16 a9 9 0 0 1 -9 9 h-2 a9 9 0 0 1 -9 -9 Z" fill="#2F3363"/>'
                             '<path d="M184 123 a9 9 0 0 1 9 -9 h2 a9 9 0 0 1 9 9 v16 a9 9 0 0 1 -9 9 h-2 a9 9 0 0 1 -9 -9 Z" fill="#2F3363"/>'),
    }
    return {"canvas": 300, "offsetX": 30, "offsetY": 30, "bodyWidth": 160,
            "leafGradient": ["#C4ECC2", "#6FB683"], "parts": parts, "eyes": eyes, "mouths": mouths}

def palette():
    names = {"calm": "Calm", "happy": "Happy", "focus": "Focus", "excited": "Excited", "sleepy": "Sleepy", "mystic": "Mystic",
             "alert": "Alert", "dim": "Dim", "error": "Error", "online": "Online", "hot": "Hot", "cold": "Cold", "love": "Love", "night": "Night",
             "sick": "Sick", "angry": "Angry"}
    return {names[k]: {"top": v["top"], "mid": v["mid"], "bottom": v["bot"], "glow": v["glow"], "bud": v["bud"], "blush": v["blush"]}
            for k, v in D.MOODS.items()}

# (id, key, mood, eyes, mouth, orbGlyph, orbTop, orbBottom, extras) — expressions board 1–25 (spec §5), then 26–29
EXPR = [
    (0, "idle", "Calm", "Open", "Smile", None, None, None, []),
    (1, "music", "Happy", "Happy", "Smile", "🎵", "#C77AD6", "#5A2468", ["Headphones", "Notes"]),
    (2, "claude-working", "Focus", "Focus", "Flat", "•••", "#4A50A0", "#171A40", []),
    (3, "claude-permission", "Alert", "Open", "O", "؟", "#F7C29E", "#D77E50", []),
    (4, "claude-done", "Happy", "Happy", "Grin", "✓", "#9FE6DF", "#3E9C94", ["Hearts"]),
    (5, "timer-done", "Excited", "Open", "O", "🔔", "#FFE6A6", "#D9A23A", []),
    (6, "celebrate", "Excited", "Sparkle", "Grin", "🎆", "#8FD6A0", "#2F8A4C", ["Squish", "Confetti"]),
    (7, "rain", "Focus", "Open", "Flat", "🌧️", "#8FB4E2", "#3E5A86", ["Rain"]),
    (8, "battery-low", "Dim", "Closed", "Sleepy", "🪫", "#4A4D70", "#2A2D48", ["Grounded"]),
    (9, "charging", "Calm", "Open", "Smile", "🔋", "#4A50A0", "#171A40", []),
    (10, "download", "Calm", "Open", "Smile", "📥", "#4A50A0", "#171A40", []),
    (11, "sleep", "Sleepy", "Closed", "Sleepy", "🌙", "#3A3F7A", "#1E2350", ["Zzz"]),
    (12, "back", "Happy", "Open", "Grin", "☀️", "#FFF0B8", "#FFB45E", []),
    (13, "notification", "Happy", "Wide", "O", "📩", "#FFC7A6", "#E07A52", []),
    (14, "dnd", "Sleepy", "Closed", "Flat", "🔕", "#E36A6E", "#A9343A", []),
    (15, "offline", "Dim", "Worried", "Wobble", "📵", "#9A9DB5", "#5A5D78", ["Grounded"]),
    (16, "online", "Online", "Happy", "Grin", "🛜", "#9FE6DF", "#3E9C94", []),
    (17, "screenshot", "Calm", "Closed", "Smile", "📸", "#4A50A0", "#171A40", []),
    (18, "copied", "Calm", "Open", "Smile", "📋", "#8FB4E2", "#3E5A86", []),
    (19, "reminder", "Excited", "Open", "Smile", "📅", "#FFE6A6", "#D9A23A", []),
    (20, "error", "Error", "Worried", "BigO", "✕", "#F58A8E", "#B8343A", []),
    (21, "hot", "Hot", "Focus", "Flat", None, None, None, ["Steam"]),
    (22, "poked", "Happy", "Happy", "Grin", None, None, None, ["Squish"]),
    (23, "dragged", "Calm", "Wide", "BigO", None, None, None, []),
    (24, "break", "Happy", "Happy", "Smile", "☕", "#C9966A", "#7A4E2E", []),
    (25, "late-night", "Mystic", "Mystic", "Mystic", "✨", "#6E58C9", "#2A1C66", ["Stars"]),
    (26, "quran", "Calm", "Closed", "Smile", "📖", "#5FB38A", "#1F5A3E", ["Headphones"]),
    (27, "dizzy", "Calm", "Dizzy", "Wobble", "💫", "#FFE89A", "#C9A43A", ["Stars"]),
    (28, "sick", "Sick", "Dizzy", "Puff", "🤢", "#CDEFC2", "#5E9E4E", ["Sweat"]),
    (29, "angry", "Angry", "Half", "Puff", "💢", "#FFB3AC", "#B3261E", ["Steam"]),
]

def expressions():
    return [{"id": i, "key": k, "mood": m, "eyes": e, "mouth": mo, "orbGlyph": g, "orbTop": t, "orbBottom": b,
             "extras": ", ".join(x) if x else "None"} for i, k, m, e, mo, g, t, b, x in EXPR]

def write(name, data):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write("\n")

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    OUT = ap.parse_args().out
    os.makedirs(os.path.join(OUT, "art"), exist_ok=True)
    write("art/sahar-rig.json", rig())
    write("palette.json", palette())
    write("expressions.json", expressions())
    print("exported rig, palette, expressions")
