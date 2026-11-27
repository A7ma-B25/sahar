"""ui-test `stages=auto`: changed paths → the stages that can see them (tools/ci/ui_stages.py)."""
import os
import re
import unittest

from ui_groups import ALL
from ui_stages import RUN_PS1, run_ps1_regions, select, touches_brain

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8-sig") as f:
        return f.read()


REGIONS = run_ps1_regions(read(*RUN_PS1.split("/")))


def line_in(stage, offset=2):
    start = next(a for s, a, b in REGIONS if s == stage)
    return start + offset


class UiStagesTests(unittest.TestCase):
    def test_a_no_op_change_runs_only_smoke(self):
        self.assertEqual(["smoke"], select({"docs/design/notes.md": None}))
        self.assertEqual(["smoke"], select({"README.md": None, "tools/ci/ui_groups.py": None, "tests/web/a.test.mjs": None}))
        self.assertEqual(["smoke"], select({}))

    def test_her_brain_runs_the_mind_stage(self):
        for path in ("src/Sahar.Mind/MindHost.cs", "src/Sahar.Core/Mind/Models/ModelCatalog.cs", "src/Sahar.Engine/Mind/X.cs",
                     "src/Sahar.Core/assets/mind/prompt.txt", "tools/mind/pins.json", "tests/ui/mind-runtime.ps1"):
            self.assertEqual(["smoke", "mind"], select({path: None}), path)

    def test_parts_with_one_owner(self):
        self.assertEqual(["smoke", "face", "games"], select({"src/Sahar.Engine/Games/GamePlayer.cs": None}))
        self.assertEqual(["smoke", "install"], select({"packaging/installer/Sahar.iss": None}))
        self.assertEqual(["smoke", "look"], select({"tests/ui/look.ps1": None}))
        self.assertEqual(["smoke", "menu", "quranbar"], select({"src/Sahar.Core/Quran/Q.cs": None}))

    def test_the_engine_the_settings_app_and_unknown_paths_run_everything(self):
        for path in ("src/Sahar.Engine/App.cs", "src/Sahar.Settings/Program.cs", "src/Sahar.Core/Art/Rig.cs",
                     "Directory.Build.props", "global.json", "something/new.txt", "tests/ui/pipe.ps1"):
            self.assertEqual(ALL, select({path: None}), path)

    def test_run_ps1_lines_map_to_the_stage_that_holds_them(self):
        self.assertEqual(["smoke", "senses", "games"],
                         select({RUN_PS1: [line_in("games"), line_in("senses")]}, REGIONS))
        self.assertEqual(["smoke", "face"], select({RUN_PS1: [line_in("face", 10)]}, REGIONS))

    def test_run_ps1_shared_helpers_or_unknown_lines_run_everything(self):
        first = min(a for _, a, _ in REGIONS)
        self.assertEqual(ALL, select({RUN_PS1: [first - 1]}, REGIONS))   # above every stage: Send, Start-Engine, ...
        self.assertEqual(ALL, select({RUN_PS1: None}, REGIONS))

    def test_every_stage_function_is_a_stage_the_groups_run(self):
        names = {s for s, _, _ in REGIONS}
        self.assertTrue(names <= set(ALL), names - set(ALL))
        # Stage-Look and Stage-MindEval live in their own files; look.ps1 has its own rule.
        self.assertIn("function Stage-Look", read("tests", "ui", "look.ps1"))
        self.assertEqual(set(ALL) - {"look"}, names)

    def test_order_is_the_groups_order_and_smoke_first(self):
        stages = select({"tests/ui/look.ps1": None, "packaging/installer/build.ps1": None, "tools/mind/pins.json": None})
        self.assertEqual(["smoke", "install", "mind", "look"], stages)

    def test_brain_paths(self):
        self.assertTrue(touches_brain(["src/Sahar.Mind/Program.cs"]))
        self.assertTrue(touches_brain(["tools/mind/pins.json"]))
        self.assertTrue(touches_brain(["src/Sahar.Core/Mind/Models/ModelCatalog.cs"]))
        self.assertTrue(touches_brain(["tests/mind/golden/eval.jsonl"]))
        self.assertFalse(touches_brain(["src/Sahar.Engine/App.cs", "docs/x.md", "tests/ui/mind-runtime.ps1"]))

    def test_the_rules_name_only_known_stages(self):
        import ui_stages
        for prefix, stages in ui_stages.RULES:
            self.assertTrue(set(stages) <= set(ALL), prefix)
        self.assertTrue(re.match(r"^[\w./-]+$", "".join(p for p, _ in ui_stages.RULES)))


if __name__ == "__main__":
    unittest.main()
