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


if __name__ == "__main__":
    unittest.main()
