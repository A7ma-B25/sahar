"""The test build's README names the zip of this version (review 0.11 #12: it still said 0.10.1)."""
import os
import re
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8-sig") as f:
        return f.read()


class ReadmeTests(unittest.TestCase):
    def test_every_zip_name_is_this_version(self):
        version = re.search(r"<Version>([^<]+)</Version>", read("Directory.Build.props")).group(1)
        names = re.findall(r"Sahar-([0-9.]+)-test-win-x64\.zip", read("packaging", "README-test-build.txt"))
        self.assertTrue(names)
        self.assertEqual({version}, set(names))

    def test_every_setup_name_is_this_version(self):
        version = re.search(r"<Version>([^<]+)</Version>", read("Directory.Build.props")).group(1)
        for doc in (("packaging", "README-test-build.txt"), ("README.md",)):
            names = re.findall(r"Sahar-Setup-([0-9.]+)\.exe", read(*doc))
            self.assertTrue(names, doc)
            self.assertEqual({version}, set(names), doc)

    def test_the_smartscreen_steps_are_in_arabic(self):
        for doc in (("packaging", "README-test-build.txt"), ("README.md",)):
            text = read(*doc)
            self.assertIn("More info", text, doc)
            self.assertIn("Run anyway", text, doc)
            self.assertIn("SmartScreen", text, doc)
            self.assertIn("دوّر على تحديث", read("packaging", "README-test-build.txt"))


if __name__ == "__main__":
    unittest.main()
