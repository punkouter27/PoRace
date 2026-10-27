"""Matt: photoscanned human racer, cleaned and rigged headless (roster stub, not raceable until a biped policy graduates):

  blender -b --factory-startup -P tools/blender/matt_rig.py [-- --check <dir>]

Source Assets/Art/Raw/Matt.glb is one static mesh (A-pose, no armature, unit-scaled to 0.78 m, standing on a scanned
floor mat, a phone in the left hand). This script
  - welds the UV-seam splits, deletes the mat (floor-level faces that are grey or outside the two footprints) and
    caps the soles. The phone stays: the scan has no palm under it, and every cut-and-cap left a worse hand,
  - scales to Matt's real height (1.89 m), feet on z = 0, pelvis over the origin, facing -Y (Unity +Z),
  - builds a Unity Humanoid armature (Mecanim bone names, so the avatar maps itself) from landmarks measured on the
    scan, with feet and hands located from the mesh itself,
  - skins with Blender's bone-heat weights; any vertex heat leaves unweighted goes to its nearest bone, then 4
    influences max, normalised.
Outputs: Assets/Art/Raw/matt.blend (source) and Assets/Art/Exports/GLB/matt.fbx plus matt_albedo.png,
matt_normal.png and matt_metal_smooth.png (URP Lit layout: metallic in R, smoothness in A), all 2048 px.
--check <dir> also renders posed front/side views (arms raised to a T, a stride, a crouch) to <dir> for inspection.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "Assets", "Art", "Raw")
EXP = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB")
SRC = os.path.join(RAW, "Matt.glb")
HEIGHT = 1.89     # m, Matt's real height
TEX = 2048
# Scan-space landmarks (metres in the 0.78 m scan, z up, facing -Y), read off horizontal slices of the mesh.
CX, CY = 0.025, 0.02     # body centreline
FLOOR = 0.035            # the mat lies below this
TOP = 0.782              # crown of the head


def skin_like(rgb):
  r, g, b = rgb
  return r > 0.22 and r - b > 0.06 and r >= g


# Footprints as heel -> toe capsules (scan x, y), read off a top-down render of the scan; the feet toe out.
FEET = (((-0.0525, 0.075), (-0.080, -0.0375)), ((0.105, 0.075), (0.1375, -0.040)))
FOOT_R = 0.028


def in_foot(c):
  for (hx, hy), (tx, ty) in FEET:
    ax, ay = tx - hx, ty - hy
    t = max(0.0, min(1.0, ((c.x - hx) * ax + (c.y - hy) * ay) / (ax * ax + ay * ay)))
    if math.hypot(c.x - hx - ax * t, c.y - hy - ay * t) < FOOT_R:
      return True
  return False


def sample(img_px, w, h, uv):
  x = min(w - 1, max(0, int(uv[0] * w)))
  y = min(h - 1, max(0, int(uv[1] * h)))
  return img_px[y, x, :3]


def clean(ob):
  """Weld seams, drop the mat, cap the soles. Returns the number of deleted faces and dropped islands."""
  img = next(n.image for n in ob.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and "normal" not in n.image.name
             and "metallic" not in n.image.name)
  w, h = img.size
  px = np.empty(w * h * 4, dtype=np.float32)
  img.pixels.foreach_get(px)
  px = px.reshape(h, w, 4)
  bm = bmesh.new()
  bm.from_mesh(ob.data)
  bm.transform(ob.matrix_world)
  bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
  uv = bm.loops.layers.uv.active
  dead = []
  for f in bm.faces:
    c = f.calc_center_median()
    col = sample(px, w, h, sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops))
    top = max(v.co.z for v in f.verts)
    if (top < FLOOR + 0.02 and not skin_like(col)) or (top < FLOOR and not in_foot(c)):
      dead.append(f)                                      # mat: grey, or skin-tinted but outside the feet
  bmesh.ops.delete(bm, geom=dead, context="FACES")
  bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
  # keep only the body: drop islands left over from the mat
  bm.verts.ensure_lookup_table()
  seen, islands = set(), []
  for v in bm.verts:
    if v.index in seen:
      continue
    stack, comp = [v], []
    seen.add(v.index)
    while stack:
      x = stack.pop()
      comp.append(x)
      for e in x.link_edges:
        y = e.other_vert(x)
        if y.index not in seen:
          seen.add(y.index)
          stack.append(y)
    islands.append(comp)
  islands.sort(key=len, reverse=True)
  bmesh.ops.delete(bm, geom=[v for comp in islands[1:] for v in comp], context="VERTS")
  bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)   # cap the soles
  bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")   # concave caps: Unity drops n-gons it can't split
  ob.matrix_world.identity()
  bm.to_mesh(ob.data)
  bm.free()
  return len(dead), len(islands) - 1


def to_world(p):
  """Scan coordinates -> final metres (feet on z = 0, centreline on the origin)."""
  s = HEIGHT / TOP
  return Vector(((p[0] - CX) * s, (p[1] - CY) * s, p[2] * s))


def normalise(ob):
  s = HEIGHT / TOP
  zmin = min(v.co.z for v in ob.data.vertices)
  for v in ob.data.vertices:
    v.co = Vector(((v.co.x - CX) * s, (v.co.y - CY) * s, (v.co.z - zmin) * s))
  return zmin


def landmarks(ob):
  """Joint heads in final metres. Torso/legs from the scan slices; feet and hands from the mesh."""
  vs = [v.co.copy() for v in ob.data.vertices]
  s = HEIGHT / TOP
  J = {}
  for name, (x, y, z) in {
      "Hips": (0, 0.02, 0.400), "Spine": (0, 0.022, 0.450), "Chest": (0, 0.025, 0.520), "UpperChest": (0, 0.025, 0.580),
      "Neck": (0, 0.020, 0.645), "Head": (0, 0.005, 0.690), "HeadTop": (0, 0.005, TOP)}.items():
    J[name] = to_world((CX + x, y, z))
  for side, sx in (("Left", 1), ("Right", -1)):
    J[side + "Shoulder"] = to_world((CX + sx * 0.020, 0.025, 0.625))
    J[side + "UpperArm"] = to_world((CX + sx * 0.092, 0.025, 0.615))
    J[side + "UpperLeg"] = to_world((CX + sx * 0.055, 0.020, 0.385))
    J[side + "LowerLeg"] = to_world((CX + sx * 0.074, 0.030, 0.215))
    J[side + "Foot"] = to_world((CX + sx * 0.081, 0.060, 0.050))
    # foot: lowest band of this side's leg; heel = max y, toe tip = min y
    sole = [v for v in vs if v.z < 0.03 * s and sx * v.x > 0.02 * s]
    toe_y, heel_y = min(v.y for v in sole), max(v.y for v in sole)
    fx = sum(v.x for v in sole) / len(sole)
    J[side + "Toes"] = Vector((fx, heel_y + 0.72 * (toe_y - heel_y), 0.02 * s))
    J[side + "ToeTip"] = Vector((fx, toe_y, 0.012 * s))
    # hand: fingertips are the lowest points of the arm hanging outside the torso; wrist one hand-length up the arm
    arm = [v for v in vs if sx * v.x > 0.2 * s and v.z > 0.25 * s]
    tip = min(arm, key=lambda v: v.z)
    near = [v for v in arm if (v - tip).length < 0.012 * s]
    tip = sum(near, Vector()) / len(near)
    axis = (tip - J[side + "UpperArm"]).normalized()
    wrist = tip - axis * (0.105 * HEIGHT * 0.8)          # hand length ~0.108 H; tip cluster sits inside the fingers
    J[side + "Hand"] = wrist
    J[side + "HandTip"] = tip
    J[side + "LowerArm"] = J[side + "UpperArm"].lerp(wrist, 0.56)
    J[side + "LowerArm"].y += 0.01 * s                    # elbow sits slightly behind the shoulder-wrist line
  return J


BONES = [  # name, head, tail, parent
  ("Hips", "Hips", "Spine", None), ("Spine", "Spine", "Chest", "Hips"), ("Chest", "Chest", "UpperChest", "Spine"),
  ("UpperChest", "UpperChest", "Neck", "Chest"), ("Neck", "Neck", "Head", "UpperChest"), ("Head", "Head", "HeadTop", "Neck"),
]
for _s in ("Left", "Right"):
  BONES += [
    (_s + "Shoulder", _s + "Shoulder", _s + "UpperArm", "UpperChest"), (_s + "UpperArm", _s + "UpperArm", _s + "LowerArm", _s + "Shoulder"),
    (_s + "LowerArm", _s + "LowerArm", _s + "Hand", _s + "UpperArm"), (_s + "Hand", _s + "Hand", _s + "HandTip", _s + "LowerArm"),
    (_s + "UpperLeg", _s + "UpperLeg", _s + "LowerLeg", "Hips"), (_s + "LowerLeg", _s + "LowerLeg", _s + "Foot", _s + "UpperLeg"),
    (_s + "Foot", _s + "Foot", _s + "Toes", _s + "LowerLeg"), (_s + "Toes", _s + "Toes", _s + "ToeTip", _s + "Foot")]


def armature(J):
  arm = bpy.data.armatures.new("MattRig")
  rig = bpy.data.objects.new("Matt", arm)
  bpy.context.scene.collection.objects.link(rig)
  bpy.context.view_layer.objects.active = rig
  bpy.ops.object.mode_set(mode="EDIT")
  for name, head, tail, parent in BONES:
    b = arm.edit_bones.new(name)
    b.head, b.tail = J[head], J[tail]
    if parent:
      b.parent = arm.edit_bones[parent]
      b.use_connect = name not in ("Spine",) and (b.head - b.parent.tail).length < 1e-4
  for b in arm.edit_bones:      # consistent rolls: local Z toward the front (-Y) for every bone
    b.align_roll(Vector((0, -1, 0)) if abs(b.vector.normalized().y) < 0.9 else Vector((0, 0, 1)))
  bpy.ops.object.mode_set(mode="OBJECT")
  return rig


def skin(ob, rig):
  bpy.ops.object.select_all(action="DESELECT")
  ob.select_set(True)
  rig.select_set(True)
  bpy.context.view_layer.objects.active = rig
  bpy.ops.object.parent_set(type="ARMATURE_AUTO")
  groups = {g.index: g.name for g in ob.vertex_groups}
  segs = {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}
  fixed = 0
  for v in ob.data.vertices:
    if sum(g.weight for g in v.groups) > 1e-3:
      continue
    best, bd = None, 1e9
    for name, (a, b) in segs.items():
      ab = b - a
      t = max(0.0, min(1.0, (v.co - a).dot(ab) / ab.length_squared))
      d = (v.co - (a + ab * t)).length
      if d < bd:
        best, bd = name, d
    ob.vertex_groups[best].add([v.index], 1.0, "REPLACE")
    fixed += 1
  bpy.context.view_layer.objects.active = ob
  bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
  bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
  return fixed, len(groups)


def textures(ob):
  """Glb PBR set -> 2K PNGs in Unity URP Lit layout; the material is repointed at them so the .blend stays small."""
  nodes = ob.data.materials[0].node_tree.nodes
  imgs = {n.image.name: n.image for n in nodes if n.type == "TEX_IMAGE"}
  base = next(i for k, i in imgs.items() if "normal" not in k and "metallic" not in k)
  normal = next(i for k, i in imgs.items() if "normal" in k)
  mr = next(i for k, i in imgs.items() if "metallic" in k)

  def save(img, name, px=None):
    out = bpy.data.images.new(name, TEX, TEX, alpha=True, is_data=name != "matt_albedo")   # Non-Color set here: setting it later clears the pixels
    if px is None:
      img.scale(TEX, TEX)
      px = np.empty(TEX * TEX * 4, dtype=np.float32)
      img.pixels.foreach_get(px)
    out.pixels.foreach_set(px)
    out.filepath_raw = os.path.join(EXP, name + ".png")
    out.file_format = "PNG"
    out.save()
    return out

  albedo = save(base, "matt_albedo")
  nrm = save(normal, "matt_normal")
  mr.scale(TEX, TEX)
  src = np.empty(TEX * TEX * 4, dtype=np.float32)
  mr.pixels.foreach_get(src)
  src = src.reshape(-1, 4)
  ms = np.zeros_like(src)
  ms[:, 0] = ms[:, 1] = ms[:, 2] = src[:, 2]     # glTF: metallic in B, roughness in G
  ms[:, 3] = 1.0 - src[:, 1]
  sm = save(mr, "matt_metal_smooth", ms.reshape(-1))
  for n in nodes:   # .blend shows the exported 2K maps (metal/rough wiring kept on the glb image)
    if n.type == "TEX_IMAGE" and n.image == base:
      n.image = albedo
    elif n.type == "TEX_IMAGE" and n.image == normal:
      n.image = nrm
  for img in (albedo, nrm, sm):
    img.filepath = img.filepath_raw     # made relative by save_as_mainfile
  return base.size[:]


def check(rig, ob, out):
  """Posed renders: T-pose (Unity avatar), stride, crouch, front and side."""
  sc = bpy.context.scene
  sc.render.engine = "BLENDER_WORKBENCH"
  sc.display.shading.color_type = "TEXTURE"
  sc.display.shading.light = "STUDIO"
  sc.render.resolution_x = sc.render.resolution_y = 640
  cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
  sc.collection.objects.link(cam)
  sc.camera = cam
  cam.data.type = "ORTHO"
  cam.data.ortho_scale = 2.3
  pb = rig.pose.bones
  for b in pb:
    b.rotation_mode = "XYZ"

  def pose(**rot):
    for b in pb:
      b.rotation_euler = (0, 0, 0)
    for k, v in rot.items():
      pb[k].rotation_euler = [math.radians(a) for a in v]
    bpy.context.view_layer.update()

  # bone-local axes: X across the body plane (roll put Z to the front), so a rotation about local Z swings an arm
  # in the frontal plane and one about local X swings a leg fore/aft.
  poses = {
    "rest": {},
    "tpose": {"LeftUpperArm": (0, 0, 52), "RightUpperArm": (0, 0, -52), "LeftLowerArm": (0, 0, 8), "RightLowerArm": (0, 0, -8)},
    "stride": {"LeftUpperLeg": (-35, 0, 0), "LeftLowerLeg": (40, 0, 0), "RightUpperLeg": (25, 0, 0), "RightLowerLeg": (10, 0, 0),
               "LeftUpperArm": (30, 0, 0), "RightUpperArm": (-30, 0, 0), "LeftLowerArm": (-45, 0, 0), "RightLowerArm": (-45, 0, 0),
               "Head": (0, 25, 0)},
    "crouch": {"LeftUpperLeg": (-70, 0, 0), "RightUpperLeg": (-70, 0, 0), "LeftLowerLeg": (110, 0, 0), "RightLowerLeg": (110, 0, 0),
               "LeftFoot": (-40, 0, 0), "RightFoot": (-40, 0, 0), "Spine": (-25, 0, 0)},
  }
  for name, rot in poses.items():
    pose(**rot)
    for view, (loc, eul, size) in {"front": ((0, -4, 0.95), (90, 0, 0), 2.3), "side": ((4, 0, 0.95), (90, 0, 90), 2.3),
                                   "feet": ((2, -3.4, 1.2), (72, 0, 30), 0.9)}.items():
      cam.data.ortho_scale = size
      cam.location = loc
      cam.rotation_euler = [math.radians(a) for a in eul]
      sc.render.filepath = os.path.join(out, f"matt_{name}_{view}.png")
      bpy.ops.render.render(write_still=True)
  pose()


def main():
  argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
  check_dir = argv[argv.index("--check") + 1] if "--check" in argv else None
  bpy.ops.wm.read_factory_settings(use_empty=True)
  os.makedirs(EXP, exist_ok=True)
  bpy.ops.import_scene.gltf(filepath=SRC)
  ob = next(o for o in bpy.data.objects if o.type == "MESH")
  ob.name = ob.data.name = "MattMesh"
  for o in [o for o in bpy.data.objects if o is not ob]:
    bpy.data.objects.remove(o)
  dead, islands = clean(ob)
  zmin = normalise(ob)
  bpy.context.view_layer.objects.active = ob
  ob.select_set(True)
  bpy.ops.object.shade_smooth()
  J = landmarks(ob)
  rig = armature(J)
  fixed, groups = skin(ob, rig)
  src_px = textures(ob)
  bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RAW, "matt.blend"))

  bpy.ops.object.select_all(action="DESELECT")
  ob.select_set(True)
  rig.select_set(True)
  bpy.ops.export_scene.fbx(filepath=os.path.join(EXP, "matt.fbx"), use_selection=True, apply_unit_scale=True,
                           apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", object_types={"MESH", "ARMATURE"},
                           add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X", mesh_smooth_type="FACE",
                           use_armature_deform_only=True, bake_anim=False, path_mode="STRIP")
  tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
  dims = [max(v.co[i] for v in ob.data.vertices) - min(v.co[i] for v in ob.data.vertices) for i in range(3)]
  print(f"[matt] removed {dead} mat faces + {islands} islands, floor offset {zmin:.4f}; {len(ob.data.vertices)} verts, "
        f"{tris} tris, size {dims[0]:.2f} x {dims[1]:.2f} x {dims[2]:.2f} m; {len(rig.data.bones)} bones, "
        f"{groups} groups, {fixed} verts nearest-bone; textures {src_px[0]} -> {TEX}")
  if check_dir:
    check(rig, ob, check_dir)


main()
