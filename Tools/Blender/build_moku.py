"""
Builds the easter-egg Moku (the streamer of the Omangi fandom) as a low-poly smol chibi, and optionally
exports it to its own FBX. Reuses the mesh helpers of build_visitors.py.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_moku.py -- \
      [--out Assets/Models/Characters/Moku.fbx] [--preview <dir>]

Style (after the low-poly "smol" chibis): about two heads tall, flat-shaded facets, a box head with a
flat face whose features are painted in the texture, a big blunt-cut bob that makes the silhouette, and a
small simple body. Small print (face, tee print, jacket patches, arm warmers) lives in one atlas
(Textures/MokuAtlas.png next to the FBX); only the parts that carry the silhouette are geometry
(horns, saddle ears, piercings, choker, belts, chains, tail, boots).
Every part also gets an inverted-hull outline object (<part>_Outline) for the black contour line.
Moku_Jacket (and its outline) can be toggled: the tee's sleeves and arm warmers are underneath.
Origin between the feet, facing -Y like the visitors. Object name prefix: Moku_.
"""
import argparse
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_visitors as bv  # noqa: E402
from build_visitors import (AXIS_X, AXIS_Y, AXIS_Z, FRONT, Part, add_cone, add_sphere,  # noqa: E402
                            add_tube, basis, orient, track, trs)

NECK_Z = 0.40                      # top of the shoulders
HEAD_CENTER = Vector((0.0, 0.0, 0.64))
HEAD_HALF = Vector((0.23, 0.2, 0.2))  # box head half extents
NO_OUTLINE = ("Moku_Piercings", "Moku_Choker", "Moku_Belts", "Moku_Ears", "Moku_Tail")  # tiny metal bits: a contour would only blot them
OUTLINE = 0.01                    # outline hull thickness

# Atlas layout in pixels (x, y, w, h), origin bottom-left.
ATLAS_W, ATLAS_H = 512, 256
CELL_FACE = (0, 0, 256, 256)        # planar from the front over the face
CELL_SHIRT = (256, 128, 128, 128)   # planar from the front over the chest
CELL_JACKET = (384, 128, 128, 128)  # wrapped around the jacket
CELL_WARMER = (384, 64, 64, 64)     # arm warmer: black with a red stripe band
CELL_HAIR = (448, 64, 64, 64)       # hair: black above HAIR_SPLIT_Z, blue below (secret two-tone)
HAIR_Z = (0.40, 0.70)               # world z range the hair cell spans
HAIR_SPLIT_Z = 0.49
FACE_X = (-0.24, 0.24)
FACE_Z = (-0.24, 0.24)              # relative to HEAD_CENTER
SHIRT_X = (-0.1, 0.1)
SHIRT_Z = (0.255, 0.455)

bv.MATERIALS.update({
    # Textured materials are white: their colors come from the atlas.
    "MokuSkin": ((1.0, 1.0, 1.0), True),
    "MokuShirt": ((1.0, 1.0, 1.0), True),
    "MokuJacket": ((1.0, 1.0, 1.0), True),
    "MokuWarmer": ((1.0, 1.0, 1.0), True),
    "MokuHair": ((1.0, 1.0, 1.0), True),
    "MokuHairBlue": ((1.0, 1.0, 1.0), True),  # the blue pixel of the hair cell
    "MokuHorn": ((0.85, 0.06, 0.08), False),
    "MokuEar": ((0.07, 0.07, 0.08), False),
    "MokuLine": ((0.02, 0.02, 0.02), False),
    "MokuMetal": ((0.66, 0.66, 0.68), False),
    "MokuGold": ((0.85, 0.65, 0.20), False),
    "MokuGem": ((0.80, 0.04, 0.07), False),
    "MokuLining": ((0.62, 0.04, 0.06), False),
    "MokuSkirt": ((0.05, 0.05, 0.06), False),
    "MokuBoot": ((0.08, 0.08, 0.09), False),
    "MokuOutline": ((0.0, 0.0, 0.0), False),
})

SKIN = (0.97, 0.92, 0.91)
SHIRT = (0.05, 0.05, 0.06)
BLUE = (0.20, 0.44, 0.56)
RED = (0.75, 0.05, 0.08)

HEAD_BVH = None
HAIR_BVH = None
ATLAS_PATH = None


def px_uv(x, y):
    return (x / ATLAS_W, y / ATLAS_H)


def cell_point(cell, s, t):
    return px_uv(cell[0] + s * cell[2], cell[1] + t * cell[3])


# ---------------------------------------------------------------- atlas

def paint_atlas():
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

    def ring(cx, cy, r, width):
        d = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
        return np.abs(d - r) <= width * 0.5

    def rect(x0, y0, x1, y1):
        return (xs >= x0) & (xs < x1) & (ys >= y0) & (ys < y1)

    # Face (planar, meters relative to the head center -> pixels).
    fx = CELL_FACE[2] / (FACE_X[1] - FACE_X[0])

    def fp(x, z):
        return CELL_FACE[0] + (x - FACE_X[0]) * fx, CELL_FACE[1] + (z - FACE_Z[0]) * fx

    face = in_cell(CELL_FACE)
    fill(face, SKIN)
    # Sleepy eyes: gray with gold at the bottom, the top hidden under a heavy lid line, a lash flick
    # at the outer corner and a small highlight. The right one (+X) is mostly under the bangs.
    for sx in (-1, 1):
        cx, cy = fp(sx * 0.085, -0.02)
        a, b = 0.034 * fx, 0.04 * fx
        eye = face & ellipse(cx, cy, a, b)
        fill(eye, (0.50, 0.52, 0.57))
        fill(eye & (ys < cy - 0.35 * b), (0.88, 0.64, 0.18))
        fill(eye & ellipse(cx - sx * 0.25 * a, cy + 0.1 * b, 0.22 * a, 0.22 * b), (0.12, 0.12, 0.15))
        fill(face & ellipse(cx + sx * 0.2 * a, cy + 0.25 * b, 0.18 * a, 0.14 * b), (1.0, 1.0, 1.0))
        fill(eye & (ys > cy + 0.35 * b), SKIN)
        lid = cy + 0.35 * b
        fill(face & segment((cx - 1.1 * a, lid - 1), (cx + 1.1 * a, lid + 1), 0.011 * fx), (0.03, 0.03, 0.04))
        fill(face & segment((cx + sx * 1.0 * a, lid), (cx + sx * 1.45 * a, lid + 0.4 * b), 0.008 * fx),
             (0.03, 0.03, 0.04))
    # ":3" mouth, like Omangi's.
    cx, cy = fp(0.0, -0.105)
    half, height = 0.026 * fx, 0.012 * fx
    pts = [(cx + u, cy + 0.5 * height * math.cos(2 * math.pi * u / half)) for u in np.linspace(-half, half, 17)]
    fill(face & np.logical_or.reduce([segment(p, q, 0.007 * fx) for p, q in zip(pts, pts[1:])]), (0.03, 0.03, 0.04))
    # Blush on the open-eye cheek; the inverted cross on the other.
    fill(face & ellipse(*fp(-0.14, -0.075), 0.035 * fx, 0.016 * fx), (0.98, 0.70, 0.74))
    cx, cy = fp(0.13, -0.08)
    w = 0.008 * fx
    fill(face & rect(cx - w / 2, cy - 0.03 * fx, cx + w / 2, cy + 0.045 * fx), RED)
    fill(face & rect(cx - 0.019 * fx, cy - 0.012 * fx - w / 2, cx + 0.019 * fx, cy - 0.012 * fx + w / 2), RED)

    # Tee: an inverted pentagram in two concentric circles, in hair blue, and red lettering below.
    x, y, w, h = CELL_SHIRT
    shirt = in_cell(CELL_SHIRT)
    fill(shirt, SHIRT)
    cx, cy, r = x + w * 0.5, y + h * 0.58, w * 0.2
    fill(shirt & ring(cx, cy, r, 3.0), BLUE)
    fill(shirt & ring(cx, cy, r * 0.82, 2.0), BLUE)
    star = [(cx + r * 0.8 * math.sin(math.radians(180 + 144 * k)), cy + r * 0.8 * math.cos(math.radians(180 + 144 * k)))
            for k in range(6)]
    fill(shirt & np.logical_or.reduce([segment(p, q, 2.5) for p, q in zip(star, star[1:])]), BLUE)
    rng = random.Random(6)
    gx = x + w * 0.27
    for k in range(5):  # five blocky "letters"
        lx = gx + k * w * 0.095
        for _ in range(3):
            bx, by = lx + rng.uniform(0, 5), y + h * 0.27 + rng.uniform(0, 6)
            fill(shirt & rect(bx, by, bx + rng.uniform(2, 6), by + rng.uniform(2, 7)), RED)

    # Jacket: black, covered in patches, pins and lettering (busy, not legible).
    x, y, w, h = CELL_JACKET
    jacket = in_cell(CELL_JACKET)
    fill(jacket, (0.07, 0.07, 0.08))
    palette = [RED, (0.92, 0.70, 0.15), (0.35, 0.62, 0.66), (0.85, 0.85, 0.82), (0.45, 0.45, 0.48)]
    rng = random.Random(666)
    for _ in range(38):
        px, py, color = x + rng.uniform(4, w - 4), y + rng.uniform(4, h - 4), rng.choice(palette)
        kind = rng.random()
        if kind < 0.45:
            fill(jacket & ellipse(px, py, rng.uniform(2.5, 6), rng.uniform(2.5, 6)), color)
        elif kind < 0.75:
            fill(jacket & rect(px, py, px + rng.uniform(4, 9), py + rng.uniform(3, 7)), color)
        else:
            s = rng.uniform(2, 4)
            fill(jacket & (segment((px - s, py), (px + s, py), 1.5) | segment((px, py - s), (px, py + s), 1.5)), color)
    for k in range(5):  # "DEMON" down one sleeve
        fill(jacket & rect(x + w * 0.78, y + h * (0.75 - 0.11 * k), x + w * 0.88, y + h * (0.83 - 0.11 * k)),
             (0.95, 0.75, 0.15))


    # Arm warmer: black with a red band.
    x, y, w, h = CELL_WARMER
    warm = in_cell(CELL_WARMER)
    fill(warm, (0.06, 0.06, 0.07))
    fill(warm & rect(x, y + h * 0.55, x + w, y + h * 0.7), RED)

    # Hair: the two-tone is a split in the texture by height, so it cuts cleanly through the locks.
    x, y, w, h = CELL_HAIR
    hair = in_cell(CELL_HAIR)
    split = (HAIR_SPLIT_Z - HAIR_Z[0]) / (HAIR_Z[1] - HAIR_Z[0])
    fill(hair, (0.05, 0.05, 0.06))
    fill(hair & (ys < y + h * split), BLUE)

    atlas = bpy.data.images.new("MokuAtlas", ATLAS_W, ATLAS_H, alpha=False)
    atlas.pixels.foreach_set(img.ravel())
    os.makedirs(os.path.dirname(ATLAS_PATH), exist_ok=True)
    atlas.filepath_raw = ATLAS_PATH
    atlas.file_format = "PNG"
    atlas.save()
    bv.ATLAS = atlas  # textured materials sample this


# ---------------------------------------------------------------- helpers

def face_point(x, z, lift=0.0):
    """Point on the flat face at (x, z relative to the head center)."""
    loc, n, _, _ = HEAD_BVH.ray_cast(Vector((x, -1.0, HEAD_CENTER.z + z)), AXIS_Y)
    if loc is None:  # past the chamfered edge: stay on the face plane
        loc, n = Vector((x, -HEAD_HALF.y, HEAD_CENTER.z + z)), FRONT
    return loc + n * lift, n


def hair_point(direction, lift=0.0):
    d = Vector(direction).normalized()
    loc, n, _, _ = HAIR_BVH.ray_cast(HEAD_CENTER + d * 1.0, -d)
    return loc + n * lift, n


def ring_points(center, rx, ry, count=12, close=True):
    """Horizontal ellipse around center (front at -Y)."""
    steps = count + (1 if close else 0)
    return [center + Vector((rx * math.sin(a), -ry * math.cos(a), 0.0))
            for a in (2 * math.pi * k / count for k in range(steps))]


def add_lathe(part, mat, profile, segments, center=Vector(), outward=1, gap=0.0, wobble=None):
    """Faceted surface of revolution around the Z axis through center. profile: (radius, z) from the
    top down; a leading radius 0 closes the top with a fan. gap: half-angle (radians) left open at the
    front. wobble(k, i): optional radius factor per segment k and ring i (pleats, blunt ends)."""
    bm = part.bm
    index = part.mat_index(mat)
    closed = gap == 0.0
    count = segments if closed else segments + 1
    angles = [gap + (2 * math.pi - 2 * gap) * k / segments for k in range(count)]
    top = profile[0][0] == 0.0
    bottom = len(profile) > 1 and profile[-1][0] == 0.0  # a trailing radius 0 closes the bottom flat
    rings = []
    for i, (r, z) in enumerate(profile[1 if top else 0:-1 if bottom else None]):
        ring = []
        for k, a in enumerate(angles):
            rr = r * (wobble(k, i) if wobble else 1.0)
            ring.append(bm.verts.new(center + Vector((rr * math.sin(a), -rr * math.cos(a), z))))
        rings.append(ring)
    faces = []
    span = range(segments) if closed else range(segments)
    if top:
        apex = bm.verts.new(center + Vector((0.0, 0.0, profile[0][1])))
        faces += [bm.faces.new((rings[0][k], rings[0][(k + 1) % count], apex)) for k in span]
    for a, b in zip(rings, rings[1:]):
        for k in span:
            k2 = (k + 1) % count
            faces.append(bm.faces.new((a[k], a[k2], b[k2], b[k])))
    if bottom:
        base = bm.verts.new(center + Vector((0.0, 0.0, profile[-1][1])))
        for k in span:
            f = bm.faces.new((rings[-1][k], rings[-1][(k + 1) % count], base))
            orient(f, -AXIS_Z)
            f.material_index = index
            for loop in f.loops:
                loop[part.uv].uv = bv.WHITE_UV
    for f in faces:
        c = f.calc_center_median() - center
        orient(f, Vector((c.x, c.y, c.z * 0.3)) * outward)
        f.material_index = index
        for loop in f.loops:
            loop[part.uv].uv = bv.WHITE_UV
    return faces


def add_clump(part, mat, points, normals, widths, thickness):
    """Flat hair clump along points, lying on the surface (normals), its end cut off square."""
    bm = part.bm
    index = part.mat_index(mat)
    count = len(points)
    rings = []
    for i, (p, n) in enumerate(zip(points, normals)):
        t = (points[min(i + 1, count - 1)] - points[max(i - 1, 0)]).normalized()
        w = n.cross(t).normalized() * widths[i] * 0.5
        h = n * thickness * 0.5
        rings.append([bm.verts.new(p + w), bm.verts.new(p + h), bm.verts.new(p - w), bm.verts.new(p - h)])
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(4):
            faces.append(bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k])))
    faces += [bm.faces.new(rings[0][::-1]), bm.faces.new(rings[-1])]
    for f in faces:
        c = f.calc_center_median()
        nearest = min(points, key=lambda q: (q - c).length)
        orient(f, c - nearest if (c - nearest).length > 1e-6 else normals[0])
        f.material_index = index
        for loop in f.loops:
            loop[part.uv].uv = bv.WHITE_UV


# ---------------------------------------------------------------- head

def build_head():
    """Faceted rounded box head (features in the atlas), and the neck with a spiked choker. The box is cut
    once in each direction and pulled toward an ellipsoid, so each side folds along its middle: the face
    gets a crease down the nose line and across, and reads as round instead of a flat plate."""
    global HEAD_BVH
    part = Part("Moku_Head")
    bmesh.ops.create_cube(part.bm, size=1.0,
                          matrix=Matrix.Translation(HEAD_CENTER) @ Matrix.Diagonal((*(HEAD_HALF * 2), 1.0)))
    bmesh.ops.subdivide_edges(part.bm, edges=list(part.bm.edges), cuts=1, use_grid_fill=True)
    radii = Vector((0.26, 0.25, 0.22))
    for v in part.bm.verts:
        d = v.co - HEAD_CENTER
        n = Vector((d.x / radii.x, d.y / radii.y, d.z / radii.z))
        v.co = HEAD_CENTER + d.lerp(d / n.length, 0.7)
    part.assign(list(part.bm.verts), "MokuSkin")
    HEAD_BVH = BVHTree.FromBMesh(part.bm)
    neck_bottom, neck_top = NECK_Z - 0.02, HEAD_CENTER.z - HEAD_HALF.z + 0.03
    add_cone(part, "MokuSkin", trs(Vector((0, 0, (neck_bottom + neck_top) * 0.5)), Matrix(), (1, 1, 1)),
             0.065, 0.06, neck_top - neck_bottom, segments=6)
    part.finish()

    part = Part("Moku_Choker")
    z = NECK_Z + 0.025
    band = ring_points(Vector((0, 0, z)), 0.07, 0.07, 6)
    add_tube(part, "MokuLine", band, [0.013] * len(band), AXIS_Z, segments=4, cap_start=False, cap_end=False)
    for k in (-2, -1, 1, 2):
        a = math.radians(k * 30)
        out = Vector((math.sin(a), -math.cos(a), 0.0))
        add_cone(part, "MokuMetal", trs(Vector((0, 0, z)) + out * 0.083, track(out), (1, 1, 1)),
                 0.009, 0.001, 0.022, segments=4)
    add_sphere(part, "MokuGem", trs(Vector((0, -0.085, z)), Matrix(), (0.013, 0.008, 0.013)), u=4, v=3)
    part.finish()


def add_slab(part, mat, grid, thickness):
    """Thick hair lock from a grid of outer points (rows top to bottom, columns side to side): the outer
    surface, an inner one pushed in toward the head axis by thickness, and the rim closing them, so the
    blunt-cut end reads as a solid block."""
    bm = part.bm
    index = part.mat_index(mat)

    def inward(p):
        d = p - HEAD_CENTER
        return Vector((d.x, d.y, 0.0)).normalized()

    outer = [[bm.verts.new(p) for p in row] for row in grid]
    inner = [[bm.verts.new(p - inward(p) * thickness) for p in row] for row in grid]
    rows, cols = len(grid), len(grid[0])
    faces = []
    for i in range(rows - 1):
        for j in range(cols - 1):
            for layer, sign in ((outer, 1), (inner, -1)):
                f = bm.faces.new((layer[i][j], layer[i][j + 1], layer[i + 1][j + 1], layer[i + 1][j]))
                faces.append((f, sign))
    rim = [(i, 0) for i in range(rows)] + [(rows - 1, j) for j in range(1, cols)] \
        + [(i, cols - 1) for i in range(rows - 2, -1, -1)] + [(0, j) for j in range(cols - 2, 0, -1)]
    for (i0, j0), (i1, j1) in zip(rim, rim[1:] + rim[:1]):
        f = bm.faces.new((outer[i0][j0], outer[i1][j1], inner[i1][j1], inner[i0][j0]))
        faces.append((f, 0))
    middle = grid[rows // 2][cols // 2] - inward(grid[rows // 2][cols // 2]) * thickness * 0.5
    for f, sign in faces:
        c = f.calc_center_median()
        orient(f, inward(c) * sign if sign else c - middle)
        f.material_index = index
        for loop in f.loops:
            loop[part.uv].uv = bv.WHITE_UV


def build_hair():
    """Secret two-tone bob after the smol chibis: a rounded cap hugging the forehead, then separate
    blunt-cut locks that bulge at the ears and tuck in toward the jaw (a mushroom bob) in uneven lengths,
    over a blue under layer whose tips show below them; locks framing the face at both cheeks; a heavy
    square-cut fringe across the whole forehead (down to the lid of the open eye, longer over the other);
    the blue parting below the right horn; an ahoge."""
    global HAIR_BVH
    part = Part("Moku_Hair")
    # Cap: rounded crown down to the temples, (radius, z relative to the head center). Dense enough for
    # the parting to be shaped into it.
    cap = [(0.0, 0.27)] + [(0.318 * math.cos(math.radians(e)) ** 0.8, 0.27 * math.sin(math.radians(e)))
                           for e in (78, 66, 55, 45, 36, 28, 21, 15, 10, 5, 0, -4)]
    add_lathe(part, "MokuHair", cap, 40, HEAD_CENTER)
    window = [f for f in part.bm.faces
              if (c := f.calc_center_median() - HEAD_CENTER).z < 0.13
              and abs(math.degrees(math.atan2(c.x, -c.y))) < 42]
    bmesh.ops.delete(part.bm, geom=window, context="FACES")
    # Pull the front of the cap onto the forehead, so it is a hairline and not a visor.
    for v in part.bm.verts:
        d = v.co - HEAD_CENTER
        front = max(0.0, -d.y / max(Vector((d.x, d.y)).length, 1e-6) - 0.5) / 0.5
        low = min(1.0, max(0.0, (0.24 - d.z) / 0.1))
        if front * low > 0.0:
            n = d.normalized()
            loc, _, _, _ = HEAD_BVH.ray_cast(HEAD_CENTER + n, -n)
            if loc is not None:
                v.co = v.co.lerp(loc + n * 0.015, front * low)

    # Parting: it starts just in front of the right horn (-X), where the ahoge springs up, and runs forward
    # to the hairline. The hair splits there: both sides swell into a ridge and fall away from the line,
    # the line itself sinks into a groove where the blue under layer shows (the strip added below).
    part_start = Vector((-0.38, -0.12, 0.92)).normalized()
    part_end = Vector((-0.24, -0.8, 0.56)).normalized()
    plane = part_start.cross(part_end).normalized()
    span = part_start.angle(part_end)
    for v in part.bm.verts:
        d = (v.co - HEAD_CENTER).normalized()
        side = d.dot(plane)  # signed distance from the parting (unit sphere)
        p = (d - plane * side).normalized()
        along = math.atan2(part_start.cross(p).dot(plane), part_start.dot(p))
        if along < 0.0:  # behind the start the ridges round off into the whorl
            reach = math.exp(-(along / 0.12) ** 2)
        elif along > span + 0.2:
            reach = 0.0
        else:
            reach = 1.0
        groove = -0.014 * math.exp(-(side / 0.05) ** 2)
        ridge = 0.024 * math.exp(-((abs(side) - 0.12) / 0.07) ** 2)
        v.co += d * (groove + ridge) * reach

    # Blue under layer: tucked in like the locks and a little longer, so only its tips show below them,
    # open at the face (seen from outside only; the camera never looks up into it).
    under = [(0.288, 0.06), (0.29, -0.1), (0.265, -0.19), (0.245, -0.235)]
    add_lathe(part, "MokuHairBlue", under, 10, HEAD_CENTER, 1, gap=math.radians(40))
    HAIR_BVH = BVHTree.FromBMesh(part.bm)

    def lock(a0, a1, z_end, tuck, mat="MokuHair", r_top=0.31):
        """Lock between angles a0..a1 (degrees, 0 = front, + toward +X) from under the cap down to z_end,
        bulging a little at the middle and tucked in by `tuck` at the end."""
        grid = []
        for z, t in ((0.03, 0.0), (-0.09, 0.45), (z_end, 1.0)):
            r = r_top + 0.014 * math.sin(math.pi * t) - tuck * t * t
            grid.append([HEAD_CENTER + Vector((r * math.sin(math.radians(a)), -r * math.cos(math.radians(a)), z))
                         for a in (a0, (a0 + a1) / 2, a1)])
        add_slab(part, mat, grid, 0.022)

    # Face-framing locks at both cheeks, reaching the jaw and tucked in toward the face.
    for sx in (-1, 1):
        lock(sx * 40, sx * 64, -0.19, 0.045)
    # Side and back locks in uneven lengths, tucked in toward the jaw; the blue tips show below them.
    ends = (-0.2, -0.185, -0.215, -0.195, -0.21, -0.185, -0.2, -0.19)
    for k, z_end in enumerate(ends):
        a0 = 62 + k * 30
        lock(a0, a0 + 33, z_end, 0.035)

    # Fringe: broad square-cut flaps across the whole forehead. Down to the lid of the open eye (-X),
    # longer and swept over the other (+X); the blue one continues the parting.
    flaps = [(-0.19, 0.0, 0.018, "MokuHair"), (-0.125, 0.0, 0.012, "MokuHairBlue"), (-0.06, 0.005, 0.01, "MokuHair"),
             (0.0, 0.02, 0.0, "MokuHair"), (0.06, 0.035, -0.05, "MokuHair"), (0.125, 0.025, -0.055, "MokuHair"),
             (0.19, 0.0, -0.03, "MokuHair")]  # the long side stops above the cheek cross
    for x0, sweep, z_end, mat in flaps:
        ts = (0.0, 0.5, 1.0)
        pts = [face_point(x0 + sweep * t, 0.16 + (z_end - 0.16) * t, 0.03 * (1 - t) + 0.012)[0] for t in ts]
        add_clump(part, mat, pts, [FRONT] * 3, [0.088, 0.086, 0.082], 0.02)

    # The blue under layer showing in the parting groove.
    pts, normals = [], []
    for k in range(17):
        p, n = hair_point(part_start.lerp(part_end, k / 16), 0.008)
        pts.append(p)
        normals.append(n)
    bv.add_ribbon(part, "MokuHairBlue", pts, normals, 0.04)

    # Ahoge: one strand springing up from where the parting starts and curling forward.
    root, n = hair_point(part_start)
    ts = [i / 6 for i in range(7)]
    ahoge = [root - n * 0.015 + Vector((0.11 * t, -0.07 * math.sin(math.pi * t * 0.9), 0.16 * math.sin(math.pi * t * 0.6)))
             for t in ts]
    add_tube(part, "MokuHair", ahoge, [0.014 * (1 - t) + 0.004 for t in ts], AXIS_X, segments=4, cap_start=False)
    part.finish()


def build_horns():
    """Glossy red horns, simple cones out of the hair."""
    part = Part("Moku_Horns")
    for sx in (-1, 1):
        anchor, n = hair_point((sx * 0.45, 0.05, 0.88))
        d = (n + AXIS_Z * 1.2).normalized()
        start = anchor - d * 0.03
        add_tube(part, "MokuHorn", [start, start + d * 0.09, start + d * 0.18 + AXIS_X * sx * 0.01],
                 [0.05, 0.028, 0.003], AXIS_Y, segments=5, cap_start=False)
    part.finish()


def add_saddle_ear(part, root, d, length, half_width, thickness):
    """Saddle-shaped floppy ear: a leaf whose black outer face looks forward, with the upper edge bent
    back and the lower edge turned out toward the front, showing a blue cuff (the blue underside),
    curving down toward the tip. Returns a function giving points on its front face (u along, v across:
    -1 lower edge, 1 upper edge), for the piercings."""
    bm = part.bm
    face = FRONT * 0.75 + AXIS_Z * 0.65
    face = (face - d * face.dot(d)).normalized()
    across = face.cross(d).normalized()
    if across.z < 0:
        across = -across
    us = [i / 4 for i in range(5)]
    vs = [-1.0, -0.4, 0.3, 1.0]

    def point(u, v, layer):
        width = half_width * math.sin(math.pi * (0.12 + 0.88 * u)) ** 0.7
        drop = AXIS_Z * 0.05 * u * u
        bend = (-0.03 if v > 0 else 0.03) * v * v * (0.4 + u)
        return (root + d * length * u + across * width * v - drop
                + face * (bend + layer * thickness * (1 - u)))

    for layer, outward in ((0.5, 1), (-0.5, -1)):
        grid = [[bm.verts.new(point(u, v, layer)) for v in vs] for u in us]
        for i in range(len(us) - 1):
            for j in range(len(vs) - 1):
                f = bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
                orient(f, face * outward)
                cuff = outward > 0 and j == 0
                f.material_index = part.mat_index("MokuEar" if outward > 0 and not cuff else "MokuHairBlue")
                for loop in f.loops:
                    loop[part.uv].uv = bv.WHITE_UV
    # The tip narrows to a point: weld its collapsed vertices and drop the faces that degenerate.
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=1e-5)
    return lambda u, v, lift=0.0: point(u, v, 0.5) + face * lift


def build_ears():
    """Saddle ears out of the hair at both sides. Right ear (-X): red ring, stud and a dangling inverted
    cross. Left ear (+X): two studs joined by a short chain."""
    part = Part("Moku_Ears")
    ears = {}
    for sx in (-1, 1):
        anchor, _ = hair_point((sx, -0.05, 0.1))
        d = Vector((sx * 0.85, -0.1, -0.4)).normalized()
        ears[sx] = add_saddle_ear(part, anchor - d * 0.03, d, 0.2, 0.085, 0.02)
    part.finish()

    part = Part("Moku_Piercings")
    ear = ears[-1]
    add_sphere(part, "MokuGem", trs(ear(0.45, -0.75, 0.012), Matrix(), (0.013, 0.013, 0.013)), u=5, v=3)
    add_sphere(part, "MokuMetal", trs(ear(0.62, -0.7, 0.01), Matrix(), (0.008, 0.008, 0.008)), u=4, v=3)
    hook = ear(0.8, -0.85, 0.008)
    cross = hook - AXIS_Z * 0.07
    add_tube(part, "MokuMetal", [hook, cross + AXIS_Z * 0.032], [0.004, 0.004], AXIS_Y, segments=3)
    add_tube(part, "MokuMetal", [cross + AXIS_Z * 0.032, cross - AXIS_Z * 0.04], [0.007, 0.007], AXIS_Y, segments=3)
    add_tube(part, "MokuMetal", [cross - AXIS_X * 0.02 - AXIS_Z * 0.018, cross + AXIS_X * 0.02 - AXIS_Z * 0.018],
             [0.007, 0.007], AXIS_Y, segments=3)
    ear = ears[1]
    studs = [ear(0.55, -0.75, 0.01), ear(0.8, -0.75, 0.01)]
    for c in studs:
        add_cone(part, "MokuMetal", trs(c, track(FRONT), (1, 1, 1)), 0.011, 0.011, 0.011, segments=3)
    chain = [studs[0].lerp(studs[1], k / 2) - AXIS_Z * 0.03 * math.sin(math.pi * k / 2) for k in range(3)]
    add_tube(part, "MokuMetal", chain, [0.004] * len(chain), FRONT, segments=3, cap_start=False, cap_end=False)
    part.finish()


# ---------------------------------------------------------------- body

def build_body():
    """Small simple body: boots, bare legs (a red garter on her right thigh, a red dashed cut line
    around her left thigh), high-waisted pleated skirt with a layered double belt and
    a hip chain on the left, the tee (print in the atlas), arms in black warmers, silver necklaces."""
    part = Part("Moku_Boots")
    for sx in (-1, 1):
        x = sx * 0.055
        add_lathe(part, "MokuBoot", [(0.0, 0.11), (0.04, 0.11), (0.042, 0.06), (0.05, 0.035), (0.05, 0.0), (0.0, 0.0)], 6,
                  Vector((x, 0.0, 0.0)))
        add_cone(part, "MokuBoot", trs(Vector((x, -0.035, 0.025)), Matrix(), (1, 1, 1)), 0.045, 0.04, 0.05,
                 segments=6, smooth=False)  # flat-soled toe
        if sx < 0:  # slouchy leg warmer over the right boot
            add_lathe(part, "MokuBoot", [(0.05, 0.13), (0.056, 0.1), (0.052, 0.075)], 6, Vector((x, 0.0, 0.0)))
    part.finish()

    part = Part("Moku_Legs")
    for sx in (-1, 1):
        x = sx * 0.055
        add_lathe(part, "MokuSkin", [(0.027, 0.22), (0.025, 0.16), (0.022, 0.1)], 6, Vector((x, 0.0, 0.0)))
    garter = ring_points(Vector((-0.055, 0.0, 0.17)), 0.029, 0.029, 6)
    add_tube(part, "MokuLining", garter, [0.006] * len(garter), AXIS_Z, segments=3, cap_start=False, cap_end=False)
    # Dashed "cut here" line around her left thigh: short red dashes standing just off the skin.
    for k in range(6):
        a0, a1 = 2 * math.pi * k / 6, 2 * math.pi * (k + 0.5) / 6
        dash = [Vector((0.055 + 0.0265 * math.sin(a), -0.0265 * math.cos(a), 0.158)) for a in (a0, a1)]
        add_tube(part, "MokuGem", dash, [0.0035, 0.0035], AXIS_Z, segments=3)
    part.finish()

    part = Part("Moku_Skirt")
    # High-waisted pleats: alternating radius at the hem.
    add_lathe(part, "MokuSkirt", [(0.085, 0.31), (0.1, 0.25), (0.145, 0.175)], 12, Vector(),
              wobble=lambda k, i: 1.0 if i < 2 or k % 2 == 0 else 0.9)
    part.finish()

    part = Part("Moku_Belts")
    for z, r, tilt in ((0.295, 0.092, 0.0), (0.27, 0.1, 0.015)):  # layered: the lower one slung at an angle
        band = [p + AXIS_Z * tilt * p.x / r for p in ring_points(Vector((0, 0, z)), r, r, 8)]
        add_tube(part, "MokuLine", band, [0.008] * len(band), AXIS_Z, segments=3, cap_start=False, cap_end=False)
        add_cone(part, "MokuMetal", trs(Vector((0, -r - 0.006, z)), track(FRONT), (1.3, 0.8, 1)),
                 0.012, 0.012, 0.004, segments=4)
    chain = [Vector((0.03 + 0.07 * t, -0.098 + 0.06 * t * t, 0.265 - 0.045 * math.sin(math.pi * t))) for t in
             (k / 3 for k in range(4))]
    add_tube(part, "MokuMetal", chain, [0.004] * len(chain), AXIS_Y, segments=3, cap_start=False, cap_end=False)
    part.finish()

    part = Part("Moku_Clothes")
    add_lathe(part, "MokuShirt", [(0.0, NECK_Z + 0.015), (0.07, NECK_Z + 0.01), (0.1, NECK_Z - 0.02),
                                  (0.095, 0.33), (0.09, 0.29)], 8, Vector())
    for drop, pendant in ((0.025, False), (0.05, True)):
        chain = []
        for a in (math.radians(t) for t in range(-90, 91, 45)):
            sag = math.cos(a) ** 2
            chain.append(Vector((0.075 * math.sin(a), -0.075 * math.cos(a) - 0.03 * sag, NECK_Z + 0.008 - drop * sag)))
        add_tube(part, "MokuMetal", chain, [0.0035] * len(chain), AXIS_Z, segments=3, cap_start=False, cap_end=False)
        if pendant:
            add_sphere(part, "MokuMetal", trs(chain[len(chain) // 2] + Vector((0, -0.004, -0.012)), Matrix(),
                                              (0.011, 0.005, 0.013)), u=4, v=3)
    part.finish()

    # Arms: short tee sleeves, then black arm warmers with a red band, small hands.
    part = Part("Moku_Arms")
    for sx in (-1, 1):
        shoulder = Vector((sx * 0.085, 0.0, 0.385))
        wrist = Vector((sx * 0.155, -0.01, 0.255))
        elbow = shoulder.lerp(wrist, 0.35)
        add_tube(part, "MokuShirt", [shoulder, elbow], [0.034, 0.033], AXIS_Y, segments=5)
        add_tube(part, "MokuWarmer", [elbow, wrist], [0.029, 0.027], AXIS_Y, segments=5, cap_start=False)
        add_sphere(part, "MokuSkin", trs(wrist + (wrist - shoulder).normalized() * 0.022, Matrix(),
                                         (0.024, 0.022, 0.026)), u=5, v=3)
    part.finish()


def build_jacket():
    """Oversized patched jacket, open in front, red lining and collar, and puffy moe sleeves (red cuff
    stripes) running past the hands and swallowing them."""
    part = Part("Moku_Jacket")
    profile = [(0.085, NECK_Z + 0.015), (0.115, NECK_Z - 0.01), (0.125, 0.3), (0.135, 0.2)]
    add_lathe(part, "MokuJacket", profile, 10, Vector(), 1, gap=math.radians(34))
    add_lathe(part, "MokuLining", [(r * 0.95, z) for r, z in profile], 10, Vector(), -1, gap=math.radians(34))
    collar = [Vector((0.09 * math.sin(a), -0.09 * math.cos(a), NECK_Z + 0.02 - 0.02 * math.cos(a)))
              for a in (math.radians(t) for t in range(50, 311, 26))]
    add_tube(part, "MokuLining", collar, [0.018] * len(collar), AXIS_Z, segments=4)
    for sx in (-1, 1):
        shoulder = Vector((sx * 0.1, 0.0, 0.39))
        cuff = Vector((sx * 0.178, -0.016, 0.195))  # past the hand (Moku_Arms), so the sleeve hides it
        mid = shoulder.lerp(cuff, 0.45) + AXIS_X * sx * 0.02
        add_tube(part, "MokuJacket", [shoulder, mid, cuff], [0.042, 0.058, 0.054], AXIS_Y, segments=6)
        d = (cuff - mid).normalized()
        add_tube(part, "MokuLining", [cuff - d * 0.03, cuff - d * 0.016], [0.058, 0.058], AXIS_Y, segments=6,
                 cap_start=False, cap_end=False)
    part.finish()


def build_tail():
    """Thin black devil tail from under the skirt, a red bow near the base, spade tip."""
    part = Part("Moku_Tail")
    base = Vector((0.0, 0.09, 0.2))
    pts = [base + Vector((-0.1 * t, 0.06 * math.sin(math.pi * t), -0.15 * t)) for t in (k / 4 for k in range(5))]
    add_tube(part, "MokuLine", pts, [0.01] * len(pts), AXIS_X, segments=4, cap_start=False)
    d = (pts[-1] - pts[-2]).normalized()
    add_cone(part, "MokuLine", trs(pts[-1] + d * 0.02, track(d), (1, 0.35, 1)), 0.03, 0.001, 0.045, segments=4)
    bow = pts[1]
    for sx in (-1, 1):
        add_cone(part, "MokuLining", trs(bow + AXIS_X * sx * 0.016, track(AXIS_X * sx), (1, 0.5, 1)),
                 0.005, 0.02, 0.028, segments=4)
    part.finish()


# ---------------------------------------------------------------- uv, shading, outline

def map_uvs(obj):
    """UVs for the atlas-textured materials (everything else samples nothing)."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    names = [s.material.name if s.material else "" for s in obj.material_slots]

    for f in bm.faces:
        mat = names[f.material_index] if f.material_index < len(names) else ""
        c = f.calc_center_median()
        n = f.normal
        if mat == "MokuSkin":
            if obj.name == "Moku_Head" and n.y < -0.6:
                for loop in f.loops:
                    p = loop.vert.co
                    s = (p.x - FACE_X[0]) / (FACE_X[1] - FACE_X[0])
                    t = (p.z - HEAD_CENTER.z - FACE_Z[0]) / (FACE_Z[1] - FACE_Z[0])
                    loop[uv].uv = cell_point(CELL_FACE, s, t)
            else:
                for loop in f.loops:
                    loop[uv].uv = cell_point(CELL_FACE, 0.03, 0.03)
        elif mat == "MokuShirt":
            front = obj.name == "Moku_Clothes" and n.y < -0.4
            for loop in f.loops:
                p = loop.vert.co
                if front:
                    s = (p.x - SHIRT_X[0]) / (SHIRT_X[1] - SHIRT_X[0])
                    t = (p.z - SHIRT_Z[0]) / (SHIRT_Z[1] - SHIRT_Z[0])
                    loop[uv].uv = cell_point(CELL_SHIRT, min(max(s, 0.01), 0.99), min(max(t, 0.01), 0.99))
                else:
                    loop[uv].uv = cell_point(CELL_SHIRT, 0.03, 0.03)
        elif mat == "MokuJacket":
            ca = math.atan2(c.x, -c.y)
            for loop in f.loops:
                p = loop.vert.co
                a = math.atan2(p.x, -p.y)
                a = ca + (a - ca + math.pi) % (2 * math.pi) - math.pi  # unwrap around the face center
                s = 0.5 + a / (2 * math.pi)
                t = (p.z - 0.18) / 0.25
                loop[uv].uv = cell_point(CELL_JACKET, min(max(s, 0.01), 0.99), min(max(t, 0.01), 0.99))
        elif mat == "MokuHair":
            for loop in f.loops:
                t = (loop.vert.co.z - HAIR_Z[0]) / (HAIR_Z[1] - HAIR_Z[0])
                loop[uv].uv = cell_point(CELL_HAIR, 0.5, min(max(t, 0.02), 0.98))
        elif mat == "MokuHairBlue":
            for loop in f.loops:
                loop[uv].uv = cell_point(CELL_HAIR, 0.5, 0.02)  # blue all over
        elif mat == "MokuWarmer":
            for loop in f.loops:
                t = (loop.vert.co.z - 0.25) / 0.1
                loop[uv].uv = cell_point(CELL_WARMER, 0.5, min(max(t, 0.02), 0.98))
        else:  # flat colors sample the white pixel (a boolean leaves interpolated garbage)
            for loop in f.loops:
                loop[uv].uv = bv.WHITE_UV
    bm.to_mesh(me)
    bm.free()


def union_closed(obj):
    """Boolean-unions the closed pieces of a part into one skin, so the pieces that overlap (toe and boot,
    neck and head) lose the faces buried inside each other. Open pieces (skirts, lathes without caps)
    are left as they are."""
    scene = bpy.context.scene
    name = obj.name
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="LOOSE")
    bpy.ops.object.mode_set(mode="OBJECT")
    pieces = [o for o in scene.objects if o.name == name or o.name.startswith(name + ".")]
    closed = []
    for o in pieces:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        if all(e.is_manifold for e in bm.edges) and len(bm.faces) > 3:
            closed.append(o)
        bm.free()
    if len(closed) > 1:
        base = max(closed, key=lambda o: len(o.data.polygons))
        for other in closed:
            if other is base:
                continue
            bpy.context.view_layer.objects.active = base
            mod = base.modifiers.new("union", "BOOLEAN")
            mod.operation = "UNION"
            mod.solver = "EXACT"
            mod.object = other
            bpy.ops.object.modifier_apply(modifier=mod.name)
            pieces.remove(other)
            bpy.data.objects.remove(other)
    bpy.ops.object.select_all(action="DESELECT")
    for o in pieces:
        o.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    if len(pieces) > 1:
        bpy.ops.object.join()
    bpy.context.view_layer.objects.active.name = name
    return bpy.context.view_layer.objects.active


def dissolve_flat(obj, angle=2.0):
    """Retopology of the flat bits: merges the faces that lie in one plane (the splinters a boolean leaves,
    and the extra cuts across flat sides) into single polygons, keeping every material border. The UVs
    are recomputed from the positions afterwards, so the atlas still lines up."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(angle), verts=list(bm.verts), edges=list(bm.edges),
                             delimit={"MATERIAL"})
    bm.to_mesh(obj.data)
    bm.free()


def add_outline(obj):
    """Inverted hull: a copy pushed out along the vertex normals with the faces flipped, so only its
    inside shows behind the silhouette edges as a black line (back-face culled)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    # Weld coincident vertices so each piece's hull is closed and the line has no cracks.
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
    mesh.materials.append(bv.get_mat("MokuOutline"))
    out = bpy.data.objects.new(obj.name + "_Outline", mesh)
    bpy.context.scene.collection.objects.link(out)
    return out


# ---------------------------------------------------------------- preview

# (turn in degrees, object name prefixes left out)
PREVIEW_TURNS = ((0, ()), (35, ()), (90, ()), (180, ()), (0, ("Moku_Jacket",)), (35, ("Moku_Jacket",)))


def render_preview(out_dir):
    scene = bpy.context.scene
    parts = [o for o in scene.objects if o.type == "MESH"]
    for i, (turn, skip) in enumerate(PREVIEW_TURNS):
        for src in parts:
            if src.name.startswith(skip) if skip else False:
                continue
            obj = src.copy()
            scene.collection.objects.link(obj)
            obj.matrix_world = Matrix.Translation(((i - (len(PREVIEW_TURNS) - 1) / 2) * 0.85, 0, 0)) \
                @ Matrix.Rotation(math.radians(turn), 4, "Z") @ src.matrix_world
    for src in parts:
        src.hide_render = True

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x, scene.render.resolution_y = 3600, 1000
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.55, 0.55, 0.58)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.5
    sun.rotation_euler = [math.radians(a) for a in (50, 0, 20)]
    scene.collection.objects.link(sun)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 5.2
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (0, -5, 0.5)
    cam.rotation_euler = [math.radians(a) for a in (90, 0, 0)]
    scene.render.filepath = os.path.join(out_dir, "moku.png")
    bpy.ops.render.render(write_still=True)


def main():
    global ATLAS_PATH
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out")
    parser.add_argument("--preview")
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out) if args.out else os.path.abspath(os.path.join(args.preview or ".", "Moku.fbx"))
    ATLAS_PATH = os.path.join(os.path.dirname(out), "Textures", "MokuAtlas.png")

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    paint_atlas()
    build_head()
    build_hair()
    build_horns()
    build_ears()
    build_body()
    build_jacket()
    build_tail()

    bv.get_mat("MokuOutline").use_backface_culling = True
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in list(parts):
        if obj.name not in ("Moku_Jacket", "Moku_Hair"):
            merged = union_closed(obj)
            before = sum(len(p.vertices) - 2 for p in merged.data.polygons)
            dissolve_flat(merged)
            print(f"[moku] retopo {merged.name}: {before} -> {sum(len(p.vertices) - 2 for p in merged.data.polygons)} tris")
    parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    for obj in parts:
        map_uvs(obj)
        for p in obj.data.polygons:
            p.use_smooth = False  # flat-shaded facets
        if obj.name not in NO_OUTLINE:
            add_outline(obj)

    total = 0
    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        total += tris
        if not obj.name.endswith("_Outline"):
            print(f"[moku] {obj.name}: {tris} tris")
    print(f"[moku] total {total} tris (outlines included)")
    if args.out:
        bv.export_fbx(out)
        print(f"[moku] exported {args.out}")
    if args.preview:
        render_preview(os.path.abspath(args.preview))


if __name__ == "__main__":
    main()
