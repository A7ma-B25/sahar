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


if __name__ == "__main__":
    unittest.main()
