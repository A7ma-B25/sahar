"""Review 0.13 #4: the activities never log an exception's text (it can hold a path, a title or a URL): kinds only,
through LogRules.Describe(e) or e.GetType().Name."""
import os
import re
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
FOLDER = os.path.join(ROOT, "src", "Sahar.Engine", "Activities")


class LogPrivacyTests(unittest.TestCase):
    def test_no_raw_exception_in_log_lines(self):
        bad = []
        for name in sorted(os.listdir(FOLDER)):
            with open(os.path.join(FOLDER, name), encoding="utf-8") as f:
                for i, line in enumerate(f, 1):
                    if "Log.Write" in line and (re.search(r"\{e\}", line) or ".Message" in line):
                        bad.append(f"{name}:{i}")
        self.assertEqual([], bad)


if __name__ == "__main__":
    unittest.main()
