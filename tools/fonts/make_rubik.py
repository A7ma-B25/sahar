"""Cuts static Rubik instances (Regular 400, Medium 500, SemiBold 600) from Google Fonts' variable Rubik[wght].ttf for the
engine (plan 0.10b Ruling 6: DirectWrite in-memory font set; static instances are safer through raw COM).

Usage: python3 -I tools/fonts/make_rubik.py <Rubik[wght].ttf> <out-dir>
Prints per file: weight, size, whether `tnum` is present, and the advance widths of the Arabic-Indic digits ٠–٩
(Ruling 5: they are proportional, so the engine gives every digit a fixed cell).
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

WEIGHTS = {400: "Regular", 500: "Medium", 600: "SemiBold"}


def main(src, out):
    os.makedirs(out, exist_ok=True)
    for w, sub in WEIGHTS.items():
        font = TTFont(src)
        inst = instancer.instantiateVariableFont(font, {"wght": w}, updateFontNames=True)
        name = inst["name"]
        for rec in list(name.names):
            if rec.nameID in (1, 16):
                rec.string = "Rubik"
            elif rec.nameID in (2, 17):
                rec.string = sub if rec.nameID == 17 or sub in ("Regular",) else rec.string
        name.setName("Rubik", 1, 3, 1, 0x409)
        name.setName("Rubik", 16, 3, 1, 0x409)
        name.setName(sub, 17, 3, 1, 0x409)
        name.setName(f"Rubik {sub}", 4, 3, 1, 0x409)
        name.setName(f"Rubik-{sub}", 6, 3, 1, 0x409)
        inst["OS/2"].usWeightClass = w
        path = os.path.join(out, f"Rubik-{sub}.ttf")
        inst.save(path)
        f = TTFont(path)
        gsub = f["GSUB"].table if "GSUB" in f else None
        tnum = bool(gsub and any(fr.FeatureTag == "tnum" for fr in gsub.FeatureList.FeatureRecord))
        cmap = f.getBestCmap()
        hmtx = f["hmtx"]
        widths = [hmtx[cmap[0x0660 + d]][0] for d in range(10) if 0x0660 + d in cmap]
        print(f"{os.path.basename(path)}: weight {w}, {os.path.getsize(path)} bytes, tnum {'yes' if tnum else 'no'}, "
              f"Arabic-Indic digit widths {widths}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
