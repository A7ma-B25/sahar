"""The settings app's window icon (review fix m10): the installer's assets/sahar.ico (src/Sahar.Engine/assets)."""
import os
import re
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


class IconTests(unittest.TestCase):
    def test_settings_app_icon_is_the_installers_icon(self):
        """0.14.0: the installer's icon is in the repo now, so the guard is gone: the build fails if it goes missing."""
        text = open(os.path.join(ROOT, "src", "Sahar.Settings", "Sahar.Settings.csproj"), encoding="utf-8").read()
        m = re.search(r"<ApplicationIcon>([^<]+)</ApplicationIcon>", text)
        self.assertIsNotNone(m, "no ApplicationIcon")
        self.assertEqual(r"..\Sahar.Engine\assets\sahar.ico", m.group(1))
        self.assertTrue(os.path.isfile(os.path.join(ROOT, "src", "Sahar.Settings", *m.group(1).split("\\"))))


if __name__ == "__main__":
    unittest.main()
