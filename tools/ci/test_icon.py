"""The settings app's window icon (review fix m10): the csproj points at assets/sahar.ico — the installer branch draws
it — and only when the file is there, so the build never fails without it."""
import os
import re
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


class IconTests(unittest.TestCase):
    def test_settings_app_icon_is_wired_and_guarded(self):
        text = open(os.path.join(ROOT, "src", "Sahar.Settings", "Sahar.Settings.csproj"), encoding="utf-8").read()
        m = re.search(r"<ApplicationIcon\s+Condition=\"Exists\('([^']+)'\)\">([^<]+)</ApplicationIcon>", text)
        self.assertIsNotNone(m, "no guarded ApplicationIcon")
        self.assertEqual(m.group(1), m.group(2))
        self.assertTrue(m.group(2).replace("\\", "/").endswith("assets/sahar.ico"))


if __name__ == "__main__":
    unittest.main()
