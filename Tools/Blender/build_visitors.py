"""
Builds the low-poly visitor meshes (Koonmungchi / Omangi and costume props), their texture atlas,
and exports them to one FBX.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_visitors.py -- \
      --out Assets/Models/Characters/Visitors.fbx [--preview <dir>]

Conventions:
  - Every part is its own object with its origin at the world origin (between the feet),
    so in Unity each part sits at local zero under the visitor root and parts can be toggled freely.
    Exception: Koon_Tail is pivoted at its base so it can wag.
  - Characters face -Y in Blender, which becomes +Z (forward) in Unity.
  - Object name prefixes: Body (shared), Koon_ (Koonmungchi), Oma_ (Omangi), Fake_ (costume props).
  - Material names are slot names; Unity remaps them to project materials. "Fur" is swapped per character.
  - Flat markings (Omangi's face and cross, button X, lens X, tail stripes) live in one atlas texture
    (Textures/VisitorAtlas.png next to the FBX) instead of geometry. Untextured faces point at a white cell.
  - Budget: about 700 triangles or less per character without props.
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

CENTER = Vector((0.0, 0.0, 0.42))
RADIUS = 0.36
SQUASH = 0.92
# Horns are cones with a ~60 degree apex angle so they read clearly at low resolution.
HORN_BASE = 0.1 * math.tan(math.radians(30))
FRONT = Vector((0.0, -1.0, 0.0))
AXIS_X = Vector((1.0, 0.0, 0.0))
AXIS_Y = Vector((0.0, 1.0, 0.0))
AXIS_Z = Vector((0.0, 0.0, 1.0))

# Atlas layout in pixels (x, y, w, h), origin bottom-left.
ATLAS_W, ATLAS_H = 512, 256
CELL_FACE = (0, 0, 256, 256)        # Omangi face/belly markings, planar-projected from the front
CELL_BUTTON = (256, 128, 128, 128)  # blue button with X stitch
CELL_LENS = (384, 128, 128, 128)    # fake glasses lens with a drawn X
CELL_TAIL = (256, 0, 64, 128)       # raccoon tail stripes, base at the bottom
CELL_FAKE_TAIL = (320, 0, 64, 128)
CELL_WHITE = (384, 0, 128, 128)
FACE_X = (-0.4, 0.4)   # world X range mapped onto CELL_FACE
FACE_Z = (0.02, 0.82)  # world Z range mapped onto CELL_FACE

# Preview colors and whether the material samples the atlas. In-game look is defined by the Unity materials.
MATERIALS = {
    "Fur": ((0.42, 0.42, 0.44), False),
    "NoseTip": ((0.08, 0.08, 0.09), False),
    "Button": ((1.0, 1.0, 1.0), True),
    "TailStripe": ((1.0, 1.0, 1.0), True),
    "Horn": ((0.06, 0.05, 0.05), False),
    "Ear": ((0.07, 0.06, 0.06), False),
    "Piercing": ((0.78, 0.78, 0.80), False),
    "PropBand": ((0.05, 0.05, 0.05), False),
    "PropHorn": ((0.10, 0.02, 0.02), False),
    "PropFelt": ((0.12, 0.11, 0.11), False),
    "PropPaper": ((0.55, 0.55, 0.52), False),
    "PropString": ((0.92, 0.92, 0.92), False),
    "PropStrap": ((0.35, 0.20, 0.10), False),
    "PropFrame": ((0.08, 0.08, 0.08), False),
    "PropLens": ((1.0, 1.0, 1.0), True),
    "FakeTailStripe": ((1.0, 1.0, 1.0), True),
    "PropBucket_Red": ((0.75, 0.08, 0.06), False),
    "PropTin": ((0.62, 0.62, 0.64), False),
    "CostumeHat": ((0.14, 0.07, 0.20), False),
    "CostumeOrange": ((0.95, 0.42, 0.04), False),
    "CostumeDark": ((0.04, 0.03, 0.03), False),
    "CostumeBandage": ((0.86, 0.82, 0.70), False),
    "CostumeWing": ((0.07, 0.05, 0.09), False),
    "CostumeBow": ((0.50, 0.12, 0.62), False),
    "CostumeWhite": ((0.95, 0.95, 0.93), False),
    "CostumeBunnyInner": ((0.60, 0.58, 0.60), False),
    "CostumeGold": ((0.95, 0.75, 0.15), False),
    "CostumeLensAmber": ((0.25, 0.15, 0.03), False),
    "CostumeAlienLens": ((0.12, 0.14, 0.13), False),
    "CostumeLime": ((0.45, 0.90, 0.12), False),
    "Sheet": ((0.90, 0.90, 0.88), False),
}

BVH = None
ATLAS = None
ATLAS_PATH = None


def cell_uv(cell, s, t):
    return ((cell[0] + s * cell[2]) / ATLAS_W, (cell[1] + t * cell[3]) / ATLAS_H)


WHITE_UV = cell_uv(CELL_WHITE, 0.5, 0.5)


def face_px(x, z):
    """World X/Z (front planar projection) to atlas pixels inside CELL_FACE."""
    s = (x - FACE_X[0]) / (FACE_X[1] - FACE_X[0])
    t = (z - FACE_Z[0]) / (FACE_Z[1] - FACE_Z[0])
    return CELL_FACE[0] + s * CELL_FACE[2], CELL_FACE[1] + t * CELL_FACE[3]


def make_material(name, color, textured):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.6
    if textured:
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = ATLAS
        tex.interpolation = "Closest"
        mix = nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs[0].default_value = 1.0
        mix.inputs[6].default_value = (*color, 1.0)
        links.new(tex.outputs["Color"], mix.inputs[7])
        links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def get_mat(name):
    return bpy.data.materials.get(name) or make_material(name, *MATERIALS[name])


class Part:
    """One exported object, built from several pieces in a single bmesh."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.mats = []

    def mat_index(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def assign(self, verts, mat, smooth=True):
        index = self.mat_index(mat)
        faces = {f for v in verts for f in v.link_faces}
        for face in faces:
            face.material_index = index
            face.smooth = smooth
            for loop in face.loops:
                loop[self.uv].uv = WHITE_UV
        return faces

    def finish(self):
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for mat in self.mats:
            mesh.materials.append(get_mat(mat))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ---------------------------------------------------------------- helpers

def surf(direction):
    """Hit point and (softened) normal on the body surface in the given direction from the center."""
    d = Vector(direction).normalized()
    loc, normal, _, _ = BVH.ray_cast(CENTER + d * 1.0, -d)
    return loc, normal.lerp(d, 0.5).normalized()


def project(point, normal, lift):
    loc, n, _, _ = BVH.ray_cast(point + normal * 0.3, -normal)
    return loc + n * lift


def basis(z_dir, x_hint):
    """Rotation matrix whose local Z is z_dir and local X is as close to x_hint as possible."""
    z = z_dir.normalized()
    x = (x_hint - z * x_hint.dot(z)).normalized()
    y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_4x4()


def trs(loc, rot, scale):
    return Matrix.Translation(loc) @ rot @ Matrix.Diagonal((*scale, 1.0))


def track(direction):
    return direction.normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()


def orient(face, outward):
    face.normal_update()
    if face.normal.dot(outward) < 0:
        face.normal_flip()


def add_sphere(part, mat, matrix, u=6, v=4):
    res = bmesh.ops.create_uvsphere(part.bm, u_segments=u, v_segments=v, radius=1.0, matrix=matrix)
    return part.assign(res["verts"], mat)


def add_cone(part, mat, matrix, r1, r2, depth, segments=6, smooth=True):
    res = bmesh.ops.create_cone(part.bm, cap_ends=True, cap_tris=True, segments=segments,
                                radius1=r1, radius2=r2, depth=depth, matrix=matrix)
    part.assign(res["verts"], mat, smooth)


def add_tube(part, mat, points, radii, ref, segments=6, cap_start=True, cap_end=True, uv_cell=None):
    """Tube along points. With uv_cell, V runs from 0 at the first point to 1 at the last (for stripes)."""
    bm = part.bm
    count = len(points)
    index = part.mat_index(mat)
    tangents = [(points[min(i + 1, count - 1)] - points[max(i - 1, 0)]).normalized() for i in range(count)]
    rings = []
    for i, p in enumerate(points):
        b = tangents[i].cross(ref).normalized()
        n = b.cross(tangents[i])
        r = max(radii[i], 0.002)
        rings.append([bm.verts.new(p + r * (math.cos(a) * b + math.sin(a) * n))
                      for a in (2 * math.pi * k / segments for k in range(segments))])

    def uv_at(i):
        # Kept off the cell edges so filtering never picks up the neighbouring atlas cell.
        return cell_uv(uv_cell, 0.5, 0.03 + 0.94 * i / (count - 1)) if uv_cell else WHITE_UV

    def finish_face(face, outward, ring_of):
        orient(face, outward)
        face.material_index = index
        face.smooth = True
        for loop in face.loops:
            loop[part.uv].uv = uv_at(ring_of[loop.vert])

    for i in range(count - 1):
        ring_of = {v: i for v in rings[i]} | {v: i + 1 for v in rings[i + 1]}
        for k in range(segments):
            k2 = (k + 1) % segments
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            finish_face(f, f.calc_center_median() - (points[i] + points[i + 1]) * 0.5, ring_of)
    caps = ([(0, -tangents[0])] if cap_start else []) + ([(count - 1, tangents[-1])] if cap_end else [])
    for i, outward in caps:
        center = bm.verts.new(points[i])
        ring_of = {v: i for v in rings[i]} | {center: i}
        for k in range(segments):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % segments], center))
            finish_face(f, outward, ring_of)


def add_disc(part, mat, center, normal, radius, depth, cell, segments=10):
    """Flat round disc (front face and rim, no back) with the front mapped onto an atlas cell."""
    bm = part.bm
    index = part.mat_index(mat)
    rot = track(normal).to_3x3()
    ax, ay = rot.col[0], rot.col[1]
    front, back = center + normal * depth * 0.5, center - normal * depth * 0.5
    angles = [2 * math.pi * k / segments for k in range(segments)]
    rim_f = [bm.verts.new(front + radius * (math.cos(a) * ax + math.sin(a) * ay)) for a in angles]
    rim_b = [bm.verts.new(back + radius * (math.cos(a) * ax + math.sin(a) * ay)) for a in angles]
    mid = bm.verts.new(front)
    uv_front = {mid: cell_uv(cell, 0.5, 0.5)}
    uv_front.update({v: cell_uv(cell, 0.5 + 0.48 * math.cos(a), 0.5 + 0.48 * math.sin(a)) for v, a in zip(rim_f, angles)})
    for k in range(segments):
        k2 = (k + 1) % segments
        f = bm.faces.new((rim_f[k], rim_f[k2], mid))
        orient(f, normal)
        for loop in f.loops:
            loop[part.uv].uv = uv_front[loop.vert]
        side = bm.faces.new((rim_b[k], rim_b[k2], rim_f[k2], rim_f[k]))
        orient(side, (rim_f[k].co + rim_f[k2].co) * 0.5 - front)
        for loop in side.loops:
            loop[part.uv].uv = cell_uv(cell, 0.5, 0.03)
        for face in (f, side):
            face.material_index = index
            face.smooth = False


def koon_tail_path(steps):
    """Koonmungchi's tail: base anchor, curve parameters and points (shared with the fake tail)."""
    anchor, n = surf((0.0, 1.0, -0.35))
    start = anchor - n * 0.05
    ts = [i / (steps - 1) for i in range(steps)]
    return anchor, ts, [start + Vector((0.15 * t * t, 0.28 * t, 0.55 * t * t)) for t in ts]


def koon_tail_radius(t):
    """Thick in the middle, rounded off at the tip (shared with the fake tail)."""
    r = 0.075 + 0.05 * math.sin(math.pi * t * 0.75)
    if t > 0.8:
        r *= math.sqrt(max(1 - ((t - 0.8) / 0.2) ** 2, 0.02))
    return r


def set_origin(obj, point):
    """Moves the object's pivot to point without moving its geometry."""
    obj.data.transform(Matrix.Translation(-point))
    obj.location = point


def limb(part, start_dir, direction, length, radius, sink=0.03):
    start, n = surf(start_dir)
    d = Vector(direction).normalized()
    center = start - n * sink + d * (length * 0.5)
    rot = AXIS_Z.rotation_difference(d).to_matrix().to_4x4()
    add_sphere(part, "Fur", trs(center, rot, (radius, radius, length * 0.5)))


def band_path(y_bias, lift, angles):
    """Points following the head surface over the top, side to side (headbands)."""
    return [band_point(y_bias, lift, a)[0] for a in angles]


def band_point(y_bias, lift, angle):
    loc, n = surf((math.sin(angle), y_bias, math.cos(angle)))
    return loc + n * lift, n


# ---------------------------------------------------------------- atlas

def build_atlas():
    """Paints the marking atlas procedurally. Needs the body BVH for Omangi's face positions."""
    global ATLAS
    img = np.ones((ATLAS_H, ATLAS_W, 4), dtype=np.float32)
    ys, xs = np.mgrid[0:ATLAS_H, 0:ATLAS_W] + 0.5

    def paint(mask, color):
        img[mask] = (*color, 1.0)

    def in_cell(cell):
        x, y, w, h = cell
        return (xs >= x) & (xs < x + w) & (ys >= y) & (ys < y + h)

    def ellipse(cx, cy, a, b, angle):
        c, s = math.cos(angle), math.sin(angle)
        u = (xs - cx) * c + (ys - cy) * s
        v = -(xs - cx) * s + (ys - cy) * c
        return (u / a) ** 2 + (v / b) ** 2 <= 1.0

    def segment(p0, p1, width):
        (x0, y0), (x1, y1) = p0, p1
        dx, dy = x1 - x0, y1 - y0
        t = np.clip(((xs - x0) * dx + (ys - y0) * dy) / (dx * dx + dy * dy), 0.0, 1.0)
        return (xs - x0 - t * dx) ** 2 + (ys - y0 - t * dy) ** 2 <= (width * 0.5) ** 2

    def x_mark(cell, reach, width):
        x, y, w, h = cell
        cx, cy, r = x + w / 2, y + h / 2, w / 2 * reach
        return (segment((cx - r, cy - r), (cx + r, cy + r), width)
                | segment((cx - r, cy + r), (cx + r, cy - r), width))

    px_per_m = CELL_FACE[2] / (FACE_X[1] - FACE_X[0])
    black = (0.02, 0.02, 0.02)

    # Omangi: slightly wide black oval eyes tilted into a V (inner ends lower).
    for sx in (-1, 1):
        loc, _ = surf((sx * 0.34, -1.0, 0.4))
        paint(ellipse(*face_px(loc.x, loc.z), 0.052 * px_per_m, 0.034 * px_per_m, math.radians(sx * 18)), black)
    # Puckered "3"-on-its-side (w) mouth.
    loc, _ = surf((0.0, -1.0, 0.02))
    half, height = 0.04, 0.022
    pts = [face_px(loc.x + u, loc.z + 0.5 * height * math.cos(2 * math.pi * u / half))
           for u in np.linspace(-half, half, 17)]
    paint(np.logical_or.reduce([segment(a, b, 0.012 * px_per_m) for a, b in zip(pts, pts[1:])]), black)
    # Thin inverted cross centered on the navel.
    loc, _ = surf((0.0, -1.0, -0.75))
    for u0, v0, u1, v1 in ((-0.009, -0.07, 0.009, 0.06), (-0.04, -0.035, 0.04, -0.017)):
        (x0, y0), (x1, y1) = face_px(loc.x + u0, loc.z + v0), face_px(loc.x + u1, loc.z + v1)
        paint((xs >= x0) & (xs < x1) & (ys >= y0) & (ys < y1), black)

    # Koonmungchi's blue button: darker rim and an X stitch.
    x, y, w, h = CELL_BUTTON
    cell = in_cell(CELL_BUTTON)
    paint(cell, (0.10, 0.25, 0.65))
    paint(cell & ellipse(x + w / 2, y + h / 2, w * 0.42, h * 0.42, 0.0), (0.15, 0.35, 0.85))
    paint(cell & x_mark(CELL_BUTTON, 0.52, 13), (0.05, 0.07, 0.18))

    # Fake glasses lens: pale blue with a marker-drawn X.
    paint(in_cell(CELL_LENS), (0.60, 0.75, 0.95))
    paint(in_cell(CELL_LENS) & x_mark(CELL_LENS, 0.6, 10), (0.05, 0.07, 0.18))

    # Tail stripes along V. Real tail: 7 bands, tip always dark. Fake tail: 5 bands, beige-tinted, tip dark too.
    t = (ys - CELL_TAIL[1]) / CELL_TAIL[3]
    dark = (np.floor((1 - t) * 7).astype(int) % 2) == 0
    paint(in_cell(CELL_TAIL) & dark, (0.22, 0.22, 0.24))
    paint(in_cell(CELL_TAIL) & ~dark, (0.68, 0.68, 0.70))
    dark = (np.floor((1 - t) * 5).astype(int) % 2) == 0
    paint(in_cell(CELL_FAKE_TAIL) & dark, (0.25, 0.22, 0.20))
    paint(in_cell(CELL_FAKE_TAIL) & ~dark, (0.75, 0.72, 0.66))

    ATLAS = bpy.data.images.new("VisitorAtlas", ATLAS_W, ATLAS_H, alpha=False)
    ATLAS.pixels.foreach_set(img.ravel())
    os.makedirs(os.path.dirname(ATLAS_PATH), exist_ok=True)
    ATLAS.filepath_raw = ATLAS_PATH
    ATLAS.file_format = "PNG"
    ATLAS.save()


# ---------------------------------------------------------------- parts

def build_body():
    global BVH
    part = Part("Body")
    # UV sphere with its poles pointing front/back: the front pole fans out flat over the face
    # (markings go in the texture), and the 16-segment ring becomes the front silhouette.
    poles_front = Matrix.Rotation(math.radians(90), 4, "X")
    res = bmesh.ops.create_uvsphere(part.bm, u_segments=16, v_segments=8, radius=RADIUS,
                                    matrix=Matrix.Translation(CENTER) @ poles_front)
    body_verts = res["verts"]
    # A few deliberate fur clumps instead of noise, so the silhouette reads as a cloud.
    clumps = [(a, amp) for a, amp in ((15, 0.04), (60, 0.03), (105, 0.045), (150, 0.03), (200, 0.04),
                                      (245, 0.03), (290, 0.045), (335, 0.03))]
    for v in body_verts:
        n = (v.co - CENTER).normalized()
        bump = 0.0
        for angle, amp in clumps:
            d = Vector((math.sin(math.radians(angle)), 0.25, math.cos(math.radians(angle)))).normalized()
            bump += amp * math.exp(-(1.0 - n.dot(d)) / 0.08)
        co = CENTER + n * (RADIUS + bump)
        co.z = CENTER.z + (co.z - CENTER.z) * SQUASH
        v.co = co
    body_faces = part.assign(body_verts, "Fur")
    # Front-facing faces carry Omangi's markings via a planar projection; Koonmungchi's fur ignores the texture.
    for f in body_faces:
        f.normal_update()
        if f.normal.y < -0.15:
            for loop in f.loops:
                s = (loop.vert.co.x - FACE_X[0]) / (FACE_X[1] - FACE_X[0])
                t = (loop.vert.co.z - FACE_Z[0]) / (FACE_Z[1] - FACE_Z[0])
                loop[part.uv].uv = cell_uv(CELL_FACE, s, t)
    BVH = BVHTree.FromBMesh(part.bm)
    build_atlas()

    for sx in (-1, 1):
        limb(part, (sx, -0.3, -0.1), (sx * 0.75, -0.45, -0.45), 0.17, 0.06)  # arms
        limb(part, (sx * 0.4, -0.1, -1.0), (0.0, -0.15, -1.0), 0.17, 0.075, sink=0.05)  # legs
    body_count = len(body_verts)
    obj = part.finish()

    # Shade the low-poly ball like a smooth ellipsoid (mostly), so facets do not show in the lighting.
    normals = []
    for i, v in enumerate(obj.data.vertices):
        if i < body_count:
            d = v.co - CENTER
            smooth = Vector((d.x, d.y, d.z / (SQUASH * SQUASH))).normalized()
            normals.append(smooth.lerp(v.normal, 0.3).normalized())
        else:
            normals.append(v.normal.copy())
    obj.data.normals_split_custom_set_from_vertices(normals)


def build_koon():
    # Short raccoon muzzle: wide base blending into the face, tapering quickly to a small dark nose,
    # pointing forward with a slight upturn (a long, even, droopy tube reads wrong).
    part = Part("Koon_Nose")
    base, n = surf((0.0, -1.0, -0.1))
    d = Vector((0.0, -1.0, -0.1)).normalized()
    start = base - d * 0.05
    length = 0.17
    ts = [i / 4 for i in range(5)]
    pts = [start + d * (length * t) + Vector((0, 0, 0.012)) * t * t for t in ts]
    add_tube(part, "Fur", pts, [0.035 + 0.08 * (1 - t) ** 1.5 for t in ts], AXIS_X, cap_start=False)
    add_sphere(part, "NoseTip", trs(pts[-1] + d * 0.008, Matrix(), (0.042, 0.034, 0.034)))
    part.finish()

    # Round blue button eyes; the X stitch is in the texture. The X is a permanent trait, not an expression.
    part = Part("Koon_Eyes")
    for sx in (-1, 1):
        loc, n = surf((sx * 0.36, -1.0, 0.42))
        add_disc(part, "Button", loc + n * 0.006, n, 0.058, 0.02, CELL_BUTTON)
    part.finish()

    # Thick striped raccoon tail that curls up and peeks over one shoulder.
    part = Part("Koon_Tail")
    anchor, ts, pts = koon_tail_path(8)
    add_tube(part, "TailStripe", pts, [koon_tail_radius(t) for t in ts], AXIS_X, segments=7,
             cap_start=False, uv_cell=CELL_TAIL)
    # Pivot at the tail base so it can wag in Unity (TailWag); the only part not pivoted at the feet.
    set_origin(part.finish(), anchor)


def build_oma():
    # Eyes, mouth and the inverted cross are painted in the atlas (see build_atlas).

    # Floppy goat ears on both sides of the head.
    part = Part("Oma_Ears")
    for sx in (-1, 1):
        anchor, n = surf((sx, -0.15, 0.55))
        d = Vector((sx * 0.85, -0.2, -0.5)).normalized()
        rot = basis(d, FRONT)  # local Z = length, local X = thickness (faces forward)
        add_sphere(part, "Ear", trs(anchor + d * 0.1, rot, (0.022, 0.05, 0.13)), u=6, v=4)
    part.finish()

    # Silver piercing near the base of the left ear (+X, her left): one hoop through the ear's lower edge.
    part = Part("Oma_Piercing")
    anchor, n = surf((1.0, -0.15, 0.55))
    d = Vector((0.85, -0.2, -0.5)).normalized()
    face = basis(d, FRONT).col[0].to_3d()  # the ear's flat side, facing forward
    if face.dot(FRONT) < 0:
        face = -face
    down = d.cross(face).normalized()  # across the ear, toward its lower edge
    if down.z > 0:
        down = -down
    # The ear is an ellipsoid (half width 0.05, half length 0.13) centred 0.1 out; the hoop sits near the
    # tip (0.07 past the centre), hanging from the lower edge, in the plane across the ear so it goes through it.
    edge = anchor + d * 0.17 + down * 0.05 * math.sqrt(1 - (0.07 / 0.13) ** 2)
    hoop = [edge - down * 0.004 + 0.026 * (math.cos(t) * down + math.sin(t) * face)
            for t in (2 * math.pi * k / 12 for k in range(13))]
    add_tube(part, "Piercing", hoop, [0.0045] * len(hoop), d, segments=5, cap_start=False, cap_end=False)
    part.finish()

    # Two horns on top, curving slightly back and out.
    part = Part("Oma_Horns")
    for sx in (-1, 1):
        anchor, n = surf((sx * 0.3, -0.1, 1.0))
        start = anchor - n * 0.02
        ts = [i / 3 for i in range(4)]
        pts = [start + n * 0.1 * t + Vector((sx * 0.015, 0.035, 0.0)) * t * t for t in ts]
        add_tube(part, "Horn", pts, [HORN_BASE * (1 - t) + 0.003 for t in ts], AXIS_X, segments=6, cap_start=False)
    part.finish()

    # Short goat tail, about as stubby as a rabbit's.
    part = Part("Oma_Tail")
    anchor, n = surf((0.0, 1.0, -0.3))
    rot = Matrix.Rotation(math.radians(-30), 4, "X")
    add_sphere(part, "Fur", trs(anchor + n * 0.025, rot, (0.05, 0.045, 0.062)))
    part.finish()


def build_fakes():
    """Costume props. They sit in the same places as the real parts but read as worn props."""
    top = [math.radians(a) for a in range(-95, 96, 16)]

    # Plastic horn headband: visible band over the head, straight glossy cones on collars.
    part = Part("Fake_HornBand")
    y_bias, lift = -0.12, 0.012
    add_tube(part, "PropBand", band_path(y_bias, lift, top), [0.011] * len(top), AXIS_Y, segments=4)
    for sx in (-1, 1):
        p, n = band_point(y_bias, lift, math.radians(sx * 28))
        rot = track(n)
        add_cone(part, "PropBand", trs(p + n * 0.008, rot, (1, 1, 1)), 0.068, 0.068, 0.014, smooth=False)
        add_cone(part, "PropHorn", trs(p + n * 0.065, rot, (1, 1, 1)), 0.11 * math.tan(math.radians(30)), 0.002, 0.11)
    part.finish()

    # Felt goat ears on a second headband, sitting a little further back and sticking out stiffly.
    part = Part("Fake_GoatEars")
    y_bias, lift = 0.1, 0.012
    add_tube(part, "PropBand", band_path(y_bias, lift, top), [0.011] * len(top), AXIS_Y, segments=4)
    for sx in (-1, 1):
        p, n = band_point(y_bias, lift, math.radians(sx * 78))
        d = Vector((sx * 0.9, 0.0, -0.25)).normalized()
        thickness = FRONT - d * FRONT.dot(d)
        rot = basis(thickness, d.cross(thickness))  # local Z = thickness, local Y = length along d
        add_cone(part, "PropFelt", trs(p + d * 0.1, rot, (0.05, 0.12, 1.0)), 1.0, 1.0, 0.01, segments=8, smooth=False)
    part.finish()

    # Paper party-cone nose held on with an elastic string around the head.
    part = Part("Fake_ConeNose")
    anchor, n = surf((0.0, -1.0, -0.08))
    d = Vector((0.0, -1.0, -0.05)).normalized()
    rot = AXIS_Z.rotation_difference(d).to_matrix().to_4x4()
    add_cone(part, "PropPaper", trs(anchor + d * 0.08, rot, (1, 1, 1)), 0.065, 0.004, 0.2, segments=8)
    string = []
    for a in range(25, 336, 20):
        loc, sn = surf((math.sin(math.radians(a)), -math.cos(math.radians(a)), -0.15))
        string.append(loc + sn * 0.008)
    add_tube(part, "PropString", string, [0.0035] * len(string), AXIS_Z, segments=4, cap_start=False, cap_end=False)
    part.finish()

    # Stiff striped fake tail strapped around the waist. Same thickness profile and rounded tip as the real
    # one, and its tip sits where the real tail's tip peeks over the shoulder, so from the front the two look
    # alike; the tells are the strap, the straight stiff line, the beige tint and that it does not wag.
    part = Part("Fake_RaccoonTail")
    strap = []
    for a in range(0, 361, 20):
        loc, sn = surf((math.sin(math.radians(a)), -math.cos(math.radians(a)), -0.85))
        strap.append(loc + sn * 0.012)
    add_tube(part, "PropStrap", strap, [0.014] * len(strap), AXIS_Z, segments=4, cap_start=False, cap_end=False)
    back, n = surf((0.0, 1.0, -0.85))
    base = back + n * 0.02
    tip = koon_tail_path(2)[2][-1]
    ts = [i / 7 for i in range(8)]
    pts = [base.lerp(tip, t) + Vector((0.0, 0.07, 0.0)) * math.sin(math.pi * t) for t in ts]
    add_tube(part, "FakeTailStripe", pts, [koon_tail_radius(t) for t in ts], AXIS_X, segments=7,
             cap_start=False, uv_cell=CELL_FAKE_TAIL)
    part.finish()

    # Glasses with an X drawn on each lens, imitating Koonmungchi's stitched button eyes.
    part = Part("Fake_XGlasses")
    rim_r, lens_r = 0.066, 0.062
    centers = eyewear_centers(rim_r)
    for c in centers:
        rim = [c + rim_r * (math.cos(a) * AXIS_X + math.sin(a) * AXIS_Z)
               for a in (2 * math.pi * k / 12 for k in range(13))]
        add_tube(part, "PropFrame", rim, [0.006] * len(rim), FRONT, segments=4, cap_start=False, cap_end=False)
        add_disc(part, "PropLens", c, FRONT, lens_r, 0.004, CELL_LENS, segments=12)
    left, right = centers[0] + AXIS_X * rim_r, centers[1] - AXIS_X * rim_r
    bridge = [left.lerp(right, t) + AXIS_Z * 0.012 * math.sin(math.pi * t) for t in (k / 4 for k in range(5))]
    add_tube(part, "PropFrame", bridge, [0.005] * len(bridge), FRONT, segments=4)
    add_temples(part, "PropFrame", centers, rim_r)
    part.finish()

    build_paint_bucket()


EYE_Y = 0.42  # height of both visitors' eyes


def eyewear_centers(reach):
    """Lens centers for glasses: one flat plane in front of the face, clear of Koonmungchi's button eyes
    (they stick out ~0.016) so either visitor can wear them; reach is the lens extent around each eye."""
    centers = [surf((sx * 0.36, -1.0, EYE_Y))[0] for sx in (-1, 1)]
    face_y = min(project(c + AXIS_X * r * math.cos(a) + AXIS_Z * r * math.sin(a), FRONT, 0.0).y
                 for c in centers for r in (0.0, reach) for a in (k * math.pi / 4 for k in range(8)))
    return [Vector((c.x, face_y - 0.026, c.z)) for c in centers]


def add_temples(part, mat, centers, reach):
    """Glasses arms from the outer edge of each lens back along the head."""
    for sx, c in zip((-1, 1), centers):
        temple = [c + AXIS_X * sx * reach]
        for a in range(40, 101, 15):
            loc, sn = surf((sx * math.sin(math.radians(a)), -math.cos(math.radians(a)), EYE_Y))
            temple.append(loc + sn * 0.012)
        add_tube(part, mat, temple, [0.005] * len(temple), AXIS_Z, segments=4)


def build_paint_bucket():
    """Paint tin tipped upside down onto one side of the head, as if the paint was poured over it (the fur
    paint itself is the fur change, VisitorDisguise turns it on with the bucket). The tin is coated in that
    paint (Unity swaps PropBucket_Red per visitor); the tells are the bare tin bottom, the rolled rim, the
    ridge, a dent and the wire handle hanging loose. It covers part of the head on the visitor's right
    (+X) and leaves the inner side free, so a real horn tip still peeks out next to it."""
    part = Part("Fake_PaintBucket")
    seat, n = surf((0.7, -0.1, 0.72))
    axis = n  # square to the head, so both sides of the rim rest on it
    frame = basis(axis, FRONT).to_3x3()
    ax, ay = frame.col[0], frame.col[1]  # ax faces front
    segments = 10
    opening_r, bottom_r, height = 0.155, 0.135, 0.19
    origin = seat - axis * 0.012  # the rim sits just in the fur

    def ring_point(h, r, a):
        return origin + axis * h + r * (math.cos(a) * ax + math.sin(a) * ay)

    angles = [2 * math.pi * k / segments for k in range(segments)]
    heights = (0.0, height * 0.5, height)
    bm = part.bm
    rings = []
    for h in heights:
        r = opening_r + (bottom_r - opening_r) * h / height
        ring = [bm.verts.new(ring_point(h, r, a)) for a in angles]
        rings.append(ring)
    # Dent on the front-right of the side wall.
    for k, depth in ((1, 0.022), (0, 0.008), (2, 0.008)):
        v = rings[1][k]
        v.co -= (v.co - (origin + axis * heights[1])).normalized() * depth

    paint, tin = part.mat_index("PropBucket_Red"), part.mat_index("PropTin")
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(segments):
            k2 = (k + 1) % segments
            f = bm.faces.new((a[k], a[k2], b[k2], b[k]))
            faces.append((f, paint, f.calc_center_median() - origin - axis * height * 0.5))
    for ring, h, mat, outward in ((rings[0], 0.0, paint, -axis), (rings[-1], height, tin, axis)):
        center = bm.verts.new(origin + axis * h)
        for k in range(segments):
            faces.append((bm.faces.new((ring[k], ring[(k + 1) % segments], center)), mat, outward))
    for f, mat, outward in faces:
        orient(f, outward)
        f.material_index = mat
        f.smooth = False
        for loop in f.loops:
            loop[part.uv].uv = WHITE_UV

    # Rolled rim at the opening and a pressed ridge on the side, bare tin.
    for h, r, thick in ((0.006, opening_r + 0.004, 0.009), (height * 0.72, None, 0.005)):
        r = r or opening_r + (bottom_r - opening_r) * h / height + 0.002
        loop = [ring_point(h, r, 2 * math.pi * k / 12) for k in range(13)]
        add_tube(part, "PropTin", loop, [thick] * len(loop), axis, segments=4, cap_start=False, cap_end=False)

    # Wire handle: lugs on both sides near the rim, the bail hanging loose away from the head.
    lugs = [ring_point(0.03, opening_r + 0.004, a) for a in (math.pi * 0.5, -math.pi * 0.5)]
    for p in lugs:
        add_sphere(part, "PropTin", trs(p, track(ay), (0.014, 0.014, 0.008)), u=6, v=3)
    mid = (lugs[0] + lugs[1]) * 0.5
    span = (lugs[0] - lugs[1]) * 0.5
    outward = Vector((axis.x, axis.y, 0.0)).normalized()
    hang = FRONT * 0.7 + outward * 0.5 - AXIS_Z * 0.2  # swung forward over the brow
    hang = (hang - span.normalized() * hang.dot(span.normalized())).normalized()
    bail = [mid + span * math.cos(t) + hang * (opening_r * 0.9) * math.sin(t)
            for t in (math.pi * k / 8 for k in range(9))]
    add_tube(part, "PropTin", bail, [0.0035] * len(bail), ax, segments=4, cap_start=False, cap_end=False)
    part.finish()


def add_ribbon(part, mat, points, normals, width):
    """Flat strip along points, facing normals (bandages)."""
    bm = part.bm
    index = part.mat_index(mat)
    count = len(points)
    pairs = []
    for i, (p, n) in enumerate(zip(points, normals)):
        t = (points[min(i + 1, count - 1)] - points[max(i - 1, 0)]).normalized()
        w = n.cross(t).normalized() * width * 0.5
        pairs.append((bm.verts.new(p + w), bm.verts.new(p - w)))
    for (a0, a1), (b0, b1), n in zip(pairs, pairs[1:], normals):
        f = bm.faces.new((a0, a1, b1, b0))
        orient(f, n)
        f.material_index = index
        f.smooth = False
        for loop in f.loops:
            loop[part.uv].uv = WHITE_UV


def add_flat(part, mat, outline, origin, u, v, thickness):
    """Two-sided flat shape: outline (u, v) points fanned from origin, front and back a hair apart."""
    bm = part.bm
    index = part.mat_index(mat)
    normal = u.cross(v).normalized()
    for side in (1, -1):
        shift = normal * thickness * 0.5 * side
        hub = bm.verts.new(origin + shift)
        rim = [bm.verts.new(origin + shift + u * a + v * b) for a, b in outline]
        for p, q in zip(rim, rim[1:]):
            f = bm.faces.new((hub, p, q))
            orient(f, normal * side)
            f.material_index = index
            f.smooth = False
            for loop in f.loops:
                loop[part.uv].uv = WHITE_UV


def body_ring(axis, polar, steps, lift, a0=0.0, a1=360.0):
    """Points (and normals) on the body surface along a circle at `polar` degrees around axis."""
    z = Vector(axis).normalized()
    x = (AXIS_X - z * AXIS_X.dot(z)).normalized()
    y = z.cross(x)
    points, normals = [], []
    for k in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * k / steps)
        d = z * math.cos(math.radians(polar)) + (x * math.cos(a) + y * math.sin(a)) * math.sin(math.radians(polar))
        loc, n = surf(d)
        points.append(loc + n * lift)
        normals.append(n)
    return points, normals


def build_costumes():
    """Halloween costumes for either visitor (Costume_ prefix). They imitate neither species, so they are
    noise, not clues; VisitorCostume in Unity allows one per slot (head, neck, hand, back, body)."""

    # Witch hat (head): droopy brim, crooked cone leaning back, orange band. Hides Omangi's horns.
    part = Part("Costume_WitchHat")
    axis = Vector((0.0, 0.15, 1.0)).normalized()
    base = surf(axis)[0] - axis * 0.035
    add_cone(part, "CostumeHat", trs(base, track(axis), (1, 1, 1)), 0.30, 0.18, 0.03, segments=12, smooth=False)
    heights = (0.0, 0.08, 0.16, 0.24, 0.31, 0.37)
    radii = (0.17, 0.15, 0.115, 0.07, 0.03, 0.004)
    crown = [base + axis * h + AXIS_Y * 0.13 * (h / heights[-1]) ** 2 for h in heights]
    add_tube(part, "CostumeHat", crown, radii, AXIS_X, segments=8, cap_start=False)
    band = [base + axis * 0.03 + 0.168 * (math.cos(a) * AXIS_X + math.sin(a) * axis.cross(AXIS_X))
            for a in (2 * math.pi * k / 12 for k in range(13))]
    add_tube(part, "CostumeOrange", band, [0.014] * len(band), axis, segments=4, cap_start=False, cap_end=False)
    part.finish()

    # Jack-o'-lantern candy basket (hand) hanging from the visitor's right hand (-X).
    part = Part("Costume_PumpkinBasket")
    start, n = surf((-1.0, -0.3, -0.1))
    d = Vector((-0.75, -0.45, -0.45)).normalized()
    hand = start - n * 0.03 + d * 0.17
    rx, rz = 0.095, 0.075
    center = hand + Vector((-0.03, -0.02, -0.13))
    add_sphere(part, "CostumeOrange", trs(center, Matrix.Identity(4), (rx, rx, rz)), u=8, v=5)
    for f in part.bm.faces:
        f.smooth = False  # faceted lobes
    add_cone(part, "CostumeDark", trs(center + AXIS_Z * (rz - 0.002), Matrix.Identity(4), (1, 1, 1)),
             0.055, 0.055, 0.006, segments=8, smooth=False)  # open top full of candy darkness

    def on_front(x, z):
        y = -rx * math.sqrt(max(1 - (x / rx) ** 2 - (z / rz) ** 2, 0.0)) - 0.004
        return center + Vector((x, y, z))

    bm = part.bm
    dark = part.mat_index("CostumeDark")
    shapes = [[(sx * 0.03 - 0.013, 0.008), (sx * 0.03 + 0.013, 0.008), (sx * 0.03, 0.03)] for sx in (-1, 1)]
    shapes.append([(-0.05, -0.012), (-0.025, -0.03), (0.0, -0.02), (0.025, -0.03), (0.05, -0.012),
                   (0.03, -0.045), (-0.03, -0.045)])  # grin
    for shape in shapes:
        verts = [bm.verts.new(on_front(x, z)) for x, z in shape]
        f = bm.faces.new(verts)
        orient(f, FRONT)
        f.material_index = dark
        f.smooth = False
        for loop in f.loops:
            loop[part.uv].uv = WHITE_UV
    side = AXIS_X * 0.07
    handle = [center + side * math.cos(t) + AXIS_Z * 0.05 + (hand - center - AXIS_Z * 0.05) * math.sin(t)
              for t in (math.pi * k / 8 for k in range(9))]
    add_tube(part, "CostumeDark", handle, [0.006] * len(handle), FRONT, segments=4, cap_start=False, cap_end=False)
    part.finish()

    # Mummy bandage (body): loose strips around the body and the left arm, one end hanging free.
    part = Part("Costume_Bandage")
    for axis, polar, width in (((0.25, 0.0, 1.0), 122, 0.04), ((-0.35, 0.05, 1.0), 50, 0.035),
                               ((0.1, 0.0, 1.0), 146, 0.04)):
        points, normals = body_ring(axis, polar, 22, 0.012)
        add_ribbon(part, "CostumeBandage", points, normals, width)
    points, normals = body_ring((0.25, 0.0, 1.0), 122, 4, 0.012, 330, 360)
    tail_end = points[-1]
    tail = [tail_end + Vector((0.0, -0.01 * k, -0.035 * k)) for k in range(4)]
    add_ribbon(part, "CostumeBandage", tail, [normals[-1]] * len(tail), 0.035)
    start, n = surf((1.0, -0.3, -0.1))
    d = Vector((0.75, -0.45, -0.45)).normalized()
    arm = start - n * 0.03 + d * 0.085
    ax = (AXIS_Z - d * AXIS_Z.dot(d)).normalized()
    ay = d.cross(ax)
    helix, radial = [], []
    for k in range(17):
        a = 4 * math.pi * k / 16
        r = math.cos(a) * ax + math.sin(a) * ay
        helix.append(arm + d * (-0.05 + 0.1 * k / 16) + r * 0.066)
        radial.append(r)
    add_ribbon(part, "CostumeBandage", helix, radial, 0.028)
    part.finish()

    # Bat wings (back) strapped on behind the shoulders.
    part = Part("Costume_BatWings")
    outline = [(0.0, 0.05), (0.20, 0.20), (0.46, 0.30), (0.40, 0.10), (0.33, 0.13), (0.27, -0.02),
               (0.18, 0.03), (0.10, -0.10), (0.0, -0.05)]
    for sx in (-1, 1):
        root, n = surf((sx * 0.3, 1.0, 0.2))
        root = root + n * 0.01
        u = Vector((sx, 0.9, 0.0)).normalized()  # swept back, so from the front they read as wings, not ears
        v = AXIS_Z * 0.75
        add_flat(part, "CostumeWing", outline, root, u, v, 0.008)
        bone = [root + u * a + v * b for a, b in ((0.0, 0.0), (0.20, 0.20), (0.46, 0.30))]
        add_tube(part, "CostumeWing", bone, [0.012, 0.009, 0.004], AXIS_Y, segments=4)
    part.finish()

    # Bow tie (neck) under the face.
    part = Part("Costume_BowTie")
    knot, n = surf((0.0, -1.0, -0.55))
    knot = knot + n * 0.02
    for sx in (-1, 1):
        add_cone(part, "CostumeBow", trs(knot + AXIS_X * sx * 0.04, track(AXIS_X * sx), (0.45, 1, 1)),
                 0.012, 0.045, 0.07, segments=4, smooth=False)
    add_sphere(part, "CostumeBow", trs(knot, Matrix.Identity(4), (0.02, 0.014, 0.018)), u=6, v=3)
    part.finish()

    # Maid headband (head): white band behind the horns with a pleated lace frill standing on it,
    # alternately tall and short, in and out, over a black ribbon edge.
    part = Part("Costume_MaidHeadband")
    y_bias, lift = 0.12, 0.012
    band = [math.radians(a) for a in range(-95, 96, 16)]
    add_tube(part, "CostumeWhite", band_path(y_bias, lift, band), [0.012] * len(band), AXIS_Y, segments=4)
    lower, upper = [], []
    for k, a in enumerate(math.radians(d) for d in range(-70, 71, 7)):
        p, n = band_point(y_bias, lift, a)
        lower.append(p)
        upper.append(p + n * (0.05 if k % 2 == 0 else 0.038) + FRONT * (0.012 if k % 2 else -0.004))
    add_strip(part, "CostumeWhite", lower, upper)
    edge = [p + FRONT * 0.006 for p in lower]
    add_tube(part, "CostumeDark", edge, [0.006] * len(edge), AXIS_Y, segments=4)
    part.finish()

    # Bunny headband (head): black band and long black ears with pale insides; one stands straight,
    # the other folds forward halfway up.
    part = Part("Costume_BunnyHeadband")
    y_bias, lift = 0.05, 0.012
    add_tube(part, "CostumeDark", band_path(y_bias, lift, band), [0.011] * len(band), AXIS_Y, segments=4)
    width, length = 0.05, 0.3
    leaf = [(-0.5, 0.0), (-1.0, 0.3), (-0.9, 0.7), (-0.5, 0.95), (0.0, 1.0), (0.5, 0.95), (0.9, 0.7),
            (1.0, 0.3), (0.5, 0.0)]
    stem = [(-0.5, 0.0), (-1.0, 0.25), (-1.0, 0.5), (1.0, 0.5), (1.0, 0.25), (0.5, 0.0)]
    flap = [(-1.0, 0.0), (-0.9, 0.4), (-0.5, 0.8), (0.0, 0.9), (0.5, 0.8), (0.9, 0.4), (1.0, 0.0)]

    def ear_piece(origin, side, along, outline):
        """One flat piece of ear (outline in units of width across, length along) with its pale inside
        on the front face."""
        add_flat(part, "CostumeDark", outline, origin, side * width, along * length, 0.014)
        inside = side.cross(along).normalized() * 0.009
        inner = [(a * 0.55, 0.12 + b * 0.76) for a, b in outline]
        add_flat(part, "CostumeBunnyInner", inner, origin + inside, side * width, along * length, 0.002)

    for sx in (-1, 1):
        p, n = band_point(y_bias, lift, math.radians(sx * 32))
        up = (n + AXIS_Z * 0.6).normalized()
        side = (AXIS_X - up * AXIS_X.dot(up)).normalized()
        root = p - up * 0.01
        if sx < 0:
            ear_piece(root, side, up, leaf)
        else:
            ear_piece(root, side, up, stem)
            facing = side.cross(up).normalized()
            fold = math.radians(130)
            down = up * math.cos(fold) + facing * math.sin(fold)  # folded forward and down
            add_flat(part, "CostumeDark", flap, root + up * length * 0.5, side * width, down * length * 0.5, 0.014)
            inside = side.cross(down).normalized() * 0.009
            add_flat(part, "CostumeBunnyInner", [(a * 0.55, 0.1 + b * 0.75) for a, b in flap],
                     root + up * length * 0.5 + inside, side * width, down * length * 0.5, 0.002)
    part.finish()

    # Star sunglasses (eyes): gold party glasses, a five-pointed star around each eye with dark amber lenses.
    part = Part("Costume_StarSunglasses")
    outer_r, inner_r = 0.11, 0.06  # big enough to hide the button eyes between the points
    centers = eyewear_centers(outer_r)
    star = [((outer_r if k % 2 == 0 else inner_r) * math.cos(math.radians(90 + 36 * k)),
             (outer_r if k % 2 == 0 else inner_r) * math.sin(math.radians(90 + 36 * k))) for k in range(11)]
    for c in centers:
        add_flat(part, "CostumeLensAmber", star, c, AXIS_X, AXIS_Z, 0.004)
        rim = [c + AXIS_X * a + AXIS_Z * b for a, b in star]
        add_tube(part, "CostumeGold", rim, [0.007] * len(rim), FRONT, segments=4, cap_start=False, cap_end=False)
    left, right = centers[0] + AXIS_X * inner_r, centers[1] - AXIS_X * inner_r
    bridge = [left.lerp(right, t) + AXIS_Z * 0.01 * math.sin(math.pi * t) for t in (k / 4 for k in range(5))]
    add_tube(part, "CostumeGold", bridge, [0.006] * len(bridge), FRONT, segments=4)
    add_temples(part, "CostumeGold", centers, outer_r * 0.85)
    part.finish()

    # Alien sunglasses (eyes): thick lime-green frame around dark smoky teardrop lenses slanted like an
    # alien's eyes: round at the inner bottom next to the low bridge, narrowing to points that sweep up
    # and out past the temples.
    part = Part("Costume_AlienSunglasses")
    half_len, half_w = 0.105, 0.07
    centers = eyewear_centers(half_len)
    round_ends = []
    for sx, c in zip((-1, 1), centers):
        lean = math.radians(55)
        axis = (sx * math.cos(lean), math.sin(lean))  # from the round end up and out to the point
        across = (-axis[1], axis[0])
        drop = []
        for t in (2 * math.pi * k / 16 for k in range(17)):
            s = math.cos(t)  # +1 at the point, -1 at the round end
            a, b = half_len * s, half_w * math.sin(t) * (0.3 + 0.7 * (1 - s) / 2)
            drop.append((a * axis[0] + b * across[0], a * axis[1] + b * across[1]))
        yaw = math.radians(15)
        u = Vector((math.cos(yaw), sx * math.sin(yaw), 0.0))  # the outer side wraps back a little
        lens = c + u * (axis[0] * 0.045) + AXIS_Z * (axis[1] * 0.045)  # the wide part sits over the eye
        add_flat(part, "CostumeAlienLens", drop, lens, u, AXIS_Z, 0.005)
        rim = [lens + u * a + AXIS_Z * b for a, b in drop]
        # The bridge joins the inner flank of each lens a little above its round end (nearest the center there).
        low = rim[8].z
        round_ends.append(min((p for p in rim if low + 0.025 <= p.z <= low + 0.06), key=lambda p: abs(p.x)))
        add_tube(part, "CostumeLime", rim, [0.01] * len(rim), FRONT, segments=5, cap_start=False, cap_end=False)
    left, right = round_ends
    bridge = [left.lerp(right, t) + AXIS_Z * 0.012 * math.sin(math.pi * t) for t in (k / 4 for k in range(5))]  # nose arch
    add_tube(part, "CostumeLime", bridge, [0.012] * len(bridge), FRONT, segments=5)
    add_temples(part, "CostumeLime", centers, half_len * 0.8)
    part.finish()


def add_strip(part, mat, lower, upper):
    """Two-sided strip between two rows of points (frills)."""
    bm = part.bm
    index = part.mat_index(mat)
    for side in (1, -1):  # each side gets its own vertices (bmesh allows one face per vertex set)
        lo = [bm.verts.new(p) for p in lower]
        hi = [bm.verts.new(p) for p in upper]
        for k in range(len(lo) - 1):
            quad = (lo[k], lo[k + 1], hi[k + 1], hi[k])
            f = bm.faces.new(quad if side > 0 else quad[::-1])
            f.material_index = index
            f.smooth = False
            for loop in f.loops:
                loop[part.uv].uv = WHITE_UV


def build_ghost():
    """Ghost sheet worn over either visitor: a plain lathed bell from head to ground, no eye holes,
    only a soft round volume so nothing underneath reads through. The hem is wavy; its flutter is
    done in the VisitorSheet shader (weight grows toward the ground). Hidden until GhostSheet.Wear()."""
    part = Part("Ghost_Sheet")
    bm = part.bm
    # (radius, height) from the crown down to the hem.
    # The crown sits a bit low so horns and other top parts can push it up (GhostSheet bumps).
    profile = [(0.0, 0.80), (0.17, 0.78), (0.30, 0.72), (0.39, 0.62), (0.43, 0.50),
               (0.44, 0.37), (0.46, 0.22), (0.49, 0.09), (0.52, 0.01)]
    segments = 16
    rings = []
    for i, (r, z) in enumerate(profile[1:], start=1):
        ring = []
        for k in range(segments):
            a = 2 * math.pi * k / segments
            hem = 0.025 * math.sin(4 * a) if i == len(profile) - 1 else 0.0  # wavy hem
            ring.append(bm.verts.new((r * math.sin(a), -r * math.cos(a), max(z + hem, 0.0))))
        rings.append(ring)
    crown = bm.verts.new((0.0, 0.0, profile[0][1]))
    index = part.mat_index("Sheet")
    faces = [bm.faces.new((rings[0][k], rings[0][(k + 1) % segments], crown)) for k in range(segments)]
    for a, b in zip(rings, rings[1:]):
        for k in range(segments):
            k2 = (k + 1) % segments
            faces.append(bm.faces.new((a[k], a[k2], b[k2], b[k])))
    for f in faces:
        orient(f, f.calc_center_median() - Vector((0.0, 0.0, 0.45)))
        f.material_index = index
        f.smooth = True
        for loop in f.loops:
            loop[part.uv].uv = WHITE_UV
    part.finish()


# ---------------------------------------------------------------- export / preview

def export_fbx(path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type == "MESH")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", path_mode="STRIP",
        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True)


# (name, fur color, fur samples the atlas, active prefixes)
PREVIEW_SETS = [
    ("Koonmungchi", (0.42, 0.42, 0.44), False, ("Body", "Koon_")),
    ("Omangi", (0.62, 0.12, 0.12), True, ("Body", "Oma_")),
    ("Koon_OmaCostume", (0.75, 0.08, 0.06), False, ("Body", "Koon_", "Fake_HornBand", "Fake_GoatEars")),
    ("Oma_KoonCostume", (0.50, 0.45, 0.45), True, ("Body", "Oma_", "Fake_ConeNose", "Fake_RaccoonTail", "Fake_XGlasses")),
    ("Koon_Glasses", (0.42, 0.42, 0.44), False, ("Body", "Koon_", "Fake_XGlasses")),
    ("Ghost", (0.42, 0.42, 0.44), False, ("Ghost_",)),
    ("Koon_Bucket", (0.75, 0.08, 0.06), False, ("Body", "Koon_", "Fake_PaintBucket")),
    ("Oma_Bucket", (0.62, 0.12, 0.12), True, ("Body", "Oma_", "Fake_PaintBucket")),
    ("Koon_Costumes", (0.42, 0.42, 0.44), False, ("Body", "Koon_", "Costume_WitchHat", "Costume_BowTie",
                                                   "Costume_PumpkinBasket", "Costume_BatWings", "Costume_Bandage")),
    ("Oma_Costumes", (0.62, 0.12, 0.12), True, ("Body", "Oma_", "Costume_WitchHat", "Costume_BowTie",
                                                 "Costume_PumpkinBasket", "Costume_BatWings", "Costume_Bandage")),
    ("Koon_Maid", (0.42, 0.42, 0.44), False, ("Body", "Koon_", "Costume_MaidHeadband", "Costume_StarSunglasses")),
    ("Oma_Maid", (0.62, 0.12, 0.12), True, ("Body", "Oma_", "Costume_MaidHeadband", "Costume_AlienSunglasses")),
    ("Koon_Bunny", (0.42, 0.42, 0.44), False, ("Body", "Koon_", "Costume_BunnyHeadband", "Costume_AlienSunglasses")),
    ("Oma_Bunny", (0.62, 0.12, 0.12), True, ("Body", "Oma_", "Costume_BunnyHeadband", "Costume_StarSunglasses")),
]


def render_preview(out_dir):
    scene = bpy.context.scene
    parts = [o for o in scene.objects if o.type == "MESH"]
    for i, (name, fur_color, textured, prefixes) in enumerate(PREVIEW_SETS):
        fur = make_material("Fur_" + name, fur_color, textured)
        for src in parts:
            if not src.name.startswith(prefixes):
                continue
            obj = src.copy()
            scene.collection.objects.link(obj)
            obj.location.x = (i - (len(PREVIEW_SETS) - 1) / 2) * 1.1
            for slot in obj.material_slots:
                if slot.material and slot.material.name == "Fur":
                    slot.link = "OBJECT"
                    slot.material = fur
    for src in parts:
        src.hide_render = True

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x, scene.render.resolution_y = 420 * len(PREVIEW_SETS), 640
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.25, 0.25, 0.27)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = [math.radians(a) for a in (50, 0, 20)]
    scene.collection.objects.link(sun)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 1.1 * len(PREVIEW_SETS) + 0.4
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    views = {"front": ((0, -5, 0.5), (90, 0, 0)), "back": ((0, 5, 0.5), (90, 0, 180)),
             "side": ((5, -5, 0.6), (85, 0, 45))}
    for view, (loc, rot) in views.items():
        cam.location = loc
        cam.rotation_euler = [math.radians(a) for a in rot]
        scene.render.filepath = os.path.join(out_dir, f"visitors_{view}.png")
        bpy.ops.render.render(write_still=True)


def main():
    global ATLAS_PATH
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview")
    args = parser.parse_args(argv)
    out = os.path.abspath(args.out)
    ATLAS_PATH = os.path.join(os.path.dirname(out), "Textures", "VisitorAtlas.png")

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    build_body()
    build_koon()
    build_oma()
    build_fakes()
    build_costumes()
    build_ghost()

    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"[visitors] {obj.name}: {tris} tris")
    export_fbx(out)
    print(f"[visitors] exported {args.out}")
    if args.preview:
        render_preview(os.path.abspath(args.preview))


if __name__ == "__main__":
    main()
