"""Exports the settings page's living Sahar (Calm, ids, no SMIL) from design.py.

Run from the repo root:  python3 -I tools/art/export_svg.py [--out DIR]   (default src/Sahar.Settings/web/sahar)
"""
import argparse, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import design as D

OUT = os.path.join("src", "Sahar.Settings", "web", "sahar")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=OUT)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, "sahar.svg"), "w", encoding="utf-8", newline="\n") as f:
        f.write(D.preview_svg())


if __name__ == "__main__":
    main()
