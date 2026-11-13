"""The app icon (review 2026-11-13): one .ico with every size Windows asks for, each its own drawing (PNG entries),
used by the engine, the setup, the shortcut, Settings → Apps and the tray; and the preview sheet for the owner."""
import os
import struct
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ICO = os.path.join(ROOT, "src", "Sahar.Engine", "assets", "sahar.ico")
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def entries(path):
    with open(path, "rb") as f:
        data = f.read()
    reserved, kind, count = struct.unpack_from("<HHH", data, 0)
    assert (reserved, kind) == (0, 1), "not an icon file"
    out = []
    for i in range(count):
        w, h, _colors, _r, _planes, _bpp, size, offset = struct.unpack_from("<BBBBHHII", data, 6 + 16 * i)
        out.append((w or 256, h or 256, data[offset:offset + size]))
    return out


class AppIconTests(unittest.TestCase):
    def test_every_size(self):
        self.assertEqual(SIZES, sorted(w for w, _h, _b in entries(ICO)))

    def test_each_entry_is_a_png_of_its_own_size(self):
        for w, h, blob in entries(ICO):
            self.assertEqual(b"\x89PNG\r\n\x1a\n", blob[:8], w)
            pw, ph = struct.unpack(">II", blob[16:24])
            self.assertEqual((w, h), (pw, ph))

    def test_the_preview_sheet_is_there_for_the_owner(self):
        self.assertTrue(os.path.getsize(os.path.join(ROOT, "docs", "design", "icon", "icon-sheet.png")) > 1000)


if __name__ == "__main__":
    unittest.main()
