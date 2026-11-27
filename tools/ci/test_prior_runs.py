"""test-build skips the UI run a "ui-test all" pass already made on the same commit (tools/ci/prior_runs.py)."""
import os
import unittest

from prior_runs import FULL_TITLE, decide_ui, mindeval_passed

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def read(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8") as f:
        return f.read()


def run(id, title="ui-test all", status="completed", conclusion="success"):
    return {"id": id, "display_title": title, "status": status, "conclusion": conclusion}


class PriorRunsTests(unittest.TestCase):
    def test_a_successful_full_run_is_a_pass(self):
        self.assertEqual(("pass", 2), decide_ui([run(1, conclusion="failure"), run(2)]))

    def test_a_re_run_that_succeeded_counts(self):
        # GitHub keeps one run per id: its last attempt's conclusion.
        self.assertEqual(("pass", 7), decide_ui([run(7)]))

    def test_partial_or_mindeval_runs_never_count(self):
        runs = [run(1, "ui-test smoke,games"), run(2, "ui-test auto"), run(3, "ui-test mindeval")]
        self.assertEqual(("none", None), decide_ui(runs))

    def test_a_full_run_still_going_is_waited_for(self):
        self.assertEqual(("wait", 4), decide_ui([run(3, conclusion="failure"), run(4, status="in_progress", conclusion=None)]))
        self.assertEqual(("wait", 5), decide_ui([run(5, status="queued", conclusion=None)]))

    def test_a_pass_wins_over_one_still_going(self):
        self.assertEqual(("pass", 6), decide_ui([run(5, status="in_progress", conclusion=None), run(6)]))

    def test_failed_or_cancelled_only_means_run_it_here(self):
        self.assertEqual(("none", None), decide_ui([run(1, conclusion="failure"), run(2, conclusion="cancelled")]))
        self.assertEqual(("none", None), decide_ui([]))

    def test_mindeval_passed_reads_the_reusable_job(self):
        self.assertEqual(9, mindeval_passed({8: [{"name": "mind-eval / eval", "conclusion": "failure"}],
                                             9: [{"name": "plan", "conclusion": "success"},
                                                 {"name": "mind-eval / eval", "conclusion": "success"}]}))
        self.assertIsNone(mindeval_passed({8: [{"name": "mind-eval / eval", "conclusion": "skipped"}]}))
        self.assertIsNone(mindeval_passed({}))

    def test_ui_test_names_a_full_run_the_way_test_build_looks_for_it(self):
        self.assertEqual("ui-test all", FULL_TITLE)
        self.assertIn("run-name: ui-test ${{ inputs.stages || 'all' }}", read(".github", "workflows", "ui-test.yml"))
        yml = read(".github", "workflows", "test-build.yml")
        self.assertIn("prior_runs.py ui", yml)
        self.assertIn("if: ${{ !inputs.force_ui }}", yml)   # the manual way to run every stage here anyway


if __name__ == "__main__":
    unittest.main()
