from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "assets" / "clean-logo-noise.py"
SPEC = importlib.util.spec_from_file_location("clean_logo_noise", SCRIPT)
assert SPEC is not None
clean_logo_noise = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(clean_logo_noise)


class PwaIconAssetTests(unittest.TestCase):
    def test_touch_icon_has_opaque_dark_background_for_ios_home_screen_preview(self) -> None:
        image = clean_logo_noise.parse_png(ROOT / "apps" / "web-svelte" / "static" / "apple-touch-icon.png")

        self.assertEqual(image.width, 180)
        self.assertEqual(image.height, 180)
        self.assertTrue(all(image.rgba[index + 3] == 255 for index in range(0, len(image.rgba), 4)))

        corners = [
            pixel(image, 0, 0),
            pixel(image, image.width - 1, 0),
            pixel(image, 0, image.height - 1),
            pixel(image, image.width - 1, image.height - 1),
        ]
        self.assertEqual(corners, [(7, 8, 11, 255)] * 4)


def pixel(image, x: int, y: int) -> tuple[int, int, int, int]:
    offset = (y * image.width + x) * 4
    return tuple(image.rgba[offset : offset + 4])


if __name__ == "__main__":
    unittest.main()
