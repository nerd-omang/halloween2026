"""
Builds the low-poly visitor meshes (Koonmungchi / Omangi and costume props), their texture atlas,
and exports them to one FBX.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_visitors.py -- \
      --out Assets/Models/Characters/Visitors.fbx [--preview <dir>]

Conventions:
  - Every part is its own object with its origin at the world origin (between the feet),
    so in Unity each part sits at local zero under the visitor root and parts can be toggled freely.
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
    "PropBand": ((0.05, 0.05, 0.05), False),
    "PropHorn": ((0.10, 0.02, 0.02), False),
    "PropFelt": ((0.12, 0.11, 0.11), False),
    "PropPaper": ((0.55, 0.55, 0.52), False),
    "PropString": ((0.92, 0.92, 0.92), False),
    "PropStrap": ((0.35, 0.20, 0.10), False),
    "PropFrame": ((0.08, 0.08, 0.08), False),
    "PropLens": ((1.0, 1.0, 1.0), True),
    "FakeTailStripe": ((1.0, 1.0, 1.0), True),
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
        return cell_uv(uv_cell, 0.5, i / (count - 1)) if uv_cell else WHITE_UV

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

    # Tail stripes along V. Real tail: 7 bands, tip always dark. Fake tail: 5 bands, beige-tinted.
    t = (ys - CELL_TAIL[1]) / CELL_TAIL[3]
    dark = (np.floor((1 - t) * 7).astype(int) % 2) == 0
    paint(in_cell(CELL_TAIL) & dark, (0.22, 0.22, 0.24))
    paint(in_cell(CELL_TAIL) & ~dark, (0.68, 0.68, 0.70))
    dark = (np.floor(t * 5).astype(int) % 2) == 1
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
    # Long, slightly droopy raccoon snout with a dark tip.
    part = Part("Koon_Nose")
    base, n = surf((0.0, -1.0, -0.1))
    d = Vector((0.0, -1.0, -0.28)).normalized()
    start = base - d * 0.05
    length = 0.24
    ts = [i / 4 for i in range(5)]
    pts = [start + d * (length * t) + Vector((0, 0, -0.035)) * t * t for t in ts]
    add_tube(part, "Fur", pts, [0.085 * (1 - t) + 0.05 * t for t in ts], AXIS_X, cap_start=False)
    add_sphere(part, "NoseTip", trs(pts[-1], Matrix(), (0.046, 0.046, 0.042)))
    part.finish()

    # Round blue button eyes; the X stitch is in the texture. The X is a permanent trait, not an expression.
    part = Part("Koon_Eyes")
    for sx in (-1, 1):
        loc, n = surf((sx * 0.36, -1.0, 0.42))
        add_disc(part, "Button", loc + n * 0.006, n, 0.058, 0.02, CELL_BUTTON)
    part.finish()

    # Thick striped raccoon tail that curls up and peeks over one shoulder.
    part = Part("Koon_Tail")
    anchor, n = surf((0.0, 1.0, -0.35))
    start = anchor - n * 0.05
    ts = [i / 7 for i in range(8)]
    pts = [start + Vector((0.15 * t * t, 0.28 * t, 0.55 * t * t)) for t in ts]

    def tail_radius(t):
        r = 0.075 + 0.05 * math.sin(math.pi * t * 0.75)
        if t > 0.8:
            r *= math.sqrt(max(1 - ((t - 0.8) / 0.2) ** 2, 0.02))
        return r

    add_tube(part, "TailStripe", pts, [tail_radius(t) for t in ts], AXIS_X, segments=7,
             cap_start=False, uv_cell=CELL_TAIL)
    part.finish()


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

    # Stiff striped fake tail strapped around the waist; blunt sewn-off end.
    part = Part("Fake_RaccoonTail")
    strap = []
    for a in range(0, 361, 20):
        loc, sn = surf((math.sin(math.radians(a)), -math.cos(math.radians(a)), -0.85))
        strap.append(loc + sn * 0.012)
    add_tube(part, "PropStrap", strap, [0.014] * len(strap), AXIS_Z, segments=4, cap_start=False, cap_end=False)
    back, n = surf((0.0, 1.0, -0.85))
    ts = [i / 5 for i in range(6)]
    pts = [back + n * 0.02 + Vector((0.0, 0.3 * t, 0.3 * t + 0.1 * t * t)) for t in ts]
    add_tube(part, "FakeTailStripe", pts, [0.07] * 5 + [0.055], AXIS_X, segments=7,
             cap_start=False, uv_cell=CELL_FAKE_TAIL)
    part.finish()

    # Glasses with an X drawn on each lens, imitating Koonmungchi's stitched button eyes.
    part = Part("Fake_XGlasses")
    rim_r, lens_r = 0.066, 0.062
    eye_y = 0.42
    centers = [surf((sx * 0.36, -1.0, eye_y))[0] for sx in (-1, 1)]
    # Lenses sit on one flat plane just in front of the face.
    face_y = min(project(c + AXIS_X * r * math.cos(a) + AXIS_Z * r * math.sin(a), FRONT, 0.0).y
                 for c in centers for r in (0.0, rim_r) for a in (k * math.pi / 4 for k in range(8)))
    centers = [Vector((c.x, face_y - 0.012, c.z)) for c in centers]
    for c in centers:
        rim = [c + rim_r * (math.cos(a) * AXIS_X + math.sin(a) * AXIS_Z)
               for a in (2 * math.pi * k / 12 for k in range(13))]
        add_tube(part, "PropFrame", rim, [0.006] * len(rim), FRONT, segments=4, cap_start=False, cap_end=False)
        add_disc(part, "PropLens", c, FRONT, lens_r, 0.004, CELL_LENS, segments=12)
    left, right = centers[0] + AXIS_X * rim_r, centers[1] - AXIS_X * rim_r
    bridge = [left.lerp(right, t) + AXIS_Z * 0.012 * math.sin(math.pi * t) for t in (k / 4 for k in range(5))]
    add_tube(part, "PropFrame", bridge, [0.005] * len(bridge), FRONT, segments=4)
    for sx, c in zip((-1, 1), centers):
        temple = [c + AXIS_X * sx * rim_r]
        for a in range(40, 101, 15):
            loc, sn = surf((sx * math.sin(math.radians(a)), -math.cos(math.radians(a)), eye_y))
            temple.append(loc + sn * 0.012)
        add_tube(part, "PropFrame", temple, [0.005] * len(temple), AXIS_Z, segments=4)
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
            obj.location.x = (i - 1.5) * 1.1
            for slot in obj.material_slots:
                if slot.material and slot.material.name == "Fur":
                    slot.link = "OBJECT"
                    slot.material = fur
    for src in parts:
        src.hide_render = True

    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x, scene.render.resolution_y = 1800, 640
    scene.world = bpy.data.worlds.new("PreviewWorld")
    scene.world.color = (0.25, 0.25, 0.27)
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = [math.radians(a) for a in (50, 0, 20)]
    scene.collection.objects.link(sun)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 4.6
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

    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"[visitors] {obj.name}: {tris} tris")
    export_fbx(out)
    print(f"[visitors] exported {args.out}")
    if args.preview:
        render_preview(os.path.abspath(args.preview))


if __name__ == "__main__":
    main()
