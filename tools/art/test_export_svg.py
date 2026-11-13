"""The settings page's living Sahar is drawn from design.py (plan 0.10c Task 5): one source, no drift.

Run from the repo root:  python3 -I -m unittest discover -s tools/art -p 'test_*.py' -v
"""
import os, re, subprocess, sys, tempfile, unittest
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
COMMITTED = os.path.join(ROOT, "src", "Sahar.Settings", "web", "sahar", "sahar.svg")
IDS = ["halo", "shadow", "orbit", "stars-out", "float", "squash", "leaf", "body", "night", "eyes", "eyes-joy", "mouth",
       "mouth-joy", "blush", "moon"]


class ExportSvgTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        subprocess.run([sys.executable, "-I", os.path.join(HERE, "export_svg.py"), "--out", cls.tmp.name], check=True, cwd=ROOT)
        cls.svg = open(os.path.join(cls.tmp.name, "sahar.svg"), "rb").read()

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_preview_svg_matches_export(self):
        self.assertEqual(open(COMMITTED, "rb").read(), self.svg)

    def test_preview_svg_has_ids(self):
        root = ET.fromstring(self.svg)
        ids = {el.attrib["id"] for el in root.iter() if "id" in el.attrib}
        for i in IDS:
            self.assertIn(i, ids)
        self.assertEqual(root.attrib["viewBox"], "0 0 300 300")
        ns = "{http://www.w3.org/2000/svg}"
        self.assertEqual(5, len([e for e in root.iter() if "star" in e.attrib.get("class", "").split() and e.attrib.get("data-period")]))
        self.assertEqual(6, len([e for e in root.iter() if "istar" in e.attrib.get("class", "").split()]))
        self.assertEqual(5, len([e for e in root.iter() if "idot" in e.attrib.get("class", "").split()]))

    def test_preview_svg_is_calm_and_still(self):
        text = self.svg.decode("utf-8")
        for tag in ("<animate", "<animateTransform", "<animateMotion", "<set"):
            self.assertNotIn(tag, text)          # no SMIL: the page moves her (sahar-motion.js)
        self.assertIn("#F1ECFF", text)            # Calm mid
        self.assertIn("#8E7BE0", text)            # the L2 wash, exactly
        self.assertNotRegex(text, r"https?://(?!www\.w3\.org/)")


if __name__ == "__main__":
    unittest.main()
