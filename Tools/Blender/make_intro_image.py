"""Builds the release introduction images from the transparent renders of build_visitors.py --intro.

  python Tools/Blender/make_intro_image.py <folder with intro_*.png> <output folder>

Writes costumes_030.jpg (2x2 sheet) and costumes_030_<set>.jpg (one per set): a purple night backdrop with a
moon and warm lantern glow, the visitor, a soft shadow under it, and the Korean set name at the bottom
(Galmuri11). The backdrop is drawn here, so it is only an approximation of the in-game dithered look.
"""
import math
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

SIZE = 1000
SETS = [("bunny", "바니걸"), ("maid", "메이드"), ("pirate", "해적"), ("gold", "골드 비키니")]
FONT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "Fonts", "Galmuri11.ttf")


def backdrop(seed):
    rng = random.Random(seed)
    img = Image.new("RGB", (SIZE, SIZE))
    px = img.load()
    top, mid, low = (46, 24, 78), (92, 44, 100), (20, 12, 36)
    for y in range(SIZE):
        t = y / SIZE
        if t < 0.80:
            k = t / 0.80
            c = [top[i] + (mid[i] - top[i]) * k for i in range(3)]
        else:
            k = (t - 0.80) / 0.20
            c = [mid[i] + (low[i] - mid[i]) * min(1.0, k * 1.6) for i in range(3)]
        for x in range(SIZE):
            px[x, y] = (int(c[0]), int(c[1]), int(c[2]))
    glow = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse((680, 90, 900, 310), fill=(150, 120, 60))  # moon halo
    g.ellipse((100, 810, 900, 1210), fill=(46, 22, 8))  # warm lantern light on the ground
    glow = glow.filter(ImageFilter.GaussianBlur(70))
    img = ImageChops.add(img, glow)
    d = ImageDraw.Draw(img)
    for _ in range(60):  # stars
        x, y, r = rng.randrange(SIZE), rng.randrange(int(SIZE * 0.45)), rng.choice((1, 1, 2))
        d.ellipse((x - r, y - r, x + r, y + r), fill=(235, 225, 255))
    d.ellipse((710, 120, 870, 280), fill=(255, 238, 170))  # moon
    d.ellipse((745, 165, 785, 205), fill=(240, 218, 140))
    d.ellipse((810, 205, 850, 245), fill=(240, 218, 140))
    d.polygon([(0, 800)] + [(x, 800 - 24 * abs(math.sin(x / 83.0)) - 14) for x in range(0, SIZE + 1, 20)] + [(SIZE, 800)],
              fill=(18, 10, 30))  # distant dark horizon, just behind the feet
    return img


def cell(render_path, label, seed):
    img = backdrop(seed).convert("RGBA")
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.ellipse((500 - 230, 790, 500 + 230, 850), fill=(8, 4, 16, 170))
    img = Image.alpha_composite(img, shadow.filter(ImageFilter.GaussianBlur(14)))
    img = Image.alpha_composite(img, Image.open(render_path).convert("RGBA"))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, 64)
    w = d.textlength(label, font=font)
    x, y = (SIZE - w) / 2, 880
    d.rounded_rectangle((x - 36, y - 16, x + w + 36, y + 84), radius=16, fill=(14, 8, 28, 190))
    d.text((x, y), label, font=font, fill=(255, 240, 200), stroke_width=2, stroke_fill=(40, 20, 60))
    return img.convert("RGB")


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    cells = []
    for i, (name, label) in enumerate(SETS):
        c = cell(os.path.join(src, f"intro_{name}.png"), label, 30 + i)
        c.save(os.path.join(out, f"costumes_030_{name}.jpg"), quality=92)
        cells.append(c)
    sheet = Image.new("RGB", (SIZE * 2, SIZE * 2))
    for i, c in enumerate(cells):
        sheet.paste(c, ((i % 2) * SIZE, (i // 2) * SIZE))
    sheet.save(os.path.join(out, "costumes_030.jpg"), quality=92)


if __name__ == "__main__":
    main()
