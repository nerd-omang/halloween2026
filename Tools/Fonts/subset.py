"""Subsets the UI fonts for the WebGL build.

usage: python Tools/Fonts/subset.py <dir with Galmuri11.ttf and Creepster-Regular.ttf>

Body font: Galmuri11 (SIL OFL 1.1), ASCII + KS X 1001 Hangul + common symbols.
Title font: Creepster (SIL OFL 1.1), ASCII only. Its Reserved Font Name forbids reusing the name for a
modified version, so the subset is renamed to "SpookyTitle".
"""
import sys
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont

src = Path(sys.argv[1])
out = Path(__file__).resolve().parents[2] / "Assets" / "Resources" / "Fonts"
out.mkdir(parents=True, exist_ok=True)

ascii_chars = [chr(c) for c in range(0x20, 0x7F)]
hangul = []
for hi in range(0xB0, 0xC9):
    for lo in range(0xA1, 0xFF):
        try:
            hangul.append(bytes([hi, lo]).decode("euc-kr"))
        except UnicodeDecodeError:
            pass
extra = list("·…—–‘’“”←→↑↓♥♡★☆●○■□▲▼×÷")
extra += [chr(c) for c in range(0x3131, 0x318F)]


def run(font, chars, dest, rename=None):
    opts = subset.Options()
    opts.layout_features = []
    opts.hinting = False
    opts.notdef_outline = True
    opts.name_IDs = [0, 1, 2, 3, 4, 5, 6, 13, 14]
    f = TTFont(str(src / font))
    s = subset.Subsetter(opts)
    s.populate(text="".join(chars))
    s.subset(f)
    if rename:
        for n in f["name"].names:
            if n.nameID in (1, 4, 16):
                n.string = rename
            elif n.nameID == 6:
                n.string = rename
            elif n.nameID == 3:
                n.string = rename + "-subset"
    f.save(str(out / dest))
    print(dest, (out / dest).stat().st_size)


run("Galmuri11.ttf", ascii_chars + hangul + extra, "Galmuri11.ttf")
run("Creepster-Regular.ttf", ascii_chars, "SpookyTitle.ttf", rename="SpookyTitle")
