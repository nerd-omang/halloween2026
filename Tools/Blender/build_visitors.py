"""
Builds the visitor meshes (Koonmungchi / Omangi and costume props) and exports them to one FBX.

Usage (from the project root):
  blender -b --factory-startup --python Tools/Blender/build_visitors.py -- \
      --out Assets/Models/Characters/Visitors.fbx [--preview <dir>]

Conventions:
  - Every part is its own object with its origin at the world origin (between the feet),
    so in Unity each part sits at local zero under the visitor root and parts can be toggled freely.
  - Characters face -Y in Blender, which becomes +Z (forward) in Unity.
  - Object name prefixes: Body (shared), Koon_ (Koonmungchi), Oma_ (Omangi), Fake_ (costume props).
  - Material names are slot names; Unity remaps them to project materials. "Fur" is swapped per character.
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector, noise
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

# Preview colors only; the in-game look is defined by the Unity materials.
COLORS = {
    "Fur": (0.42, 0.42, 0.44),
    "NoseTip": (0.08, 0.08, 0.09),
    "Button": (0.15, 0.35, 0.85),
    "Thread": (0.05, 0.07, 0.18),
    "TailLight": (0.68, 0.68, 0.70),
    "TailDark": (0.22, 0.22, 0.24),
    "EyeBlack": (0.02, 0.02, 0.02),
    "Horn": (0.06, 0.05, 0.05),
    "Ear": (0.07, 0.06, 0.06),
    "Mark": (0.03, 0.03, 0.03),
    "PropBand": (0.05, 0.05, 0.05),
    "PropHorn": (0.10, 0.02, 0.02),
    "PropFelt": (0.12, 0.11, 0.11),
    "PropPaper": (0.55, 0.55, 0.52),
    "PropString": (0.92, 0.92, 0.92),
    "PropStrap": (0.35, 0.20, 0.10),
    "PropFrame": (0.08, 0.08, 0.08),
    "PropLens": (0.60, 0.75, 0.95),
    "PropMarker": (0.05, 0.07, 0.18),
    "FakeTailLight": (0.75, 0.72, 0.66),
    "FakeTailDark": (0.25, 0.22, 0.20),
}

BVH = None


def get_mat(name):
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*COLORS[name], 1.0)
    return mat


class Part:
    """One exported object, built from several pieces in a single bmesh."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []

    def mat_index(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def assign(self, verts, mat, smooth=True):
        index = self.mat_index(mat)
        for face in {f for v in verts for f in v.link_faces}:
            face.material_index = index
            face.smooth = smooth

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


def add_sphere(part, mat, matrix, u=12, v=8):
    res = bmesh.ops.create_uvsphere(part.bm, u_segments=u, v_segments=v, radius=1.0, matrix=matrix)
    part.assign(res["verts"], mat)


def add_cone(part, mat, matrix, r1, r2, depth, segments=12, smooth=True):
    res = bmesh.ops.create_cone(part.bm, cap_ends=True, cap_tris=True, segments=segments,
                                radius1=r1, radius2=r2, depth=depth, matrix=matrix)
    part.assign(res["verts"], mat, smooth)


def add_box(part, mat, matrix):
    res = bmesh.ops.create_cube(part.bm, size=1.0, matrix=matrix)
    part.assign(res["verts"], mat, smooth=False)


def add_tube(part, mat_fn, points, radii, ref, segments=10):
    """Closed tube along points. mat_fn(i) gives the material of the segment starting at point i."""
    bm = part.bm
    count = len(points)
    rings = []
    for i, p in enumerate(points):
        t = (points[min(i + 1, count - 1)] - points[max(i - 1, 0)]).normalized()
        b = t.cross(ref).normalized()
        n = b.cross(t)
        r = max(radii[i], 0.002)
        rings.append([bm.verts.new(p + r * (math.cos(a) * b + math.sin(a) * n))
                      for a in (2 * math.pi * k / segments for k in range(segments))])
    faces = []
    for i in range(count - 1):
        index = part.mat_index(mat_fn(i))
        for k in range(segments):
            k2 = (k + 1) % segments
            f = bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
            f.material_index = index
            faces.append(f)
    for ring, p, i in ((rings[0], points[0], 0), (rings[-1], points[-1], count - 2)):
        center = bm.verts.new(p)
        index = part.mat_index(mat_fn(i))
        for k in range(segments):
            f = bm.faces.new((ring[k], ring[(k + 1) % segments], center))
            f.material_index = index
            faces.append(f)
    for f in faces:
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=faces)


def add_decal(part, mat, anchor_dir, rects, step=0.006, lift=0.003):
    """Flat strips projected onto the body surface. rects are (u0, v0, u1, v1) in meters around the anchor."""
    bm = part.bm
    anchor, n = surf(anchor_dir)
    u_axis = (AXIS_X - n * AXIS_X.dot(n)).normalized()
    v_axis = n.cross(u_axis)
    index = part.mat_index(mat)
    for u0, v0, u1, v1 in rects:
        nu = max(1, math.ceil((u1 - u0) / step))
        nv = max(1, math.ceil((v1 - v0) / step))
        grid = [[bm.verts.new(project(anchor + u_axis * (u0 + (u1 - u0) * i / nu)
                                      + v_axis * (v0 + (v1 - v0) * j / nv), n, lift))
                 for j in range(nv + 1)] for i in range(nu + 1)]
        for i in range(nu):
            for j in range(nv):
                f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
                f.normal_update()
                if f.normal.dot(n) < 0:
                    f.normal_flip()
                f.material_index = index
                f.smooth = False


def limb(part, start_dir, direction, length, radius, sink=0.03):
    start, n = surf(start_dir)
    d = Vector(direction).normalized()
    center = start - n * sink + d * (length * 0.5)
    rot = AXIS_Z.rotation_difference(d).to_matrix().to_4x4()
    add_sphere(part, "Fur", trs(center, rot, (radius, radius, length * 0.5)), u=10, v=6)


def band_path(y_bias, lift, angles):
    """Points following the head surface over the top, side to side (headbands)."""
    pts = []
    for a in angles:
        loc, n = surf((math.sin(a), y_bias, math.cos(a)))
        pts.append(loc + n * lift)
    return pts


def band_point(y_bias, lift, angle):
    loc, n = surf((math.sin(angle), y_bias, math.cos(angle)))
    return loc + n * lift, n


# ---------------------------------------------------------------- parts

def build_body():
    global BVH
    part = Part("Body")
    res = bmesh.ops.create_icosphere(part.bm, subdivisions=4, radius=RADIUS,
                                     matrix=Matrix.Translation(CENTER), calc_uvs=True)
    for v in res["verts"]:
        n = (v.co - CENTER).normalized()
        # Cloud-like lumps on the silhouette; fine fur is left to the shader.
        bump = noise.noise(n * 2.6 + Vector((3.1, 1.7, 0.4))) * 0.035 + noise.noise(n * 6.0) * 0.012
        co = CENTER + n * (RADIUS + bump)
        co.z = CENTER.z + (co.z - CENTER.z) * SQUASH
        v.co = co
    part.assign(res["verts"], "Fur")
    BVH = BVHTree.FromBMesh(part.bm)

    for sx in (-1, 1):
        limb(part, (sx, -0.3, -0.1), (sx * 0.75, -0.45, -0.45), 0.17, 0.06)  # arms
        limb(part, (sx * 0.4, -0.1, -1.0), (0.0, -0.15, -1.0), 0.17, 0.075, sink=0.05)  # legs
    return part.finish()


def build_koon():
    # Long, slightly droopy raccoon snout with a dark tip.
    part = Part("Koon_Nose")
    base, n = surf((0.0, -1.0, -0.1))
    d = Vector((0.0, -1.0, -0.28)).normalized()
    start = base - d * 0.05
    length = 0.24
    steps = 10
    pts = [start + d * (length * t) + Vector((0, 0, -0.035)) * t * t for t in (i / (steps - 1) for i in range(steps))]
    radii = [0.085 * (1 - t) + 0.05 * t for t in (i / (steps - 1) for i in range(steps))]
    add_tube(part, lambda i: "Fur", pts, radii, AXIS_X, segments=12)
    tip = pts[-1]
    add_sphere(part, "NoseTip", trs(tip, Matrix(), (0.046, 0.046, 0.042)), u=10, v=6)
    part.finish()

    # Round blue button eyes with an X stitch. The X is a permanent trait, not an expression.
    part = Part("Koon_Eyes")
    for sx in (-1, 1):
        loc, n = surf((sx * 0.36, -1.0, 0.42))
        rot = track(n)
        add_cone(part, "Button", trs(loc + n * 0.004, rot, (1, 1, 1)), 0.058, 0.058, 0.02, segments=16, smooth=False)
        for angle in (45, -45):
            spin = Matrix.Rotation(math.radians(angle), 4, "Z")
            add_box(part, "Thread", trs(loc + n * 0.016, rot @ spin, (0.075, 0.012, 0.008)))
    part.finish()

    # Thick striped raccoon tail that curls up and peeks over one shoulder.
    part = Part("Koon_Tail")
    anchor, n = surf((0.0, 1.0, -0.35))
    start = anchor - n * 0.05
    steps = 22
    ts = [i / (steps - 1) for i in range(steps)]
    pts = [start + Vector((0.15 * t * t, 0.28 * t, 0.55 * t * t)) for t in ts]

    def tail_radius(t):
        r = 0.075 + 0.05 * math.sin(math.pi * t * 0.75)
        if t > 0.8:
            r *= math.sqrt(max(1 - ((t - 0.8) / 0.2) ** 2, 0.02))
        return r

    bands = 7  # counted from the tip so the tip is always dark
    add_tube(part, lambda i: "TailDark" if int((1 - ts[i]) * bands) % 2 == 0 else "TailLight",
             pts, [tail_radius(t) for t in ts], AXIS_X, segments=10)
    part.finish()


def build_oma():
    # Black, slightly wide oval eyes tilted into a V (inner ends lower) for a mischievous look.
    part = Part("Oma_Eyes")
    for sx in (-1, 1):
        loc, n = surf((sx * 0.34, -1.0, 0.4))
        rot = track(n) @ Matrix.Rotation(math.radians(sx * 18), 4, "Z")
        add_sphere(part, "EyeBlack", trs(loc + n * 0.002, rot, (0.052, 0.034, 0.012)), u=12, v=6)
    part.finish()

    # Puckered "3"-on-its-side (w) mouth drawn as a thin tube on the face.
    part = Part("Oma_Mouth")
    anchor, n = surf((0.0, -1.0, 0.02))
    u_axis = (AXIS_X - n * AXIS_X.dot(n)).normalized()
    v_axis = n.cross(u_axis)
    half, height, steps = 0.04, 0.022, 17
    pts = []
    for i in range(steps):
        u = -half + 2 * half * i / (steps - 1)
        v = 0.5 * height * math.cos(2 * math.pi * u / half)
        pts.append(project(anchor + u_axis * u + v_axis * v, n, 0.006))
    add_tube(part, lambda i: "Mark", pts, [0.0055] * steps, n, segments=6)
    part.finish()

    # Thin inverted cross centered on the navel.
    part = Part("Oma_Cross")
    add_decal(part, "Mark", (0.0, -1.0, -0.75),
              [(-0.009, -0.07, 0.009, 0.06), (-0.04, -0.035, 0.04, -0.017)])
    part.finish()

    # Floppy goat ears on both sides of the head.
    part = Part("Oma_Ears")
    for sx in (-1, 1):
        anchor, n = surf((sx, -0.15, 0.55))
        d = Vector((sx * 0.85, -0.2, -0.5)).normalized()
        rot = basis(d, FRONT)  # local Z = length, local X = thickness (faces forward)
        add_sphere(part, "Ear", trs(anchor + d * 0.1, rot, (0.022, 0.05, 0.13)), u=12, v=8)
    part.finish()

    # Two small horns on top, curving slightly back and out.
    part = Part("Oma_Horns")
    for sx in (-1, 1):
        anchor, n = surf((sx * 0.3, -0.1, 1.0))
        start = anchor - n * 0.02
        steps = 8
        ts = [i / (steps - 1) for i in range(steps)]
        pts = [start + n * 0.1 * t + Vector((sx * 0.015, 0.035, 0.0)) * t * t for t in ts]
        add_tube(part, lambda i: "Horn", pts, [HORN_BASE * (1 - t) + 0.003 for t in ts], AXIS_X, segments=8)
    part.finish()

    # Short goat tail, about as stubby as a rabbit's.
    part = Part("Oma_Tail")
    anchor, n = surf((0.0, 1.0, -0.3))
    rot = Matrix.Rotation(math.radians(-30), 4, "X")
    add_sphere(part, "Fur", trs(anchor + n * 0.025, rot, (0.05, 0.045, 0.062)), u=10, v=8)
    part.finish()


def build_fakes():
    """Costume props. They sit in the same places as the real parts but read as worn props."""
    top = [math.radians(a) for a in range(-95, 96, 8)]

    # Plastic horn headband: visible band over the head, straight glossy cones on collars.
    part = Part("Fake_HornBand")
    y_bias, lift = -0.12, 0.012
    add_tube(part, lambda i: "PropBand", band_path(y_bias, lift, top), [0.011] * len(top), AXIS_Y, segments=6)
    for sx in (-1, 1):
        p, n = band_point(y_bias, lift, math.radians(sx * 28))
        rot = track(n)
        add_cone(part, "PropBand", trs(p + n * 0.008, rot, (1, 1, 1)), 0.068, 0.068, 0.014, segments=10, smooth=False)
        add_cone(part, "PropHorn", trs(p + n * 0.065, rot, (1, 1, 1)), 0.11 * math.tan(math.radians(30)), 0.002, 0.11, segments=10)
    part.finish()

    # Felt goat ears on a second headband, sitting a little further back and sticking out stiffly.
    part = Part("Fake_GoatEars")
    y_bias, lift = 0.1, 0.012
    add_tube(part, lambda i: "PropBand", band_path(y_bias, lift, top), [0.011] * len(top), AXIS_Y, segments=6)
    for sx in (-1, 1):
        p, n = band_point(y_bias, lift, math.radians(sx * 78))
        d = Vector((sx * 0.9, 0.0, -0.25)).normalized()
        thickness = FRONT - d * FRONT.dot(d)
        rot = basis(thickness, d.cross(thickness))  # local Z = thickness, local Y = length along d
        add_cone(part, "PropFelt", trs(p + d * 0.1, rot, (0.05, 0.12, 1.0)), 1.0, 1.0, 0.01, segments=14, smooth=False)
    part.finish()

    # Paper party-cone nose held on with an elastic string around the head.
    part = Part("Fake_ConeNose")
    anchor, n = surf((0.0, -1.0, -0.08))
    d = Vector((0.0, -1.0, -0.05)).normalized()
    rot = AXIS_Z.rotation_difference(d).to_matrix().to_4x4()
    add_cone(part, "PropPaper", trs(anchor + d * 0.08, rot, (1, 1, 1)), 0.065, 0.004, 0.2, segments=14)
    string = []
    for a in range(25, 336, 10):
        loc, sn = surf((math.sin(math.radians(a)), -math.cos(math.radians(a)), -0.15))
        string.append(loc + sn * 0.006)
    add_tube(part, lambda i: "PropString", string, [0.0035] * len(string), AXIS_Z, segments=5)
    part.finish()

    # Stiff striped fake tail strapped around the waist; blunt sewn-off end.
    part = Part("Fake_RaccoonTail")
    strap = []
    for a in range(0, 361, 8):
        loc, sn = surf((math.sin(math.radians(a)), -math.cos(math.radians(a)), -0.85))
        strap.append(loc + sn * 0.01)
    add_tube(part, lambda i: "PropStrap", strap, [0.014] * len(strap), AXIS_Z, segments=6)
    back, n = surf((0.0, 1.0, -0.85))
    steps = 14
    ts = [i / (steps - 1) for i in range(steps)]
    pts = [back + n * 0.02 + Vector((0.0, 0.3 * t, 0.3 * t + 0.1 * t * t)) for t in ts]
    radii = [0.07] * (steps - 2) + [0.066, 0.05]
    add_tube(part, lambda i: "FakeTailDark" if int(ts[i] * 5) % 2 else "FakeTailLight",
             pts, radii, AXIS_X, segments=10)
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
    rot = track(FRONT)
    for c in centers:
        rim = [c + rim_r * (math.cos(a) * AXIS_X + math.sin(a) * AXIS_Z)
               for a in (2 * math.pi * k / 20 for k in range(21))]
        add_tube(part, lambda i: "PropFrame", rim, [0.006] * len(rim), FRONT, segments=5)
        add_cone(part, "PropLens", trs(c, rot, (1, 1, 1)), lens_r, lens_r, 0.004, segments=20, smooth=False)
        for angle in (45, -45):
            spin = Matrix.Rotation(math.radians(angle), 4, "Z")
            add_box(part, "PropMarker", trs(c + FRONT * 0.003, rot @ spin, (0.085, 0.011, 0.003)))
    left, right = centers[0] + AXIS_X * rim_r, centers[1] - AXIS_X * rim_r
    bridge = [left.lerp(right, t) + AXIS_Z * 0.012 * math.sin(math.pi * t) for t in (k / 6 for k in range(7))]
    add_tube(part, lambda i: "PropFrame", bridge, [0.005] * len(bridge), FRONT, segments=6)
    for sx, c in zip((-1, 1), centers):
        temple = [c + AXIS_X * sx * rim_r]
        for a in range(40, 101, 6):
            loc, sn = surf((sx * math.sin(math.radians(a)), -math.cos(math.radians(a)), eye_y))
            temple.append(loc + sn * 0.012)
        add_tube(part, lambda i: "PropFrame", temple, [0.005] * len(temple), AXIS_Z, segments=6)
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
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE",
        add_leaf_bones=False, bake_anim=False, use_mesh_modifiers=True)


PREVIEW_SETS = [
    ("Koonmungchi", (0.42, 0.42, 0.44), ("Body", "Koon_")),
    ("Omangi", (0.62, 0.12, 0.12), ("Body", "Oma_")),
    ("Koon_OmaCostume", (0.75, 0.08, 0.06), ("Body", "Koon_", "Fake_HornBand", "Fake_GoatEars")),
    ("Oma_KoonCostume", (0.50, 0.45, 0.45), ("Body", "Oma_", "Fake_ConeNose", "Fake_RaccoonTail", "Fake_XGlasses")),
]


def render_preview(out_dir):
    scene = bpy.context.scene
    parts = [o for o in scene.objects if o.type == "MESH"]
    for i, (name, fur_color, prefixes) in enumerate(PREVIEW_SETS):
        fur = bpy.data.materials.new("Fur_" + name)
        fur.diffuse_color = (*fur_color, 1.0)
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

    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.render.resolution_x, scene.render.resolution_y = 1800, 640
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
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview")
    args = parser.parse_args(argv)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    build_body()
    build_koon()
    build_oma()
    build_fakes()

    for obj in bpy.context.scene.objects:
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        print(f"[visitors] {obj.name}: {tris} tris")
    export_fbx(os.path.abspath(args.out))
    print(f"[visitors] exported {args.out}")
    if args.preview:
        render_preview(os.path.abspath(args.preview))


if __name__ == "__main__":
    main()
