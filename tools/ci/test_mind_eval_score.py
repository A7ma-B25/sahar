"""mind_eval_score: the five checks on synthetic rows, and its Syrian/MSA lists equal Core's DialectScore."""
import os
import re
import unittest

import mind_eval_score as m

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


class MindEvalScoreTests(unittest.TestCase):
    def test_word_lists_mirror_core_dialect_score(self):
        with open(os.path.join(ROOT, "src", "Sahar.Core", "Mind", "Bench", "BenchSuite.cs"), encoding="utf-8") as f:
            cs = f.read()
        syr = re.findall(r'"([^"]+)"', re.search(r"Syrian = \[([^\]]*)\]", cs).group(1))
        msa = re.findall(r'"([^"]+)"', re.search(r"Msa = \[([^\]]*)\]", cs).group(1))
        self.assertEqual(syr, m.SYRIAN)
        self.assertEqual(msa, m.MSA)

    def test_the_043_fallback_line_is_not_a_reply(self):
        row = {"id": "e01", "kind": "chat", "visible": "ما فهمت عليك منيح… فيك تقولها بطريقة تانية؟", "calls": [], "fastPath": False}
        c = m.score(row, {"topic": ["منيح"]})
        self.assertFalse(c["visible"])
        self.assertFalse(c["topic"])

    def test_a_fallback_line_scores_nothing_but_the_tool(self):
        c = m.score({"kind": "chat", "visible": "ما فهمت عليك منيح… فيك تقولها بطريقة تانية؟", "calls": []})
        self.assertEqual([k for k, v in c.items() if v], ["tool"])

    def test_a_tools_list_is_not_her_line(self):
        row = {"kind": "command", "expectTool": "quran.open", "calls": ["quran.open act"], "seen": ["فتحتلك القرآن", "1. الفاتحة 2. البقرة"],
               "where": ["header", "list"], "visible": "1. الفاتحة 2. البقرة"}
        self.assertEqual(m.her_line(row), "فتحتلك القرآن")

    def test_a_good_chat_reply_passes_all_five(self):
        row = {"id": "e01", "kind": "chat", "visible": "منيحة الحمدلله، وإنت شو أخبارك؟", "calls": [], "fastPath": False}
        self.assertEqual(set(m.score(row, {"topic": ["منيح"]}).values()), {True})

    def test_a_command_needs_its_tool_or_the_fast_path(self):
        base = {"id": "e02", "kind": "command", "expectTool": "timer.start", "visible": "تمام، شغّلت مؤقت ٥ دقايق", "fastPath": False}
        self.assertTrue(m.score(dict(base, calls=["timer.start act"]))["tool"])
        self.assertFalse(m.score(dict(base, calls=["note.save act"]))["tool"])
        self.assertTrue(m.score(dict(base, calls=[], fastPath=True))["tool"])
        self.assertFalse(m.score(dict(base, expectTool="reminder.set", calls=[], fastPath=True))["tool"])

    def test_chat_with_an_action_is_the_wrong_tool(self):
        row = {"kind": "chat", "expectTool": None, "visible": "تمام", "calls": ["timer.start act"]}
        self.assertFalse(m.score(row)["tool"])

    def test_msa_is_not_syrian_and_long_is_not_short(self):
        self.assertFalse(m.syrian("أنا بخير، شكرًا لسؤالك. كيف يمكنني مساعدتك اليوم؟"))
        self.assertTrue(m.syrian("منيحة، شو بدك نعمل هلق؟"))
        row = {"kind": "chat", "visible": "منيح " * 40, "calls": []}
        self.assertFalse(m.score(row)["short"])

    def test_summary_and_markdown(self):
        rows = [{"id": "e01", "kind": "chat", "prompt": "شلونك؟", "visible": "منيحة، وإنت؟", "calls": [], "model": "x"},
                {"id": "e02", "kind": "chat", "prompt": "شو اسمك؟", "visible": "", "calls": [], "model": "x"}]
        s, scored = m.summarize(rows, {"e01": {"topic": ["منيح"]}, "e02": {"topic": ["سحر"]}})
        self.assertEqual(s["rows"], 2)
        self.assertEqual(s["totals"]["visible"], 1)
        self.assertIn("| e02 | شو اسمك؟ | (nothing) |", m.markdown(s, scored))


if __name__ == "__main__":
    unittest.main()
