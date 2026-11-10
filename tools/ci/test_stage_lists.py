"""The three stage lists must agree (review minor 6): run.ps1's ValidateSet, test-build's full UI run, and the groups
"ui-test all" runs — or a stage could be missing from the full pass that lets a publish skip test-build's own run."""
import os
import re
import unittest

from ui_groups import ALL

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8-sig") as f:
        return f.read()


class StageListsTests(unittest.TestCase):
    def test_run_ps1_knows_exactly_the_stages_ui_test_all_runs(self):
        names = re.search(r"\[ValidateSet\(([^)]*)\)\]\[string\[\]\]\$Stage", read("tests", "ui", "run.ps1")).group(1)
        self.assertEqual(sorted(ALL), sorted(re.findall(r"'([a-z]+)'", names)))

    def test_test_build_full_run_is_every_stage(self):
        yml = read(".github", "workflows", "test-build.yml")
        full = re.search(r"name: UI tests \(all stages\)[\s\S]*?-Stage ([a-z,]+)", yml).group(1).split(",")
        self.assertEqual(sorted(ALL), sorted(full))

    def test_the_short_run_after_a_full_pass_names_known_stages(self):
        yml = read(".github", "workflows", "test-build.yml")
        short = re.search(r"name: UI tests \(smoke, gallery;[\s\S]*?-Stage ([a-z,]+)", yml).group(1).split(",")
        self.assertTrue(set(short) <= set(ALL), short)
        self.assertIn("gallery", short)   # the release attaches gallery.png


if __name__ == "__main__":
    unittest.main()
