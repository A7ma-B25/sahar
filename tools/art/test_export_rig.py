"""Keeps the exported rig, palette and expressions in step with design.py and with Core's enums.

Run from the repo root:  python3 -I -m unittest discover -s tools/art -p 'test_*.py' -v
"""
import json, os, re, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
ASSETS = os.path.join(ROOT, "src", "Sahar.Core", "assets")
FILES = ["art/sahar-rig.json", "palette.json", "expressions.json"]


def core_enum(path, name):
    text = open(os.path.join(ROOT, path), encoding="utf-8").read()
    m = re.search(r"public enum " + name + r"\s*\{([^}]*)\}", text)
    return [x.strip().split("=")[0].strip() for x in m.group(1).split(",") if x.strip()]


class ExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        os.makedirs(os.path.join(cls.tmp.name, "art"))
        subprocess.run([sys.executable, "-I", os.path.join(HERE, "export_rig.py"), "--out", cls.tmp.name], check=True, cwd=ROOT)
        cls.out = {f: open(os.path.join(cls.tmp.name, f), "rb").read() for f in FILES}
        cls.rig = json.loads(cls.out["art/sahar-rig.json"])
        cls.palette = json.loads(cls.out["palette.json"])
        cls.exprs = json.loads(cls.out["expressions.json"])

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_export_matches_committed_assets(self):
        for f in FILES:
            committed = open(os.path.join(ASSETS, f), "rb").read()
            self.assertEqual(json.loads(committed), json.loads(self.out[f]), f)
            self.assertEqual(committed, self.out[f], f + " (bytes)")

    def test_palette_has_every_core_mood(self):
        # Compared as sets: the committed palette keeps design.py's order, not the enum's.
        self.assertEqual(set(core_enum("src/Sahar.Core/Art/Palette.cs", "Mood")), set(self.palette))
        self.assertEqual(16, len(self.palette))

    def test_rig_has_every_core_eyes_and_mouth(self):
        for e in core_enum("src/Sahar.Core/Expressions/Expression.cs", "Eyes"):
            self.assertIn(e, self.rig["eyes"])
            self.assertTrue(self.rig["eyes"][e], e)
        for m in core_enum("src/Sahar.Core/Expressions/Expression.cs", "Mouth"):
            self.assertIn(m, self.rig["mouths"])
            if m != "None":
                self.assertTrue(self.rig["mouths"][m], m)
        self.assertEqual([], self.rig["mouths"]["None"])

    def test_expressions_are_contiguous_and_valid(self):
        moods = set(core_enum("src/Sahar.Core/Art/Palette.cs", "Mood"))
        eyes = set(core_enum("src/Sahar.Core/Expressions/Expression.cs", "Eyes"))
        mouths = set(core_enum("src/Sahar.Core/Expressions/Expression.cs", "Mouth"))
        self.assertEqual(list(range(len(self.exprs))), [e["id"] for e in self.exprs])
        self.assertEqual(30, len(self.exprs))
        self.assertEqual(len(self.exprs), len({e["key"] for e in self.exprs}))
        for e in self.exprs:
            self.assertIn(e["mood"], moods, e["key"])
            self.assertIn(e["eyes"], eyes, e["key"])
            self.assertIn(e["mouth"], mouths, e["key"])
        self.assertEqual(["quran", "dizzy", "sick", "angry"], [e["key"] for e in self.exprs[26:30]])


    # ---- 0.10a: the sky (L1–L3) and the joy face (L4) ----

    def test_rig_sky_matches_the_design(self):
        sky = self.rig["sky"]
        spots = lambda xs, k: [tuple(d[f] for f in k) for d in xs]
        self.assertEqual([(150, 22, 0.9, 2.6, 0), (272, 120, 0.6, 3.4, 0), (46, 70, 0.7, 2.2, 0), (240, 236, 0.5, 3, 0), (36, 214, 0.55, 2.8, 0)],
                         spots(sky["outerStars"], ("x", "y", "scale", "period", "begin")))
        self.assertEqual({"d": "M0 -8 L2 -2 L8 0 L2 2 L0 8 L-2 2 L-8 0 L-2 -2Z", "fill": "#FFE7A8", "edge": "#C9963A", "edgeWidth": 1.3}, sky["star"])
        self.assertEqual({"d": "M0 -8 L1.6 -1.6 L8 0 L1.6 1.6 L0 8 L-1.6 1.6 L-8 0 L-1.6 -1.6Z", "fill": "#FFFFFF", "edge": None, "edgeWidth": 0}, sky["innerStar"])
        self.assertEqual([(84, 176, 0.55, 2.8, 0), (108, 192, 0.42, 3.4, 1.1), (138, 182, 0.5, 2.5, 0.5), (162, 170, 0.38, 3.8, 1.8), (124, 168, 0.32, 3.1, 2.4), (178, 188, 0.4, 2.9, 0.9)],
                         spots(sky["innerStars"], ("x", "y", "scale", "period", "begin")))
        self.assertEqual([(96, 168, 1.3, 2.2, 0.3), (150, 194, 1.1, 2.9, 1.4), (70, 186, 1.0, 3.3, 0.7), (118, 200, 1.2, 2.6, 2.0), (190, 176, 0.9, 3.1, 1.0)],
                         spots(sky["innerDots"], ("x", "y", "r", "period", "begin")))
        self.assertEqual({"cx": 120, "cy": 196, "rx": 86, "ry": 44, "gradCx": 0.5, "gradCy": 0.7, "gradR": 0.6, "offsets": [0, 0.6, 1], "opacities": [0.55, 0.28, 0]}, sky["wash"])

    def test_wash_stops_are_one_reviewed_table(self):
        stops = self.rig["sky"]["washStops"]
        self.assertEqual(set(self.palette), set(stops))
        self.assertEqual(["#8E7BE0", "#A897EA", "#CBC1F0"], stops["Calm"])   # the spec's L2, never darkened
        for mood, (inner, mid, outer) in stops.items():
            for c in (inner, mid, outer):
                self.assertRegex(c, r"^#[0-9A-F]{6}$", mood)
            self.assertEqual(self.palette[mood]["bottom"], outer, mood)
            self.assertNotEqual(inner, mid, mood)

    def test_joy_face(self):
        eyes = self.rig["eyes"]["Joy"]
        self.assertEqual(["M90 127 Q 100 115 110 127", "M130 127 Q 140 115 150 127", "M86 119 l-4 -3 M154 119 l4 -3"], [s["d"] for s in eyes])
        for s in eyes[:2]:
            self.assertEqual(("#2A2350", 3.6, "round", 1.0), (s["stroke"], s["strokeWidth"], s["lineCap"], s["opacity"]))
            self.assertNotIn("fill", s)
        self.assertEqual(("#2A2350", 2.0, "round", 0.55), (eyes[2]["stroke"], eyes[2]["strokeWidth"], eyes[2]["lineCap"], eyes[2]["opacity"]))
        mouth = self.rig["mouths"]["Joy"]
        self.assertEqual([("M110 138 Q 120 152 130 138", "#2A2350", 2.6, "round")], [(s["d"], s["stroke"], s["strokeWidth"], s["lineCap"]) for s in mouth])
        self.assertNotIn("fill", mouth[0])

    def test_moods_sheet_is_current(self):
        with tempfile.TemporaryDirectory() as tmp:
            subprocess.run([sys.executable, "-I", os.path.join(HERE, "moods_sheet.py"), "--out", tmp, "--no-png"], check=True, cwd=ROOT)
            made = open(os.path.join(tmp, "06-moods-sheet.svg"), "rb").read()
        committed = open(os.path.join(ROOT, "docs", "design", "character", "06-moods-sheet.svg"), "rb").read()
        self.assertEqual(committed, made)

if __name__ == "__main__":
    unittest.main()
