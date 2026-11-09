"""Bakes the "standard 70 level" ASCII ramp into a strip of 8x16 glyph tiles for AsciiGlyphGenerator.cs.

The characters are Paul Bourke's well-known ramp. They are rasterized with a monospace font (DejaVu Sans
Mono, which ships with matplotlib and is freely redistributable), then sorted by how much ink each tile
really has, so the order is right for this font at this size. The tiles go from empty to densest, which is
the order for light letters on a dark screen.

Usage:  python Tools/Ascii/bake_standard_ramp.py [path/to/font.ttf]
Output: Assets/Editor/AsciiStandardRamp.png  (70 tiles wide, 1 tile tall, grayscale ink)
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

RAMP = "$@B%8&WM#*oahkbdpqwmZO0QLCJUYXzcvunxrjft/\\|()1{}[]?-_+~<>i!lI;:,\"^`'. "
TILE_W, TILE_H = 8, 16
FONT_SIZE = 13  # advance of a mono face at this size is about 8 px
BASELINE = 12   # rows from the tile top; leaves room for descenders in ( ) | [ ] { } , ; q p

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Editor", "AsciiStandardRamp.png")


def find_font():
    if len(sys.argv) > 1:
        return sys.argv[1]
    import matplotlib
    return os.path.join(os.path.dirname(matplotlib.__file__), "mpl-data", "fonts", "ttf", "DejaVuSansMono.ttf")


def main():
    font = ImageFont.truetype(find_font(), FONT_SIZE)
    tiles = []
    for ch in RAMP:
        tile = Image.new("L", (TILE_W, TILE_H), 0)
        ImageDraw.Draw(tile).text((0, BASELINE), ch, fill=255, font=font, anchor="ls")
        tiles.append((ch, tile, sum(tile.getdata())))
    # The space is the empty tile; the rest are ordered from the lightest to the densest ink.
    tiles.sort(key=lambda t: t[2])

    strip = Image.new("L", (TILE_W * len(tiles), TILE_H), 0)
    for i, (_, tile, _) in enumerate(tiles):
        strip.paste(tile, (i * TILE_W, 0))
    strip.save(OUT)
    print(len(tiles), "glyphs:", "".join(t[0] for t in tiles))
    print("wrote", OUT)


if __name__ == "__main__":
    main()
