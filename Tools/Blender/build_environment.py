"""
Builds the low-poly Halloween background props and exports one FBX per group.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_environment.py -- \
      --out Assets/Models/Environment [--preview <dir>]

Conventions:
  - Geometry is authored in Unity coordinates (x right, y up, z forward) and converted on output
    (Unity x,y,z -> Blender -x,-z,y: the FBX importer mirrors X, and the mirror also flips winding), so the same FBX export settings as build_visitors.py apply.
  - Props that are placed in the world by script (pumpkin, bat, cobweb, porch parts) are centered on their
    own origin. Layered scenery (silhouettes, sky) is authored at final world positions around the camera at
    (0, 1.6, 0) and sits at the Environment root origin.
  - Silhouettes are flat cutouts that face the camera (rendered double-sided and unlit in Unity).
  - Material names are slot names; Unity remaps them to project materials (see EnvironmentBuilder.cs).
  - Budget: about 15k triangles for everything; the printed totals show where it stands.
"""
import argparse
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

CAMERA = Vector((0.0, 1.6, 0.0))

# Preview-only colors (in-game look is defined by the Unity materials).
PREVIEW_COLORS = {
    "EnvPumpkin": (0.8, 0.32, 0.05), "EnvStem": (0.12, 0.16, 0.05), "EnvGlow": (1.0, 0.7, 0.15),
    "EnvHalo": (1.0, 0.45, 0.08), "EnvBat": (0.02, 0.012, 0.03), "EnvCobweb": (0.6, 0.58, 0.68),
    "EnvPorch": (0.05, 0.03, 0.04), "EnvSilNear": (0.025, 0.015, 0.04), "EnvSilMid": (0.045, 0.025, 0.075),
    "EnvSilFar": (0.075, 0.04, 0.12), "EnvWindow": (0.95, 0.62, 0.2), "EnvSky": (0.2, 0.09, 0.26),
    "EnvMoon": (0.98, 0.93, 0.72),
}


def to_blender(p):
    return (-p[0], -p[2], p[1])


class Part:
    """One exported object. Faces are given in Unity winding (clockwise seen from the front)."""

    def __init__(self, name, origin=(0.0, 0.0, 0.0), weld=False, smooth=False):
        self.name = name
        self.origin = origin
        self.weld = weld
        self.smooth = smooth
        self.verts = []
        self.faces = []  # (indices, material name, uvs)
        self._index = {}

    def vert(self, p):
        p = tuple(p)
        if self.weld:
            key = tuple(round(c, 5) for c in p)
            if key in self._index:
                return self._index[key]
            self._index[key] = len(self.verts)
        self.verts.append(p)
        return len(self.verts) - 1

    def add(self, pts, mat, uvs=None, outward_from=None):
        """Adds a polygon. With outward_from, the winding is flipped if the face looks toward that point."""
        pts = [tuple(p) for p in pts]
        uvs = list(uvs) if uvs else None
        if outward_from is not None:
            a, b, c = (Vector(p) for p in pts[:3])
            normal = (b - a).cross(c - a)
            center = sum((Vector(p) for p in pts), Vector()) / len(pts)
            if normal.dot(center - Vector(outward_from)) < 0:
                pts.reverse()
                if uvs:
                    uvs.reverse()
        self.faces.append(([self.vert(p) for p in pts], mat, uvs))

    def tri_count(self):
        return sum(len(f[0]) - 2 for f in self.faces)


def make_object(part):
    mesh = bpy.data.meshes.new(part.name)
    bm = bmesh.new()
    origin = Vector(part.origin)
    bverts = [bm.verts.new(to_blender(Vector(p) - origin)) for p in part.verts]
    uv_layer = bm.loops.layers.uv.new("UVMap")
    mats = []
    for _, mat, _ in part.faces:
        if mat not in mats:
            mats.append(mat)
    for idx, mat, uvs in part.faces:
        try:
            face = bm.faces.new([bverts[i] for i in reversed(idx)])
        except ValueError:
            continue  # duplicate or degenerate face
        face.material_index = mats.index(mat)
        face.smooth = part.smooth
        if uvs:
            for loop, uv in zip(face.loops, reversed(uvs)):
                loop[uv_layer].uv = uv
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    for name in mats:
        mesh.materials.append(get_material(name))
    obj = bpy.data.objects.new(part.name, mesh)
    obj.location = to_blender(origin)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def get_material(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        c = PREVIEW_COLORS.get(name, (0.5, 0.5, 0.5))
        mat.diffuse_color = (c[0], c[1], c[2], 1.0)
    return mat


# ---------------------------------------------------------------- 2D helpers for flat cutouts

class Plane:
    """Maps (u, v) on a flat cutout standing at ground position (ox, oz) and facing the camera to world space."""

    def __init__(self, ox, oz, oy=0.0, toward=0.0):
        d = math.hypot(ox, oz) or 1.0
        self.r = (oz / d, -ox / d)
        self.ox = ox - ox / d * toward
        self.oz = oz - oz / d * toward
        self.oy = oy

    def p(self, u, v):
        return (self.ox + self.r[0] * u, self.oy + v, self.oz + self.r[1] * u)


def rotate2d(pts, angle):
    c, s = math.cos(angle), math.sin(angle)
    return [(x * c - y * s, x * s + y * c) for x, y in pts]


def cutout(part, mat, plane, polygon):
    part.add([plane.p(u, v) for u, v in polygon], mat)


def fan(part, mat, plane, center, boundary):
    for i in range(len(boundary) - 1):
        part.add([plane.p(*center), plane.p(*boundary[i]), plane.p(*boundary[i + 1])], mat)


# ---------------------------------------------------------------- jack-o'-lantern

PUMPKIN_A, PUMPKIN_B, PUMPKIN_CY = 0.5, 0.42, 0.42
PUMPKIN_RIBS, PUMPKIN_RIB_DEPTH = 12, 0.06


def pumpkin_front(x, y):
    """Surface z on the front (+Z) of the ellipsoid, a little outside so the face glows above the ribs."""
    k = 1.0 - (x / PUMPKIN_A) ** 2 - ((y - PUMPKIN_CY) / PUMPKIN_B) ** 2
    return PUMPKIN_A * math.sqrt(max(k, 0.0)) + 0.05


def build_pumpkin():
    inside = (0.0, PUMPKIN_CY, 0.0)
    body = Part("Body", weld=True, smooth=True)
    n = PUMPKIN_RIBS
    lats = [-60, -30, 0, 30, 60]
    rings = []
    for lat in lats:
        lr = math.radians(lat)
        ring = []
        for j in range(n):
            phi = 2 * math.pi * j / n
            rib = 1.0 + PUMPKIN_RIB_DEPTH * math.cos(n * phi)
            rr = PUMPKIN_A * math.cos(lr) * rib
            ring.append((rr * math.cos(phi), PUMPKIN_CY + PUMPKIN_B * math.sin(lr), rr * math.sin(phi)))
        rings.append(ring)
    bottom = (0.0, PUMPKIN_CY - PUMPKIN_B * 0.9, 0.0)
    top = (0.0, PUMPKIN_CY + PUMPKIN_B * 0.72, 0.0)  # dimple around the stem
    for j in range(n):
        k = (j + 1) % n
        body.add([bottom, rings[0][k], rings[0][j]], "EnvPumpkin", outward_from=inside)
        body.add([top, rings[-1][j], rings[-1][k]], "EnvPumpkin", outward_from=inside)
        for i in range(len(rings) - 1):
            body.add([rings[i][j], rings[i + 1][j], rings[i + 1][k], rings[i][k]], "EnvPumpkin", outward_from=inside)

    stem = Part("Stem", weld=True)
    sides = 5
    base_y = PUMPKIN_CY + PUMPKIN_B * 0.72 - 0.02
    low, high = [], []
    for j in range(sides):
        a = 2 * math.pi * j / sides
        low.append((0.07 * math.cos(a), base_y, 0.07 * math.sin(a)))
        high.append((0.04 + 0.05 * math.cos(a), base_y + 0.2, 0.05 * math.sin(a)))
    axis = (0.02, base_y + 0.1, 0.0)
    for j in range(sides):
        k = (j + 1) % sides
        stem.add([low[j], high[j], high[k], low[k]], "EnvStem", outward_from=axis)
    stem.add(high, "EnvStem", outward_from=(0.04, base_y, 0.0))

    face = Part("Face", weld=False)

    def on_surface(x, y):
        return (x, y, pumpkin_front(x, y))

    for sx in (-1, 1):
        eye = [(sx * 0.27, 0.46), (sx * 0.09, 0.46), (sx * 0.18, 0.64)]
        face.add([on_surface(*p) for p in eye], "EnvGlow", outward_from=inside)
    face.add([on_surface(*p) for p in [(-0.06, 0.34), (0.06, 0.34), (0.0, 0.43)]], "EnvGlow", outward_from=inside)
    xs = [-0.32 + 0.08 * k for k in range(9)]
    smile = lambda x: 0.05 * (x / 0.32) ** 2
    upper = [0.27 - (0.06 if k % 2 else 0.0) + smile(x) for k, x in enumerate(xs)]
    lower = [0.08 + (0.06 if k % 2 else 0.0) + smile(x) for k, x in enumerate(xs)]
    for k in range(8):
        quad = [(xs[k], upper[k]), (xs[k + 1], upper[k + 1]), (xs[k + 1], lower[k + 1]), (xs[k], lower[k])]
        face.add([on_surface(*p) for p in quad], "EnvGlow", outward_from=inside)

    # Halo: a tiny quad at the face center; the shader expands it to a camera-facing sprite from the UVs,
    # so every part of the pumpkin shares the object origin (and therefore the flicker phase).
    halo = Part("Halo")
    c = (0.0, 0.42, 0.4)
    e = 0.01
    halo.add([(c[0] - e, c[1] - e, c[2]), (c[0] - e, c[1] + e, c[2]), (c[0] + e, c[1] + e, c[2]), (c[0] + e, c[1] - e, c[2])],
             "EnvHalo", uvs=[(0, 0), (0, 1), (1, 1), (1, 0)])
    return [body, stem, face, halo]


# ---------------------------------------------------------------- bat

def build_bat():
    bat = Part("Bat")
    m = "EnvBat"
    bat.add([(0.0, 0, 0.2), (0.06, 0, 0.0), (0.0, 0, -0.15), (-0.06, 0, 0.0)], m)
    for sx in (-1, 1):
        bat.add([(sx * 0.02, 0, 0.17), (sx * 0.06, 0, 0.3), (sx * 0.07, 0, 0.16)], m)
        wing = [(0.05, 0.08), (0.4, 0.18), (0.75, 0.0), (0.55, -0.12), (0.32, -0.04), (0.18, -0.17), (0.05, -0.1)]
        for i in range(1, len(wing) - 1):
            tri = [wing[0], wing[i], wing[i + 1]]
            bat.add([(sx * u, 0.0, v) for u, v in tri], m)
    return [bat]


# ---------------------------------------------------------------- cobweb

def build_cobweb():
    web = Part("Web")
    steps = 6
    pts = [(math.cos(math.pi / 2 * k / steps), math.sin(math.pi / 2 * k / steps)) for k in range(steps + 1)]
    for k in range(steps):
        tri = [(0.0, 0.0), pts[k + 1], pts[k]]
        web.add([(x, y, 0.0) for x, y in tri], "EnvCobweb", uvs=tri)
    return [web]


# ---------------------------------------------------------------- porch frame

def box(part, mat, x0, x1, y0, y1, z0, z1):
    c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
    corners = {(i, j, k): (x1 if i else x0, y1 if j else y0, z1 if k else z0) for i in (0, 1) for j in (0, 1) for k in (0, 1)}
    quads = [[(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)], [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)],
             [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)], [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)],
             [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)], [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)]]
    for q in quads:
        part.add([corners[i] for i in q], mat, outward_from=c)


def build_porch():
    m = "EnvPorch"
    parts = []
    for name in ("PostL", "PostR"):
        p = Part(name)
        box(p, m, -0.5, 0.5, -0.5, 0.5, -0.04, 0.04)
        parts.append(p)
    header = Part("Header")
    box(header, m, -0.5, 0.5, 0.0, 1.0, -0.04, 0.04)
    parts.append(header)
    rng = random.Random(3)
    fascia = Part("Fascia")
    teeth = 11
    for k in range(teeth):
        x0, x1 = -0.5 + k / teeth, -0.5 + (k + 1) / teeth
        depth = -rng.uniform(0.55, 1.0)
        fascia.add([(x0, 0.0, 0.0), (x1, 0.0, 0.0), ((x0 + x1) / 2, depth, 0.0)], m)
    parts.append(fascia)
    for name in ("BracketL", "BracketR"):
        b = Part(name)
        arc = [(1.0 - math.sin(math.radians(a)), -1.0 + math.cos(math.radians(a))) for a in range(0, 91, 18)]
        for i in range(len(arc) - 1):
            b.add([(0.0, 0.0, 0.0), (arc[i][0], arc[i][1], 0.0), (arc[i + 1][0], arc[i + 1][1], 0.0)], m)
        parts.append(b)
    return parts


# ---------------------------------------------------------------- silhouettes

def tree(part, mat, ox, oz, height, rng, depth=4):
    plane = Plane(ox, oz, -0.15)

    def branch(p0, ang, length, w0, d):
        n = 4 if d == depth else (3 if d == depth - 1 else 2)
        pts, angs, a = [p0], [], ang
        for _ in range(n):
            a += rng.uniform(-0.32, 0.32)
            angs.append(a)
            pts.append((pts[-1][0] + math.sin(a) * length / n, pts[-1][1] + math.cos(a) * length / n))
        w1 = w0 * (0.55 if d > 0 else 0.2)
        widths = [w0 + (w1 - w0) * i / n for i in range(n + 1)]
        ring = []
        for i, p in enumerate(pts):
            ai = angs[0] if i == 0 else (angs[-1] if i == n else (angs[i - 1] + angs[i]) / 2)
            perp = (math.cos(ai), -math.sin(ai))
            hw = widths[i] / 2
            ring.append(((p[0] - perp[0] * hw, p[1] - perp[1] * hw), (p[0] + perp[0] * hw, p[1] + perp[1] * hw)))
        for i in range(n):
            cutout(part, mat, plane, [ring[i][0], ring[i][1], ring[i + 1][1], ring[i + 1][0]])
        if d > 0:
            side = rng.choice((-1, 1))
            for k in range(3 if d == depth else 2):
                idx = rng.randint(2, n) if d == depth else rng.randint(max(1, n - 1), n)
                child = angs[idx - 1] + side * rng.uniform(0.4, 0.95)
                side = -side
                child = max(-1.7, min(1.7, child))
                branch(pts[idx], child, length * rng.uniform(0.5, 0.72), widths[idx] * 0.72, d - 1)

    branch((0.0, -0.3), rng.uniform(-0.12, 0.12), height * 0.5, height * 0.085, depth)


def tombstone(part, mat, ox, oz, kind, size, lean):
    plane = Plane(ox, oz, -0.1)

    def add(poly):
        cutout(part, mat, plane, rotate2d([(x * size, y * size) for x, y in poly], lean))

    if kind == "arch":
        w, h0 = 0.28, 0.5
        add([(-w, 0.0), (w, 0.0), (w, h0), (-w, h0)])
        arc = [(w * math.cos(math.radians(a)), h0 + w * math.sin(math.radians(a))) for a in range(0, 181, 30)]
        for i in range(len(arc) - 1):
            add([(0.0, h0), arc[i], arc[i + 1]])
    elif kind == "slab":
        add([(-0.3, 0.0), (0.3, 0.0), (0.3, 0.45), (0.12, 0.62), (-0.3, 0.62)])
    elif kind == "cross":
        add([(-0.09, 0.0), (0.09, 0.0), (0.09, 1.0), (-0.09, 1.0)])
        add([(-0.32, 0.6), (0.32, 0.6), (0.32, 0.78), (-0.32, 0.78)])
    else:  # obelisk
        add([(-0.36, 0.0), (0.36, 0.0), (0.36, 0.14), (-0.36, 0.14)])
        add([(-0.24, 0.14), (0.24, 0.14), (0.14, 1.3), (-0.14, 1.3)])
        add([(-0.14, 1.3), (0.14, 1.3), (0.0, 1.62)])


def fence(part, mat, z, x0, x1, rng):
    step = 0.2
    x = x0
    while x < x1:
        if rng.random() > 0.07:
            plane = Plane(x, z, -0.1)
            h = 1.0 + rng.random() * 0.25
            w = 0.09
            poly = [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h), (0.0, h + 0.1), (-w / 2, h)]
            lean = rng.gauss(0.0, 0.05)
            poly = rotate2d(poly, lean)
            part.add([plane.p(u, v) for u, v in (poly[0], poly[1], poly[2])], mat)
            part.add([plane.p(u, v) for u, v in (poly[0], poly[2], poly[3])], mat)
            part.add([plane.p(u, v) for u, v in (poly[0], poly[3], poly[4])], mat)
        x += step
    xa = x0
    while xa < x1 - 1.0:
        xb = min(xa + rng.uniform(1.8, 3.2), x1)
        if rng.random() > 0.15:
            for v in (0.33, 0.78):
                dv = rng.uniform(-0.03, 0.03)
                pa, pb = Plane(xa, z, -0.05), Plane(xb, z, -0.05)
                part.add([pa.p(0, v - 0.035 + dv), pa.p(0, v + 0.035 + dv), pb.p(0, v + 0.035 - dv), pb.p(0, v - 0.035 - dv)], mat)
        xa = xb
    px = x0 + 0.5
    while px < x1:
        plane = Plane(px, z, -0.05)
        h = 1.4
        cutout(part, mat, plane, [(-0.08, 0.0), (0.08, 0.0), (0.08, h), (-0.08, h)])
        part.add([plane.p(-0.08, h), plane.p(0.08, h), plane.p(0.0, h + 0.18)], mat)
        px += 2.6 + rng.uniform(0, 0.6)


def hill(part, mat, ox, oz, width, height):
    plane = Plane(ox, oz, -0.4)
    top = [(math.cos(math.radians(a)) * width / 2, math.sin(math.radians(a)) * (height + 0.4)) for a in range(0, 181, 20)]
    fan(part, mat, plane, (0.0, 0.0), top)
    part.add([plane.p(-width / 2, 0.0), plane.p(0.0, 0.0), plane.p(width / 2, 0.0)], mat)


def house(far, windows, ox, oz, scale, rng):
    plane = Plane(ox, oz, -0.4)
    s = scale
    skew = lambda u, v: (u * s + v * s * 0.02, v * s)

    def poly(pts):
        cutout(far, "EnvSilFar", plane, [skew(u, v) for u, v in pts])

    poly([(-5, 0), (4, 0), (4, 5), (-5, 5)])                 # main body
    poly([(-5.7, 5), (4.7, 5), (-0.4, 9.2)])                 # roof
    poly([(3.0, 0), (5.2, 0), (5.2, 9), (3.0, 9)])           # tower
    poly([(2.7, 9), (5.5, 9), (4.1, 13.2)])                  # tower cone
    poly([(-3.7, 7), (-2.8, 7), (-2.8, 10.4), (-3.7, 10.4)])  # chimney
    poly([(-2.2, 5), (1.0, 5), (-0.6, 7.6)])                 # dormer
    wplane = Plane(ox, oz, -0.4, toward=0.4)
    for u0, v0, w, h in [(-4.2, 1.6, 0.9, 1.4), (-2.6, 1.6, 0.9, 1.4), (-1.0, 1.6, 0.9, 1.4), (0.8, 1.6, 0.9, 1.4),
                         (-4.0, 3.5, 0.8, 1.0), (1.8, 3.5, 0.8, 1.0), (3.75, 5.0, 0.7, 1.3), (-1.0, 5.4, 0.8, 1.0)]:
        if rng.random() < 0.8:
            cutout(windows, "EnvWindow", wplane, [skew(u0, v0), skew(u0 + w, v0), skew(u0 + w, v0 + h), skew(u0, v0 + h)])


def roofline(far, z, rng):
    plane_for = lambda x: Plane(x, z, -0.4)
    x = -48.0
    while x < 48.0:
        w = rng.uniform(3.0, 7.0)
        h0 = rng.uniform(2.0, 4.5)
        rise = rng.uniform(1.5, 3.5)
        pl = plane_for(x + w / 2)
        cutout(far, "EnvSilFar", pl, [(-w / 2, 0.0), (w / 2, 0.0), (w / 2, h0), (-w / 2, h0)])
        cutout(far, "EnvSilFar", pl, [(-w / 2 - 0.3, h0), (w / 2 + 0.3, h0), (0.0, h0 + rise)])
        if rng.random() < 0.15:
            cutout(far, "EnvSilFar", pl, [(-0.5, h0), (0.5, h0), (0.5, h0 + rise + 2.0), (-0.5, h0 + rise + 2.0)])
            far.add([pl.p(-0.5, h0 + rise + 2.0), pl.p(0.5, h0 + rise + 2.0), pl.p(0.0, h0 + rise + 5.5)], "EnvSilFar")
        x += w + rng.uniform(-0.5, 0.8)


def build_silhouettes():
    rng = random.Random(7)
    near = Part("Sil_Near")
    mid = Part("Sil_Mid")
    far = Part("Sil_Far")
    windows = Part("Windows")
    N, M, F = "EnvSilNear", "EnvSilMid", "EnvSilFar"

    fence(near, N, 13.6, -14.5, 14.5, rng)
    for x, z, h in [(-12.5, 16.5, 8.0), (-8.5, 18.5, 9.5), (4.5, 17.0, 7.0), (9.5, 16.0, 8.5), (14.0, 20.0, 9.5),
                    (-15.0, 21.0, 7.5), (7.5, 8.5, 7.0), (-9.8, 10.5, 8.0)]:
        tree(near, N, x, z, h, rng)
    stones = [(-13, 15.5), (-10, 17), (-8.3, 15), (-3.5, 16), (-1.5, 15.2), (0.8, 18.5), (4, 15.5), (6.5, 18), (11, 17.5), (13.5, 15.5),
              (5.2, 5.0), (6.2, 9.5), (8.8, 6.2), (-8.6, 6.8), (-10.5, 12.2), (-9.4, 8.0)]
    for x, z in stones:
        kind = rng.choice(["arch", "arch", "slab", "cross", "obelisk"])
        tombstone(near, N, x, z, kind, rng.uniform(0.85, 1.35), rng.gauss(0.0, 0.1))

    for x, z, h in [(-19, 27, 10.0), (-11, 29, 11.5), (-3, 26, 9.5), (6, 30, 12.0), (14, 27, 10.5), (22, 31, 12.0)]:
        tree(mid, M, x, z, h, rng)
    for x, z in [(-8, 24), (-1, 23), (7, 25), (12, 23.5)]:
        tombstone(mid, M, x, z, rng.choice(["arch", "cross", "obelisk"]), rng.uniform(1.3, 1.8), rng.gauss(0.0, 0.1))
    hill(mid, M, -10, 27.5, 24, 2.4)
    hill(mid, M, 14, 29, 28, 3.0)

    hill(far, F, -26, 44, 42, 5.5)
    hill(far, F, 6, 46, 52, 4.5)
    hill(far, F, 32, 43, 38, 6.5)
    for x, z, h in [(-26, 41, 15), (-17, 43, 13), (19, 42, 14), (28, 41, 16), (35, 44, 13)]:
        tree(far, F, x, z, h, rng, depth=3)
    house(far, windows, -3.5, 37.0, 1.0, rng)
    roofline(far, 47.0, rng)
    return [near, mid, far, windows]


# ---------------------------------------------------------------- sky

MOON_AZIMUTH, MOON_ELEVATION, MOON_DISTANCE = 18.0, 13.0, 48.0
MOON_HALF_SIZE = 15.0
SKY_RADIUS = 54.0


def build_sky():
    sky = Part("Backdrop")
    az_n = 20
    rows = [-4.0, 12.0, 34.0]
    ring = lambda y, az: (SKY_RADIUS * math.sin(math.radians(az)), y, SKY_RADIUS * math.cos(math.radians(az)))
    for i in range(az_n):
        a0 = -80 + 160 * i / az_n
        a1 = -80 + 160 * (i + 1) / az_n
        for r in range(len(rows) - 1):
            sky.add([ring(rows[r], a0), ring(rows[r + 1], a0), ring(rows[r + 1], a1), ring(rows[r], a1)], "EnvSky")

    az, el = math.radians(MOON_AZIMUTH), math.radians(MOON_ELEVATION)
    direction = Vector((math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az)))
    center = CAMERA + direction * MOON_DISTANCE
    right = Vector((0, 1, 0)).cross(direction).normalized()
    up = direction.cross(right).normalized()
    # Unity view: right = up x forward, up = forward x right.
    moon = Part("Moon")
    h = MOON_HALF_SIZE
    corners = [(-1, -1), (-1, 1), (1, 1), (1, -1)]
    moon.add([tuple(center + right * (cx * h) + up * (cy * h)) for cx, cy in corners], "EnvMoon", uvs=corners)
    return [sky, moon]


# ---------------------------------------------------------------- export / preview

GROUPS = {
    "Env_Pumpkin": build_pumpkin,
    "Env_Bat": build_bat,
    "Env_Cobweb": build_cobweb,
    "Env_Porch": build_porch,
    "Env_Silhouettes": build_silhouettes,
    "Env_Sky": build_sky,
}


def export_fbx(path, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", path_mode="STRIP",
        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True)


def outward_check(obj):
    """Fraction of faces whose Blender normal points away from the mesh center (sanity check for pumpkin/stem)."""
    mesh = obj.data
    center = sum((v.co for v in mesh.vertices), Vector()) / max(len(mesh.vertices), 1)
    good = sum(1 for p in mesh.polygons if p.normal.dot(p.center - center) > 0)
    return good / max(len(mesh.polygons), 1)


def render_preview(out_dir, objects):
    scene = bpy.context.scene
    for obj in objects:
        if any(m.name in ("EnvMoon", "EnvHalo", "EnvSky") for m in obj.data.materials):
            obj.hide_render = True
    az, el = math.radians(MOON_AZIMUTH), math.radians(MOON_ELEVATION)
    d = Vector((math.cos(el) * math.sin(az), math.sin(el), math.cos(el) * math.cos(az)))
    c = CAMERA + d * (MOON_DISTANCE - 1.0)
    bpy.ops.mesh.primitive_circle_add(vertices=24, radius=3.9, fill_type="NGON", location=to_blender(c),
                                      rotation=(math.radians(90 - MOON_ELEVATION), 0, math.radians(180 - MOON_AZIMUTH)))
    moon = bpy.context.active_object
    moon.data.materials.append(get_material("EnvMoon"))

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "FLAT"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.background_type = "WORLD"
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.12, 0.06, 0.18)
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.sensor_fit = "VERTICAL"
    cam_data.angle = math.radians(60)
    cam_data.clip_end = 60
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    cam.location = to_blender(CAMERA)
    cam.rotation_euler = (math.radians(90 - 8), 0, math.radians(180))
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.filepath = os.path.join(out_dir, "environment_view.png")
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview")
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()

    total = 0
    all_objects = []
    for name, builder in GROUPS.items():
        parts = builder()
        objects = [make_object(p) for p in parts]
        group_tris = sum(p.tri_count() for p in parts)
        for p, obj in zip(parts, objects):
            note = f"  outward {outward_check(obj):.2f}" if p.name in ("Body", "Stem") else ""
            print(f"[environment] {name}/{p.name}: {p.tri_count()} tris{note}")
        export_fbx(os.path.join(out, name + ".fbx"), objects)
        print(f"[environment] exported {name}.fbx ({group_tris} tris)")
        # Instanced props are counted at their placement count in EnvironmentBuilder; report model sizes here.
        total += group_tris
        all_objects += objects
    print(f"[environment] model total (each prop counted once): {total} tris")
    if args.preview:
        render_preview(os.path.abspath(args.preview), all_objects)


if __name__ == "__main__":
    main()
