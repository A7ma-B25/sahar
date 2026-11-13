import unittest

from ui_groups import ALL, plan


class UiGroupsTests(unittest.TestCase):
    def test_all_is_six_groups_covering_every_stage_once(self):
        groups = plan("all")
        self.assertEqual(6, len(groups))
        self.assertIn({"name": "settings", "stages": "settings"}, groups)   # 0.12: the settings app has its own job
        self.assertIn({"name": "activities", "stages": "activities"}, groups)   # 0.13: so do the activities
        stages = [s for g in groups for s in g["stages"].split(",")]
        self.assertEqual(sorted(ALL), sorted(stages))
        self.assertEqual(len(stages), len(set(stages)))

    def test_empty_is_all(self):
        self.assertEqual(plan("all"), plan(""))

    def test_few_stages_run_in_one_job_in_the_order_asked(self):
        self.assertEqual([{"name": "shell-idle", "stages": "shell,idle"}], plan("shell, idle"))

    def test_many_stages_keep_the_groups(self):
        self.assertEqual(
            [{"name": "motion", "stages": "motion"}, {"name": "life", "stages": "life"},
             {"name": "menu", "stages": "menu"}, {"name": "senses", "stages": "games,shell"}],
            plan("menu,games,motion,life,shell"))

    def test_unknown_stage_fails(self):
        with self.assertRaises(SystemExit):
            plan("smoke,nope")


if __name__ == "__main__":
    unittest.main()
