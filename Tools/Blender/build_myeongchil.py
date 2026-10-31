"""
Builds the easter-egg Myeongchil (the streamer of the Koonmungchi fandom) as a two-heads-tall chibi and
optionally exports it to its own FBX. Reuses the helpers of build_visitors.py and build_moku.py.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_myeongchil.py -- \
      [--detail mid|low] [--out Assets/Models/Characters/Myeongchil.fbx] [--preview <dir>]

Detail: the design is worked out at "mid" (rounder heads, more segments, more locks) and then reduced
to "low" by the same code, so every design decision is made while the shapes still read.

Look: silver-gray bob with a blue under layer and blue ends (secret two-tone), a center-parted
bang, an ahoge strand, one bent blue nail driven into her head (her right, -X, Frankenstein style) and a white mushroom clip with an X on the other side;
glowing blue swirl eyes, a mole, a blue stitched choker; a black halter top with a white band, an
off-shoulder blue jacket with big black moe sleeves, a black pleated mini skirt with blue lace-ups,
bare legs with blue stitch lines and a thigh belt with a pouch, black lace-up boots with blue soles.
Small print (face, top band, jacket and hair two-tone) lives in MyeongchilAtlas.png next to the FBX.
Origin between the feet, facing -Y like the visitors. Object name prefix: Myeong_.
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_visitors as bv  # noqa: E402
import build_moku as mk  # noqa: E402
from build_visitors import (AXIS_X, AXIS_Y, AXIS_Z, FRONT, Part, add_cone, add_sphere,  # noqa: E402
                            add_tube, orient, track, trs)

NECK_Z = 0.40
HEAD_CENTER = Vector((0.0, 0.0, 0.64))
HEAD_HALF = Vector((0.23, 0.2, 0.2))
OUTLINE = 0.01
NO_OUTLINE = ("Myeong_Pins", "Myeong_Stitches", "Myeong_Choker", "Myeong_Ahoge", "Myeong_Trim")

MID = True  # set from --detail


def S(mid, low):
    return mid if MID else low


ATLAS_W, ATLAS_H = 512, 256
CELL_FACE = (0, 0, 256, 256)         # planar from the front over the face
CELL_TOP = (256, 128, 128, 128)      # halter top: black, white band at the bottom
CELL_JACKET = (384, 128, 128, 128)   # blue above JACKET_SPLIT_Z, black below
CELL_HAIR = (448, 64, 64, 64)        # gray above HAIR_SPLIT_Z, blue below (secret two-tone)
FACE_X = (-0.24, 0.24)
FACE_Z = (-0.24, 0.24)
TOP_Z = (0.29, 0.41)
JACKET_Z = (0.12, 0.42)
JACKET_SPLIT_Z = 0.345
HAIR_Z = (0.30, 0.80)
HAIR_SPLIT_Z = 0.47
HAIR_WIDTH = 0.92                     # the hair is a little narrower than the head box is deep

SKIN = (0.96, 0.88, 0.84)
GRAY = (0.66, 0.67, 0.70)
BLUE = (0.18, 0.55, 0.92)
GLOW = (0.35, 0.80, 1.0)
BLACK = (0.05, 0.05, 0.06)

bv.MATERIALS.update({
    "MyeongSkin": ((1.0, 1.0, 1.0), True),
    "MyeongTop": ((1.0, 1.0, 1.0), True),
    "MyeongJacket": ((1.0, 1.0, 1.0), True),
    "MyeongHair": ((1.0, 1.0, 1.0), True),
    "MyeongNail": (BLUE, False),
    "MyeongClip": ((0.94, 0.94, 0.92), False),
    "MyeongBlack": (BLACK, False),
    "MyeongSkirt": ((0.07, 0.07, 0.08), False),
    "MyeongBoot": ((0.09, 0.09, 0.10), False),
    "MyeongBlue": (BLUE, False),
    "MyeongStitch": (GLOW, False),
    "MyeongWhite": ((0.92, 0.92, 0.92), False),
    "MyeongMetal": ((0.66, 0.66, 0.68), False),
    "MyeongOutline": ((0.02, 0.02, 0.02), False),
})


def px_uv(x, y):
    return (x / ATLAS_W, y / ATLAS_H)


def cell_point(cell, s, t):
    return px_uv(cell[0] + s * cell[2], cell[1] + t * cell[3])


# ---------------------------------------------------------------- atlas

def paint_atlas(path):
    img = np.ones((ATLAS_H, ATLAS_W, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:ATLAS_H, 0:ATLAS_W] + 0.5

    def fill(mask, color):
        img[mask] = (*color, 1.0)

    def in_cell(cell):
        x, y, w, h = cell
        return (xs >= x) & (xs < x + w) & (ys >= y) & (ys < y + h)

    def ellipse(cx, cy, a, b, angle=0.0):
        c, s = math.cos(angle), math.sin(angle)
        u = (xs - cx) * c + (ys - cy) * s
        v = -(xs - cx) * s + (ys - cy) * c
        return (u / a) ** 2 + (v / b) ** 2 <= 1.0

    def segment(p0, p1, width):
        (x0, y0), (x1, y1) = p0, p1
        dx, dy = x1 - x0, y1 - y0
        t = np.clip(((xs - x0) * dx + (ys - y0) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
        return (xs - x0 - t * dx) ** 2 + (ys - y0 - t * dy) ** 2 <= (width * 0.5) ** 2

    def rect(x0, y0, x1, y1):
        return (xs >= x0) & (xs < x1) & (ys >= y0) & (ys < y1)

    fx = CELL_FACE[2] / (FACE_X[1] - FACE_X[0])

    def fp(x, z):
        return CELL_FACE[0] + (x - FACE_X[0]) * fx, CELL_FACE[1] + (z - FACE_Z[0]) * fx

    face = in_cell(CELL_FACE)
    fill(face, SKIN)
    ink = (0.03, 0.04, 0.08)
    for sx in (-1, 1):
        cx, cy = fp(sx * 0.085, -0.02)
        a, b = 0.036 * fx, 0.044 * fx
        eye = face & ellipse(cx, cy, a, b)
        fill(eye, (0.06, 0.20, 0.55))
        fill(face & ellipse(cx, cy, a * 0.88, b * 0.88), GLOW)
        for k, scale in enumerate((0.7, 0.45, 0.22)):  # the swirl, as concentric rings
            ring = ellipse(cx, cy, a * scale, b * scale) & ~ellipse(cx, cy, a * scale * 0.8, b * scale * 0.8)
            fill(face & ring, (0.82, 0.97, 1.0) if k % 2 == 0 else (0.15, 0.50, 0.92))
        fill(face & ellipse(cx, cy, a * 0.12, b * 0.12), (0.02, 0.10, 0.30))
        # Upper lash: a thick dark line with a wing at the outer corner; blue dotted lashes below.
        top = cy + 0.72 * b
        fill(face & segment((cx - 1.05 * a, top - 0.15 * b), (cx + 1.05 * a, top), 0.014 * fx), ink)
        fill(face & segment((cx + sx * 1.0 * a, top - 0.05 * b), (cx + sx * 1.6 * a, top + 0.35 * b), 0.011 * fx), ink)
        fill(face & segment((cx + sx * 0.9 * a, top - 0.2 * b), (cx + sx * 1.35 * a, top - 0.4 * b), 0.008 * fx), ink)
        for k in range(5):
            dx = cx + (k - 2) * 0.42 * a
            fill(face & ellipse(dx, cy - b * 1.02, 0.07 * a, 0.09 * b), (0.20, 0.55, 0.95))
    cx, cy = fp(0.0, -0.105)
    fill(face & ellipse(cx, cy, 0.016 * fx, 0.011 * fx), (0.42, 0.08, 0.12))
    fill(face & ellipse(cx, cy + 0.004 * fx, 0.011 * fx, 0.0045 * fx), (0.95, 0.88, 0.88))
    fill(face & ellipse(*fp(0.12, -0.075), 0.005 * fx, 0.005 * fx), (0.20, 0.25, 0.45))  # the mole
    for sx in (-1, 1):
        fill(face & ellipse(*fp(sx * 0.145, -0.07), 0.036 * fx, 0.017 * fx), (0.98, 0.68, 0.70))

    x, y, w, h = CELL_TOP
    top = in_cell(CELL_TOP)
    fill(top, BLACK)
    fill(top & rect(x, y, x + w, y + h * 0.2), (0.93, 0.93, 0.93))
    for k in range(14):  # "Myeong?" repeated as small print on the band
        fill(top & rect(x + 6 + k * 8, y + h * 0.07, x + 10 + k * 8, y + h * 0.13), (0.3, 0.3, 0.33))

    x, y, w, h = CELL_JACKET
    jacket = in_cell(CELL_JACKET)
    split = (JACKET_SPLIT_Z - JACKET_Z[0]) / (JACKET_Z[1] - JACKET_Z[0])
    fill(jacket, (0.07, 0.08, 0.10))
    fill(jacket & (ys >= y + h * split), BLUE)

    x, y, w, h = CELL_HAIR
    hair = in_cell(CELL_HAIR)
    split = (HAIR_SPLIT_Z - HAIR_Z[0]) / (HAIR_Z[1] - HAIR_Z[0])
    fill(hair, GRAY)
    fill(hair & (ys < y + h * split), BLUE)

    atlas = bpy.data.images.new("MyeongchilAtlas", ATLAS_W, ATLAS_H, alpha=False)
    atlas.pixels.foreach_set(img.ravel())
    os.makedirs(os.path.dirname(path), exist_ok=True)
    atlas.filepath_raw = path
    atlas.file_format = "PNG"
    atlas.save()
    bv.ATLAS = atlas


# ---------------------------------------------------------------- helpers

def ring_z(center, r, count):
    return [center + Vector((r * math.sin(a), -r * math.cos(a), 0.0))
            for a in (2 * math.pi * k / count for k in range(count + 1))]


def ring_xz(center, r, count, plane_y=None):
    """Circle standing in the XZ plane (facing the front)."""
    return [center + Vector((r * math.cos(a), 0.0, r * math.sin(a)))
            for a in (2 * math.pi * k / count for k in range(count + 1))]


def zigzag(points, amp):
    """Alternate points up and down: the stitched seam."""
    return [p + AXIS_Z * (amp if i % 2 else -amp) for i, p in enumerate(points)]


def add_box(part, mat, center, size, rotation=None):
    m = Matrix.Translation(center)
    if rotation is not None:
        m = m @ rotation
    m = m @ Matrix.Diagonal((*size, 1.0))
    res = bmesh.ops.create_cube(part.bm, size=1.0, matrix=m)
    part.assign(res["verts"], mat, False)


# ---------------------------------------------------------------- head

def build_head():
    """Rounded box head (features painted in the atlas) with a neck; mid detail cuts the box twice so it
    reads round."""
    part = Part("Myeong_Head")
    bmesh.ops.create_cube(part.bm, size=1.0,
                          matrix=Matrix.Translation(HEAD_CENTER) @ Matrix.Diagonal((*(HEAD_HALF * 2), 1.0)))
    bmesh.ops.subdivide_edges(part.bm, edges=list(part.bm.edges), cuts=S(2, 1), use_grid_fill=True)
    radii = Vector((0.26, 0.25, 0.22))
    for v in part.bm.verts:
        d = v.co - HEAD_CENTER
        n = Vector((d.x / radii.x, d.y / radii.y, d.z / radii.z))
        v.co = HEAD_CENTER + d.lerp(d / n.length, 0.7 if not MID else 0.85)
    part.assign(list(part.bm.verts), "MyeongSkin")
    mk.HEAD_BVH = BVHTree.FromBMesh(part.bm)
    neck_bottom, neck_top = NECK_Z - 0.02, HEAD_CENTER.z - HEAD_HALF.z + 0.03
    add_cone(part, "MyeongSkin", trs(Vector((0, 0, (neck_bottom + neck_top) * 0.5)), Matrix(), (1, 1, 1)),
             0.065, 0.06, neck_top - neck_bottom, segments=S(10, 6))
    part.finish()

    part = Part("Myeong_Choker")
    z = NECK_Z + 0.025
    band = zigzag(ring_z(Vector((0, 0, z)), 0.069, S(20, 12)), 0.007)
    add_tube(part, "MyeongStitch", band, [0.005] * len(band), AXIS_Z, segments=S(4, 3), cap_start=False, cap_end=False)
    part.finish()


# ---------------------------------------------------------------- hair

def squeeze(part):
    for v in part.bm.verts:
        v.co.x *= HAIR_WIDTH


def build_hair():
    """Silver bob: a rounded cap, thick lock slabs falling to the jaw/collarbone in uneven lengths, a blue
    under layer, a center-parted bang. The two-tone (gray, blue ends) is a split in the texture by height."""
    part = Part("Myeong_Hair")
    cap = [(0.0, 0.27), (0.13, 0.26), (0.23, 0.215), (0.295, 0.14), (0.318, 0.05), (0.318, -0.02)]
    if MID:
        cap = [(0.0, 0.272), (0.06, 0.268), (0.12, 0.258), (0.18, 0.24), (0.23, 0.215), (0.27, 0.18),
               (0.295, 0.14), (0.312, 0.095), (0.318, 0.05), (0.318, -0.02)]
    mk.add_lathe(part, "MyeongHair", cap, S(20, 10), HEAD_CENTER)
    window = [f for f in part.bm.faces
              if (c := f.calc_center_median() - HEAD_CENTER).z < 0.13
              and abs(math.degrees(math.atan2(c.x, -c.y))) < 42]
    bmesh.ops.delete(part.bm, geom=window, context="FACES")
    for v in part.bm.verts:
        d = v.co - HEAD_CENTER
        front = max(0.0, -d.y / max(Vector((d.x, d.y)).length, 1e-6) - 0.5) / 0.5
        low = min(1.0, max(0.0, (0.24 - d.z) / 0.1))
        if front * low > 0.0:
            n = d.normalized()
            loc, _, _, _ = mk.HEAD_BVH.ray_cast(HEAD_CENTER + n, -n)
            if loc is not None:
                v.co = v.co.lerp(loc + n * 0.015, front * low)
    mk.HAIR_BVH = BVHTree.FromBMesh(part.bm)

    def lock(a0, a1, z_end, tuck):
        grid = []
        rows = ((0.03, 0.0), (-0.07, 0.3), (-0.15, 0.65), (z_end, 1.0)) if MID else ((0.03, 0.0), (-0.12, 0.4), (z_end, 1.0))
        cols = S(3, 2)
        for z, t in rows:
            r = 0.31 + 0.012 * math.sin(math.pi * t * 0.8) - tuck * t * t
            grid.append([HEAD_CENTER + Vector((r * math.sin(math.radians(a)), -r * math.cos(math.radians(a)), z))
                         for a in [a0 + (a1 - a0) * k / (cols - 1) for k in range(cols)]])
        mk.add_slab(part, "MyeongHair", grid, 0.02)

    # Locks framing the face reach the collarbone; the rest are a blunt bob in uneven lengths.
    for sx in (-1, 1):
        lock(sx * 38, sx * 62, -0.24, 0.05)
    count = S(10, 8)
    span = 360 - 2 * 62
    step = span / count
    ends = (-0.22, -0.2, -0.25, -0.21, -0.24, -0.2, -0.25, -0.21, -0.23, -0.2)
    for k in range(count):
        a0 = 62 + k * step
        lock(a0, a0 + step + 2, ends[k % len(ends)], 0.045)

    # Bang: center parted slightly toward -X, swept to both sides, ending just above the eyes.
    flaps = [(-0.19, -0.04, 0.02), (-0.13, -0.03, 0.01), (-0.07, -0.02, 0.0), (0.0, -0.005, -0.01),
             (0.05, 0.02, 0.0), (0.11, 0.035, -0.02), (0.17, 0.04, -0.01)] if MID else \
            [(-0.165, -0.03, 0.02), (-0.05, -0.01, 0.0), (0.06, 0.03, -0.01), (0.17, 0.04, -0.01)]
    for x0, sweep, z_end in flaps:
        ts = (0.0, 0.5, 1.0)
        pts = [mk.face_point(x0 + sweep * t, 0.16 + (z_end - 0.16) * t, 0.03 * (1 - t) + 0.012)[0] for t in ts]
        w = 0.07 if MID else 0.12
        mk.add_clump(part, "MyeongHair", pts, [FRONT] * 3, [w, w * 0.98, w * 0.94], 0.02)
    squeeze(part)
    part.finish()

    # Blue under layer: shows between and below the locks.
    part = Part("Myeong_HairUnder")
    under = [(0.288, 0.06), (0.292, -0.1), (0.28, -0.2), (0.262, -0.27)]
    mk.add_lathe(part, "MyeongHair", under, S(16, 8), HEAD_CENTER, 1, gap=math.radians(36))
    squeeze(part)
    part.finish()

    part = Part("Myeong_Ahoge")
    crown = HEAD_CENTER + Vector((0.0, 0.0, 0.272))
    steps = S(8, 4)
    ts = [k / steps for k in range(steps + 1)]  # one strand rising from the crown and curling over
    strand = [crown - AXIS_Z * 0.02 + Vector((0.025 * math.sin(math.pi * t), -0.07 * t * t,
                                                0.15 * math.sin(math.pi * t * 0.55))) for t in ts]
    add_tube(part, "MyeongHair", strand, [0.008 * (1 - t) + 0.003 for t in ts], AXIS_X, segments=S(4, 3),
             cap_start=False)
    part.finish()


def build_head_gear():
    """Bent blue nail stuck in her head on her right (-X), white mushroom clip with an X on the other side, black cross pins and
    a blue dangling earring."""
    part = Part("Myeong_Nail")
    # A banana-curved nail driven through her head from side to side, in the frontal plane, sagging in the
    # middle (hidden inside the head). Her right end (-X) is the blue point; the other end carries the
    # white, X-marked nail head (the "mushroom clip").
    p0, p1, p2 = Vector((-0.36, 0.0, 0.87)), Vector((0.0, 0.0, 0.52)), Vector((0.36, 0.0, 0.84))
    steps = S(14, 8)
    path, radii = [], []
    for k in range(steps + 1):
        t = k / steps
        path.append(p0 * (1 - t) ** 2 + p1 * 2 * t * (1 - t) + p2 * t * t)
        radii.append(0.004 + 0.016 * min(1.0, t / 0.3) ** 1.2 if t < 0.8 else 0.02)
    add_tube(part, "MyeongNail", path, radii, AXIS_Y, segments=S(6, 4), cap_end=False)
    part.finish()

    part = Part("Myeong_NailHead")
    tangent = (p2 - p1).normalized()
    cap_c = p2 + tangent * 0.008
    add_cone(part, "MyeongClip", trs(cap_c, track(tangent), (1, 1, 1)), 0.075, 0.075, 0.018, segments=S(14, 8))
    perp1 = tangent.cross(AXIS_Y).normalized()
    perp2 = tangent.cross(perp1).normalized()
    for sgn in (-1, 1):
        a = cap_c + tangent * 0.011 + perp1 * 0.04 * sgn + perp2 * 0.04
        b = cap_c + tangent * 0.011 - perp1 * 0.04 * sgn - perp2 * 0.04
        add_tube(part, "MyeongBlack", [a, b], [0.0045, 0.0045], tangent, segments=3, cap_start=False, cap_end=False)
    part.finish()

    part = Part("Myeong_Pins")
    base, _ = mk.hair_point((0.9, -0.05, 0.15), 0.0)
    base.x = base.x * HAIR_WIDTH + 0.012  # the hair was squeezed after this point was taken
    for k in range(2):
        c = base + Vector((0.0, -0.02, -0.03 * k)) + Vector((0.0, 0.0, 0.0))
        add_tube(part, "MyeongBlack", [c + Vector((0.01, 0.0, 0.012)), c - Vector((0.01, 0.0, 0.012))],
                 [0.0035, 0.0035], AXIS_Y, segments=3)
        add_tube(part, "MyeongBlack", [c + Vector((-0.01, 0.0, 0.012)), c - Vector((-0.01, 0.0, 0.012))],
                 [0.0035, 0.0035], AXIS_Y, segments=3)
    dangle, _ = mk.hair_point((-0.95, -0.05, 0.05), 0.0)
    dangle.x = dangle.x * HAIR_WIDTH - 0.012
    add_box(part, "MyeongBlue", dangle - AXIS_Z * 0.03, (0.012, 0.004, 0.05))
    part.finish()


# ---------------------------------------------------------------- body

def build_body():
    part = Part("Myeong_Boots")
    for sx in (-1, 1):
        x = sx * 0.055
        add_lathe_boot(part, x)
    part.finish()

    part = Part("Myeong_Legs")
    for sx in (-1, 1):
        x = sx * 0.055
        mk.add_lathe(part, "MyeongSkin", [(0.027, 0.22), (0.025, 0.16), (0.022, 0.1)], S(10, 6), Vector((x, 0.0, 0.0)))
    part.finish()

    part = Part("Myeong_Stitches")
    seam = zigzag(ring_z(Vector((-0.055, 0.0, 0.2)), 0.0285, S(14, 10)), 0.006)  # her right thigh
    add_tube(part, "MyeongStitch", seam, [0.0035] * len(seam), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    seam = zigzag(ring_z(Vector((0.055, 0.0, 0.145)), 0.0255, S(14, 10)), 0.006)  # her left shin, lower
    add_tube(part, "MyeongStitch", seam, [0.0035] * len(seam), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    for sx in (-1, 1):  # ankles, just above the boot
        seam = zigzag(ring_z(Vector((sx * 0.055, 0.0, 0.118)), 0.0235, S(12, 8)), 0.005)
        add_tube(part, "MyeongStitch", seam, [0.0032] * len(seam), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    arm = zigzag(ring_z(Vector((0.13, 0.0, 0.372)), 0.04, S(14, 10)), 0.006)  # her left upper arm
    add_tube(part, "MyeongStitch", arm, [0.0035] * len(arm), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    part.finish()

    part = Part("Myeong_Trim")
    belt = ring_z(Vector((0.055, 0.0, 0.215)), 0.03, S(12, 8))
    add_tube(part, "MyeongBlack", belt, [0.006] * len(belt), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    add_box(part, "MyeongBlack", Vector((0.088, 0.0, 0.21)), (0.02, 0.026, 0.05))  # the pouch
    add_box(part, "MyeongBlue", Vector((0.055, -0.03, 0.215)), (0.014, 0.006, 0.014))  # buckle
    part.finish()

    part = Part("Myeong_Skirt")
    profile = [(0.085, 0.315), (0.1, 0.25), (0.145, 0.175)]
    mk.add_lathe(part, "MyeongSkirt", profile, S(24, 12), Vector(),
                 wobble=lambda k, i: 1.0 if i < 2 or k % 2 == 0 else 0.9)
    part.finish()

    part = Part("Myeong_SkirtTrim")
    for sx in (-1, 1):  # blue cross lacing and a ring on each hip, on the skirt's front
        for dz in (0.0, 1.0):
            pass
        base = math.radians(sx * 38)
        pts = []
        for z0, z1 in ((0.285, 0.225), (0.225, 0.285)):
            r0 = 0.0955 + (0.285 - z0) * 0.7
            a0 = base - math.radians(8 * sx)
            a1 = base + math.radians(8 * sx)
            ra = lambda z: 0.1 + (0.25 - z) * 0.4 if z < 0.25 else 0.1 - (z - 0.25) * 0.25
            p0 = Vector((ra(z0) * math.sin(a0), -ra(z0) * math.cos(a0), z0))
            p1 = Vector((ra(z1) * math.sin(a1), -ra(z1) * math.cos(a1), z1))
            n = Vector((math.sin(base), -math.cos(base), 0.0))
            add_tube(part, "MyeongBlue", [p0 + n * 0.004, p1 + n * 0.004], [0.0035, 0.0035], n, segments=3)
        n = Vector((math.sin(base), -math.cos(base), 0.0))
        ringc = Vector((0.1 * math.sin(base), -0.1 * math.cos(base), 0.215)) + n * 0.01
        circ = [ringc + Vector((0.016 * math.cos(a) * math.cos(base), 0.016 * math.cos(a) * math.sin(base),
                                0.016 * math.sin(a))) for a in (2 * math.pi * k / S(10, 6) for k in range(S(10, 6) + 1))]
        add_tube(part, "MyeongBlue", circ, [0.0028] * len(circ), n, segments=3, cap_start=False, cap_end=False)
    part.finish()

    part = Part("Myeong_Top")
    mk.add_lathe(part, "MyeongTop", [(0.07, NECK_Z + 0.015), (0.1, NECK_Z - 0.02), (0.095, 0.33), (0.09, 0.29)],
                 S(16, 8), Vector())
    ring_c = Vector((0.0, -0.07, NECK_Z + 0.0))
    ring = ring_xz(ring_c, 0.018, S(10, 6))
    add_tube(part, "MyeongMetal", ring, [0.0035] * len(ring), AXIS_Y, segments=3, cap_start=False, cap_end=False)
    for sx in (-1, 1):  # halter straps up to the ring
        add_tube(part, "MyeongBlack", [Vector((sx * 0.05, -0.085, NECK_Z - 0.04)), ring_c + Vector((sx * 0.012, 0, -0.01))],
                 [0.006, 0.004], AXIS_Z, segments=3)
    part.finish()

    part = Part("Myeong_Band")
    band = ring_z(Vector((0, 0, 0.3)), 0.098, S(20, 12))
    add_tube(part, "MyeongWhite", band, [0.007] * len(band), AXIS_Z, segments=S(4, 3), cap_start=False, cap_end=False)
    part.finish()


def add_lathe_boot(part, x):
    sole = [(0.051, 0.012), (0.051, 0.0), (0.0, 0.0)]
    mk.add_lathe(part, "MyeongBlue", sole, S(10, 6), Vector((x, 0.0, 0.0)))
    mk.add_lathe(part, "MyeongBoot", [(0.0, 0.115), (0.04, 0.115), (0.042, 0.065), (0.05, 0.035), (0.05, 0.012)],
                 S(10, 6), Vector((x, 0.0, 0.0)))
    add_cone(part, "MyeongBoot", trs(Vector((x, -0.035, 0.03)), Matrix(), (1, 1, 1)), 0.046, 0.04, 0.036,
             segments=S(10, 6), smooth=False)
    if MID:  # blue laces
        for k in range(3):
            z = 0.045 + k * 0.026
            add_tube(part, "MyeongBlue", [Vector((x - 0.025, -0.036, z)), Vector((x + 0.025, -0.036, z + 0.012))],
                     [0.0025, 0.0025], AXIS_X, segments=3)


def build_jacket():
    """Off-shoulder blue jacket with big black moe sleeves covering the hands; blue above the shoulder
    line and black below is a split in the texture."""
    part = Part("Myeong_Jacket")
    for sx in (-1, 1):
        shoulder = Vector((sx * 0.125, 0.0, 0.385))
        elbow = Vector((sx * 0.15, 0.0, 0.255))
        cuff = Vector((sx * 0.155, -0.01, 0.13))
        add_tube(part, "MyeongJacket", [shoulder, elbow, cuff], [0.042, 0.05, 0.048], AXIS_Y, segments=S(12, 6),
                 cap_start=True, cap_end=True)
    mk.add_lathe(part, "MyeongJacket", [(0.105, 0.42), (0.128, 0.38), (0.125, 0.31), (0.115, 0.25)], S(14, 8), Vector(),
                 gap=math.radians(70))
    part.finish()


# ---------------------------------------------------------------- uvs and outlines

def map_uvs(obj):
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    names = [s.material.name if s.material else "" for s in obj.material_slots]
    for f in bm.faces:
        mat = names[f.material_index] if f.material_index < len(names) else ""
        n = f.normal
        if mat == "MyeongSkin":
            for loop in f.loops:
                p = loop.vert.co
                if obj.name == "Myeong_Head" and n.y < -0.6:
                    s = (p.x - FACE_X[0]) / (FACE_X[1] - FACE_X[0])
                    t = (p.z - HEAD_CENTER.z - FACE_Z[0]) / (FACE_Z[1] - FACE_Z[0])
                    loop[uv].uv = cell_point(CELL_FACE, s, t)
                else:
                    loop[uv].uv = cell_point(CELL_FACE, 0.03, 0.03)
        elif mat == "MyeongTop":
            for loop in f.loops:
                t = (loop.vert.co.z - TOP_Z[0]) / (TOP_Z[1] - TOP_Z[0])
                loop[uv].uv = cell_point(CELL_TOP, 0.5, min(max(t, 0.02), 0.98))
        elif mat == "MyeongJacket":
            for loop in f.loops:
                t = (loop.vert.co.z - JACKET_Z[0]) / (JACKET_Z[1] - JACKET_Z[0])
                loop[uv].uv = cell_point(CELL_JACKET, 0.5, min(max(t, 0.02), 0.98))
        elif mat == "MyeongHair":
            for loop in f.loops:
                if obj.name == "Myeong_HairUnder":
                    loop[uv].uv = cell_point(CELL_HAIR, 0.5, 0.02)  # blue all over
                else:
                    t = (loop.vert.co.z - HAIR_Z[0]) / (HAIR_Z[1] - HAIR_Z[0])
                    loop[uv].uv = cell_point(CELL_HAIR, 0.5, min(max(t, 0.02), 0.98))
    bm.to_mesh(me)
    bm.free()


def add_outline(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * OUTLINE
    bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    for f in bm.faces:
        f.material_index = 0
    mesh = bpy.data.meshes.new(obj.name + "_Outline")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(bv.get_mat("MyeongOutline"))
    out = bpy.data.objects.new(obj.name + "_Outline", mesh)
    bpy.context.scene.collection.objects.link(out)
    return out


def main():
    global MID
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out")
    parser.add_argument("--preview")
    parser.add_argument("--detail", default="mid", choices=("mid", "low"))
    args = parser.parse_args(argv)
    MID = args.detail == "mid"
    out = os.path.abspath(args.out) if args.out else os.path.abspath(os.path.join(args.preview or ".", "Myeongchil.fbx"))
    atlas_path = os.path.join(os.path.dirname(out), "Textures", "MyeongchilAtlas.png")

    mk.HEAD_CENTER = HEAD_CENTER
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    paint_atlas(atlas_path)
    build_head()
    build_hair()
    build_head_gear()
    build_body()
    build_jacket()

    bv.get_mat("MyeongOutline").use_backface_culling = True
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in parts:
        map_uvs(obj)
        for p in obj.data.polygons:
            p.use_smooth = False
        if obj.name not in NO_OUTLINE and obj.name != "Myeong_HairUnder":
            add_outline(obj)

    total = 0
    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        total += tris
        if not obj.name.endswith("_Outline"):
            print(f"[myeong] {obj.name}: {tris} tris")
    print(f"[myeong] total {total} tris (outlines included, detail {args.detail})")
    if args.out:
        bv.export_fbx(out)
        print(f"[myeong] exported {args.out}")
    if args.preview:
        mk.PREVIEW_TURNS = ((0, ()), (35, ()), (90, ()), (180, ()), (0, ("Myeong_Jacket",)), (35, ("Myeong_Jacket",)))
        mk.render_preview(os.path.abspath(args.preview))


if __name__ == "__main__":
    main()
