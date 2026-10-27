"""Matt, game-ready look (stage 2, after matt_rig.py), headless and CPU only:

  blender -b Assets/Art/Raw/matt.blend -P tools/blender/matt_look.py [-- --check <dir>]

matt.blend (the cleaned, rigged scan) stays untouched: it is the pinned source of Matt's MuJoCo body
(tools/blender/matt_mjcf.py fits the collision primitives to its skin). This stage builds the game asset from it and
saves Assets/Art/Raw/matt_game.blend:
  - topology: voxel remesh (watertight: the scan's holes and non-manifold edges go) -> QuadriFlow quads -> shrinkwrap
    back onto the scan; Smart UV Project, uniform texel density, 4 px island margin at 2048
  - bakes (Cycles, CPU) scan -> game mesh: albedo, tangent-space normal, roughness, metallic; ambient occlusion on
    the game mesh itself
  - rig: the scan rig plus Root (origin, UE needs it), twist bones (upper arm, forearm, thigh: deform, children of
    their segment, so anything that poses the segment carries them) and Belly (spring bone for the sweatshirt hem);
    bone-heat skin, 4 influences, normalised
  - shape key "Breathe": chest and belly out along the normal (<= 7 mm)
  - props: a modelled phone on the LeftHand bone (covers the scanned one, which has no palm under it) and a race bib
    (roster colour band, number 02) skinned to the chest
  - textures (2048 unless noted) in Assets/Art/Exports/GLB/Matt/Textures: albedo, normal, metal_smooth (URP: R metal,
    A smoothness), occlusion, orm (UE: R AO, G roughness, B metal), grime (RGBA dirt, alpha = feet/shin mask for the
    wear overlay), detail_mask (A = sweatshirt), knit_normal (512, tileable), bib (512)
  - "Walk" clip (tools/blender/matt_walk.py, root motion on Root)
  - exports Matt/matt.fbx (-Z forward, Y up, deform bones, tangent space, shape keys, Walk) and Matt/matt.glb (+Y up,
    tangents, morph targets, skin, Walk)
--check <dir>: Cycles CPU stills of the result (rest, walk mid-stride, close-ups) for inspection.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import matt_walk  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "Assets", "Art", "Raw")
EXP = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB", "Matt")
TEXD = os.path.join(EXP, "Textures")
TEX = 2048
QUADS = 14000
ROSTER = (1.0, 0.62, 0.18)       # MattBuilder.RosterColor
NUMBER = "02"                    # Creature02_Matt


def log(*a):
  print("[look]", *a, flush=True)


def select_only(*obs, active=None):
  bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
  bpy.ops.object.select_all(action="DESELECT")
  for o in obs:
    o.select_set(True)
  bpy.context.view_layer.objects.active = active or obs[0]


# --- topology ---------------------------------------------------------------------------------------------------

def retopo(scan):
  me = scan.data.copy()
  ob = bpy.data.objects.new("MattMesh", me)
  bpy.context.scene.collection.objects.link(ob)
  ob.vertex_groups.clear()
  if me.shape_keys:
    ob.shape_key_clear()
  select_only(ob)
  me.remesh_voxel_size = 0.006
  bpy.ops.object.voxel_remesh()
  # QuadriFlow needs one manifold, consistently oriented shell: the voxel pass of the scan's fused fingers and soles
  # leaves stray shells and non-manifold edges / vertices. Keep the largest shell, cut the bad spots out and cap,
  # until none are left.
  bm = bmesh.new(); bm.from_mesh(ob.data)
  for it in range(6):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.verts.ensure_lookup_table()
    seen, shells = set(), []
    for v in bm.verts:
      if v.index in seen:
        continue
      stack, comp = [v], []
      seen.add(v.index)
      while stack:
        x = stack.pop(); comp.append(x)
        for e in x.link_edges:
          y = e.other_vert(x)
          if y.index not in seen:
            seen.add(y.index); stack.append(y)
      shells.append(comp)
    shells.sort(key=len, reverse=True)
    if len(shells) > 1:
      bmesh.ops.delete(bm, geom=[v for s in shells[1:] for v in s], context="VERTS")
    bad = {f for e in bm.edges if not e.is_manifold and not e.is_boundary for f in e.link_faces}
    bad |= {f for v in bm.verts if not v.is_manifold and not v.is_boundary for f in v.link_faces}
    if bad:
      bmesh.ops.delete(bm, geom=list(bad), context="FACES")
      bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    if not bad and len(shells) == 1 and all(e.is_manifold for e in bm.edges) and all(v.is_manifold for v in bm.verts):
      break
  bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
  log(f"cleanup: {it + 1} passes, manifold {all(e.is_manifold for e in bm.edges) and all(v.is_manifold for v in bm.verts)}")
  bm.to_mesh(ob.data); bm.free()
  v_voxel = len(ob.data.polygons)
  try:
    bpy.ops.object.quadriflow_remesh(target_faces=QUADS, use_mesh_symmetry=False, use_preserve_sharp=False,
                                     use_preserve_boundary=False, smooth_normals=False, mode="FACES", seed=0)
  except RuntimeError as e:
    log("quadriflow error:", e)
  if len(ob.data.polygons) > 2 * QUADS:               # QuadriFlow refused (it can return FINISHED without remeshing)
    # all-quad fallback: a coarse voxel remesh sized for the face budget (uniform quads, no edge-flow alignment)
    me.remesh_voxel_size = 0.006 * math.sqrt(v_voxel / QUADS)
    log(f"quadriflow did not remesh; coarse voxel quads at {me.remesh_voxel_size * 1000:.1f} mm instead")
    bpy.ops.object.voxel_remesh()
  sw = ob.modifiers.new("onto_scan", "SHRINKWRAP"); sw.target = scan; sw.wrap_method = "NEAREST_SURFACEPOINT"
  bpy.ops.object.modifier_apply(modifier=sw.name)
  sm = ob.modifiers.new("relax", "CORRECTIVE_SMOOTH"); sm.iterations = 3; sm.use_only_smooth = True
  bpy.ops.object.modifier_apply(modifier=sm.name)
  quads = sum(1 for p in ob.data.polygons if len(p.vertices) == 4)
  log(f"retopo: voxel {v_voxel} faces -> {len(ob.data.polygons)} faces ({100 * quads / len(ob.data.polygons):.0f} % quads)")
  # UVs
  bpy.ops.object.mode_set(mode="EDIT")
  bpy.ops.mesh.select_all(action="SELECT")
  margin = 4.0 / TEX
  bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0, correct_aspect=True)
  bpy.ops.uv.pack_islands(margin=margin)
  bpy.ops.object.mode_set(mode="OBJECT")
  for p in ob.data.polygons:
    p.use_smooth = True
  ob.data.materials.clear()
  return ob


# --- bakes ------------------------------------------------------------------------------------------------------

def new_image(name, data=False, alpha=False):
  img = bpy.data.images.new(name, TEX, TEX, alpha=alpha, is_data=data)
  return img


def game_material(ob):
  m = bpy.data.materials.new("Matt")
  m.use_nodes = True
  ob.data.materials.append(m)
  return m


def bake(scan, ob, kind, img, selected=True, samples=1, prep=None):
  sc = bpy.context.scene
  sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = samples
  nt = ob.data.materials[0].node_tree
  node = nt.nodes.get("bake_target") or nt.nodes.new("ShaderNodeTexImage")
  node.name = "bake_target"; node.image = img
  nt.nodes.active = node
  undo = prep() if prep else None
  if selected:
    select_only(scan, ob, active=ob)
  else:
    select_only(ob)
  bk = sc.render.bake
  # 17 mm voxel quads sit up to ~2 cm off the scan under the chin and in the armpits: a 3 cm cage reaches them
  bk.use_selected_to_active = selected; bk.cage_extrusion = 0.03; bk.max_ray_distance = 0.08; bk.margin = 8
  kw = {}
  if kind == "DIFFUSE":
    bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
    kw = dict(pass_filter={"COLOR"})
  bpy.ops.object.bake(type=kind, use_selected_to_active=selected, **kw)
  if undo:
    undo()
  return img


def emit_from(scan, socket_name):
  """Route the scan's input `socket_name` (its linked source) into emission for an EMIT bake; returns the restore."""
  nt = scan.data.materials[0].node_tree
  bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
  src = bsdf.inputs[socket_name].links[0].from_socket if bsdf.inputs[socket_name].is_linked else None
  out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
  em = nt.nodes.new("ShaderNodeEmission")
  if src is not None:
    nt.links.new(src, em.inputs["Color"])
  else:
    v = bsdf.inputs[socket_name].default_value
    em.inputs["Color"].default_value = (v, v, v, 1)
  old = out.inputs["Surface"].links[0].from_socket
  nt.links.new(em.outputs["Emission"], out.inputs["Surface"])

  def undo():
    nt.links.new(old, out.inputs["Surface"]); nt.nodes.remove(em)
  return undo


def procedural_emit(ob, build):
  """Temporarily make `ob` emit a procedural (build(nt) -> socket) for an EMIT bake; returns the restore."""
  nt = ob.data.materials[0].node_tree
  out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
  old = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].is_linked else None
  added_before = set(nt.nodes)
  em = nt.nodes.new("ShaderNodeEmission")
  nt.links.new(build(nt), em.inputs["Color"])
  nt.links.new(em.outputs["Emission"], out.inputs["Surface"])

  def undo():
    for n in set(nt.nodes) - added_before:
      nt.nodes.remove(n)
    if old is not None:
      nt.links.new(old, out.inputs["Surface"])
  return undo


def pixels(img):
  a = np.empty(img.size[0] * img.size[1] * 4, np.float32)
  img.pixels.foreach_get(a)
  return a.reshape(img.size[1], img.size[0], 4)


def save(name, arr, data=True, size=None):
  h, w = arr.shape[:2]
  img = bpy.data.images.new(name, w, h, alpha=True, is_data=data)
  img.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
  img.filepath_raw = os.path.join(TEXD, name + ".png"); img.file_format = "PNG"; img.save()
  return img


def bakes(scan, ob):
  albedo = bake(scan, ob, "DIFFUSE", new_image("bake_albedo"))
  normal = bake(scan, ob, "NORMAL", new_image("bake_normal", data=True))
  rough = bake(scan, ob, "ROUGHNESS", new_image("bake_rough", data=True))
  metal = bake(scan, ob, "EMIT", new_image("bake_metal", data=True), prep=lambda: emit_from(scan, "Metallic"))
  ao = bake(scan, ob, "AO", new_image("bake_ao", data=True), selected=False, samples=32)
  A, N, R, Me, O = (pixels(i) for i in (albedo, normal, rough, metal, ao))
  log(f"bakes: albedo mean {A[..., :3].mean():.3f}, roughness mean {R[..., 0].mean():.3f}, metal mean {Me[..., 0].mean():.3f}, AO mean {O[..., 0].mean():.3f}")
  one = np.ones_like(A[..., 0])
  out = {}
  out["albedo"] = save("matt_albedo", np.dstack([A[..., 0], A[..., 1], A[..., 2], one]), data=False)
  out["normal"] = save("matt_normal", np.dstack([N[..., 0], N[..., 1], N[..., 2], one]))
  out["metal_smooth"] = save("matt_metal_smooth", np.dstack([Me[..., 0], Me[..., 0], Me[..., 0], 1 - R[..., 0]]))
  out["occlusion"] = save("matt_occlusion", np.dstack([O[..., 0], O[..., 0], O[..., 0], one]))
  out["orm"] = save("matt_orm", np.dstack([O[..., 0], R[..., 0], Me[..., 0], one]))
  # sweatshirt (dark navy) -> detail mask in A, for the tiling knit normal (URP Lit detail inputs read the mask's A)
  r, g, b = A[..., 0], A[..., 1], A[..., 2]
  navy = ((b > r + 0.02) & (np.maximum(np.maximum(r, g), b) < 0.4)).astype(np.float32)
  out["detail_mask"] = save("matt_detail_mask", np.dstack([navy, navy, navy, navy]))
  # grime: dirt colour with a feet/shin mask (object-space height) broken up by noise, baked on the game mesh
  def height_mask(nt):
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ"); nt.links.new(geo.outputs["Position"], sep.inputs[0])
    ramp = nt.nodes.new("ShaderNodeMapRange"); nt.links.new(sep.outputs["Z"], ramp.inputs["Value"])
    ramp.inputs["From Min"].default_value = 0.55; ramp.inputs["From Max"].default_value = 0.0
    noise = nt.nodes.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 18.0; noise.inputs["Detail"].default_value = 6.0
    nt.links.new(geo.outputs["Position"], noise.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.use_clamp = True
    nt.links.new(ramp.outputs["Result"], mul.inputs[0])
    sharp = nt.nodes.new("ShaderNodeMapRange"); nt.links.new(noise.outputs["Fac"], sharp.inputs["Value"])
    sharp.inputs["From Min"].default_value = 0.35; sharp.inputs["From Max"].default_value = 0.65
    nt.links.new(sharp.outputs["Result"], mul.inputs[1])
    return mul.outputs["Value"]
  mask = pixels(bake(scan, ob, "EMIT", new_image("bake_grime", data=True), selected=False, prep=lambda: procedural_emit(ob, height_mask)))[..., 0]
  rng = np.random.default_rng(2)
  speck = 0.85 + 0.3 * rng.random(mask.shape, dtype=np.float32)
  out["grime"] = save("matt_grime", np.dstack([0.30 * speck, 0.24 * speck, 0.18 * speck, np.clip(mask, 0, 1)]), data=False)
  # knit: vertical ribs with stitch rows, tileable 512 -> tangent-space normal
  n = 512
  u, v = np.meshgrid(np.arange(n) / n, np.arange(n) / n)
  hgt = 0.5 + 0.5 * np.sin(2 * np.pi * 32 * u) * (0.75 + 0.25 * np.sin(2 * np.pi * 96 * v))
  gy, gx = np.gradient(hgt)
  k = 6.0
  nx, ny, nz = -gx * k, -gy * k, np.ones_like(hgt)
  ln = np.sqrt(nx * nx + ny * ny + nz * nz)
  out["knit_normal"] = save("matt_knit_normal", np.dstack([0.5 + 0.5 * nx / ln, 0.5 + 0.5 * ny / ln, 0.5 + 0.5 * nz / ln, np.ones_like(hgt)]))
  # the .blend shows the baked look
  nt = ob.data.materials[0].node_tree
  bsdf = next(nd for nd in nt.nodes if nd.type == "BSDF_PRINCIPLED")
  ti = nt.nodes.new("ShaderNodeTexImage"); ti.image = out["albedo"]; nt.links.new(ti.outputs["Color"], bsdf.inputs["Base Color"])
  tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = out["normal"]
  nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
  bsdf.inputs["Roughness"].default_value = float(R[..., 0].mean())
  nt.nodes.remove(nt.nodes["bake_target"])
  for i in (albedo, normal, rough, metal, ao):
    bpy.data.images.remove(i)
  return out


# --- rig ----------------------------------------------------------------------------------------------------------

def game_rig(rig, ob, scan):
  """Scan rig + Root, twist and Belly bones; skin transferred from the scan."""
  select_only(rig)
  bpy.ops.object.mode_set(mode="EDIT")
  eb = rig.data.edit_bones
  root = eb.new("Root"); root.head = (0, 0, 0); root.tail = (0, 0, 0.2); root.roll = 0
  eb["Hips"].parent = root
  for side in ("Left", "Right"):
    for seg, nxt in (("UpperArm", "LowerArm"), ("LowerArm", "Hand"), ("UpperLeg", "LowerLeg")):
      b = eb[side + seg]
      t = eb.new(side + seg + "Twist")
      t.head = b.head.lerp(b.tail, 0.5); t.tail = b.tail; t.roll = b.roll; t.parent = b
  spine, chest = eb["Spine"], eb["Chest"]
  belly = eb.new("Belly")
  front = min((v.co for v in ob.data.vertices if abs(v.co.x) < 0.08 and spine.head.z < v.co.z < chest.head.z), key=lambda c: c.y)
  belly.head = Vector((0, front.y + 0.07, (spine.head.z + chest.head.z) / 2)); belly.tail = belly.head + Vector((0, -0.08, 0))
  belly.parent = spine
  bpy.ops.object.mode_set(mode="OBJECT")
  # Weights: the scan's verified bone-heat skin (matt_rig.py), transferred nearest-face interpolated. Bone heat on the
  # dense remeshed surface fails to converge for most bones, the transfer cannot.
  dt = ob.modifiers.new("weights", "DATA_TRANSFER"); dt.object = scan; dt.use_vert_data = True
  dt.data_types_verts = {"VGROUP_WEIGHTS"}; dt.vert_mapping = "POLYINTERP_NEAREST"
  select_only(ob)
  bpy.ops.object.datalayout_transfer(modifier=dt.name)
  bpy.ops.object.modifier_apply(modifier=dt.name)
  for n in ("Root", "Belly"):
    if n not in ob.vertex_groups:
      ob.vertex_groups.new(name=n)
  # Twist bones take the distal part of their segment's weight: 0 at 30 % of the bone, 60 % from 90 % on.
  for side in ("Left", "Right"):
    for seg in ("UpperArm", "LowerArm", "UpperLeg"):
      bone = rig.data.bones[side + seg]; a, b = bone.head_local, bone.tail_local; ab = b - a
      src, tw = ob.vertex_groups[side + seg], ob.vertex_groups.new(name=side + seg + "Twist")
      for v in ob.data.vertices:
        w = next((g.weight for g in v.groups if g.group == src.index), 0.0)
        if w <= 0.0:
          continue
        t = max(0.0, min(1.0, (v.co - a).dot(ab) / ab.length_squared))
        s = 0.6 * max(0.0, min(1.0, (t - 0.3) / 0.6)) ** 2
        src.add([v.index], w * (1 - s), "REPLACE"); tw.add([v.index], w * s, "REPLACE")
  arm = ob.modifiers.new("Armature", "ARMATURE"); arm.object = rig
  ob.parent = rig
  # Belly: soft falloff around the belly front, max 0.5, so the hem can sway (spring in Unity)
  g = ob.vertex_groups["Belly"]
  bh = rig.matrix_world @ rig.data.bones["Belly"].head_local
  for v in ob.data.vertices:
    d = (v.co - bh).length
    if d < 0.16 and v.co.y < bh.y + 0.02:
      g.add([v.index], 0.5 * (1 - d / 0.16) ** 2, "REPLACE")
  select_only(ob)
  bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
  bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
  unweighted = sum(1 for v in ob.data.vertices if sum(w.weight for w in v.groups) < 1e-3)
  log(f"rig: {len(rig.data.bones)} bones (Root, 6 twist, Belly added); unweighted verts {unweighted} of {len(ob.data.vertices)}")
  # forearm twist takes half the hand's roll (authoring in Blender; exported animation bakes it). The upper-arm and
  # thigh twist bones split their segment's weights and ride on it.
  for side in ("Left", "Right"):
    for seg, nxt in (("LowerArm", "Hand"),):
      c = rig.pose.bones[side + seg + "Twist"].constraints.new("COPY_ROTATION")
      c.target = rig; c.subtarget = side + nxt; c.use_x = c.use_z = False; c.mix_mode = "ADD"; c.target_space = c.owner_space = "LOCAL"
      c.influence = 0.5


def breathe(ob, rig):
  ob.shape_key_add(name="Basis")
  k = ob.shape_key_add(name="Breathe")
  chest = rig.matrix_world @ rig.data.bones["Chest"].head_local
  centre = Vector((0, chest.y - 0.08, chest.z + 0.05))
  moved = 0
  for v in ob.data.vertices:
    d = (v.co - centre).length
    if d < 0.30 and v.normal.y < 0.2:            # front and sides of chest and belly, not the back
      k.data[v.index].co = v.co + v.normal * 0.007 * (1 - d / 0.30)
      moved += 1
  log(f"shape key Breathe: {moved} verts")


# --- props --------------------------------------------------------------------------------------------------------

def cut_scanned_phone(ob, rig, albedo):
  """The scanned phone is fused with the fingers (no palm under it): cut its bright, colourless faces out of the game
  mesh near the left hand and cap the hole. The cap sits inside the modelled phone."""
  A = pixels(albedo); h, w = A.shape[:2]
  hand = rig.matrix_world @ rig.data.bones["LeftHand"].head_local
  bm = bmesh.new(); bm.from_mesh(ob.data); uv = bm.loops.layers.uv.active
  dead = []
  for f in bm.faces:
    c = f.calc_center_median()
    if (c - hand).length > 0.25:
      continue
    u = sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
    col = A[min(h - 1, max(0, int(u.y * h))), min(w - 1, max(0, int(u.x * w))), :3]
    if col.min() > 0.55 and col.max() - col.min() < 0.08:
      dead.append(f)
  bmesh.ops.delete(bm, geom=dead, context="FACES")
  bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
  caps = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)["faces"]
  bmesh.ops.triangulate(bm, faces=[f for f in caps if len(f.verts) > 4])
  bm.to_mesh(ob.data); bm.free()
  log(f"scanned phone: {len(dead)} faces cut, {len(caps)} cap faces")


def phone(scan, rig):
  """Rounded slab where the scanned phone is (its bright, colourless faces at the left hand), parented to LeftHand."""
  img = next(n.image for n in scan.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and "albedo" in n.image.name)
  px = pixels(img); h, w = px.shape[:2]
  uv = scan.data.uv_layers.active.data
  hand = rig.matrix_world @ rig.data.bones["LeftHand"].head_local
  pts = []
  for p in scan.data.polygons:
    c = scan.matrix_world @ p.center
    if (c - hand).length > 0.25:
      continue
    u = sum((uv[li].uv for li in p.loop_indices), Vector((0, 0))) / len(p.loop_indices)
    col = px[min(h - 1, int(u.y * h)), min(w - 1, int(u.x * w)), :3]
    if col.min() > 0.55 and col.max() - col.min() < 0.08:
      pts.append(c)
  if len(pts) < 10:
    log(f"phone: only {len(pts)} phone-coloured faces near the left hand, prop skipped"); return None
  P = np.array([tuple(c) for c in pts]); mid = P.mean(0)
  _, _, axes = np.linalg.svd(P - mid)
  if np.linalg.det(axes) < 0:
    axes[2] *= -1                                       # a rotation, not a reflection
  loc = (P - mid) @ axes.T
  ext = loc.max(0) - loc.min(0)
  # a phone (~15 x 7 x 0.9 cm), a little proud of the scan; the scanned shape is fused with the fingers, so its
  # extents are clamped to phone proportions
  size = np.clip(ext, [0.13, 0.065, 0.009], [0.16, 0.078, 0.012])[:3] + 0.004
  bpy.ops.mesh.primitive_cube_add(size=1.0)
  ph = bpy.context.active_object; ph.name = "MattPhone"
  ph.scale = tuple(size)
  R = Matrix([list(axes[0]), list(axes[1]), list(axes[2])]).transposed()
  ph.matrix_world = Matrix.Translation(Vector(mid) + Vector(tuple(axes.T @ ((loc.max(0) + loc.min(0)) / 2)))) @ R.to_4x4() @ Matrix.Diagonal((*size, 1))
  bv = ph.modifiers.new("round", "BEVEL"); bv.width = 0.003; bv.segments = 3
  select_only(ph); bpy.ops.object.modifier_apply(modifier="round"); bpy.ops.object.transform_apply(scale=True)
  m = bpy.data.materials.new("MattPhone"); m.use_nodes = True
  b = m.node_tree.nodes["Principled BSDF"]; b.inputs["Base Color"].default_value = (0.03, 0.03, 0.035, 1); b.inputs["Roughness"].default_value = 0.25
  ph.data.materials.append(m)
  for p in ph.data.polygons:
    p.use_smooth = True
  mw = ph.matrix_world.copy()
  ph.parent = rig; ph.parent_type = "BONE"; ph.parent_bone = "LeftHand"
  ph.matrix_world = mw
  log(f"phone: {len(pts)} scan faces -> slab {size[0] * 100:.1f} x {size[1] * 100:.1f} x {size[2] * 100:.1f} cm on LeftHand")
  return ph


SEG = {"0": "abcdef", "1": "bc", "2": "abged", "3": "abgcd", "4": "fgbc", "5": "afgcd", "6": "afgedc", "7": "abc", "8": "abcdefg", "9": "abcfgd"}


def bib_texture():
  n = 512
  img = np.ones((n, n, 4), np.float32)
  img[int(n * 0.84):, :, :3] = ROSTER                          # roster band along the top (row 0 = bottom in Blender)
  img[:int(n * 0.03), :, :3] = 0.85; img[:, :int(n * 0.03), :3] = 0.85; img[:, -int(n * 0.03):, :3] = 0.85
  t = 26                                                       # stroke
  for k, ch in enumerate(NUMBER):
    x0, y0, w, h = 110 + k * 160, 90, 130, 260
    seg = {"a": (x0, y0 + h - t, w, t), "g": (x0, y0 + h // 2 - t // 2, w, t), "d": (x0, y0, w, t),
           "f": (x0, y0 + h // 2, t, h // 2), "b": (x0 + w - t, y0 + h // 2, t, h // 2),
           "e": (x0, y0, t, h // 2), "c": (x0 + w - t, y0, t, h // 2)}
    for s in SEG[ch]:
      x, y, ww, hh = seg[s]
      img[y:y + hh, x:x + ww, :3] = 0.05
  return save("matt_bib", img, data=False)


def bib(ob, rig, tex):
  chest = rig.matrix_world @ rig.data.bones["Chest"].head_local
  z = chest.z + 0.04
  front = min((v.co for v in ob.data.vertices if abs(v.co.x) < 0.1 and abs(v.co.z - z) < 0.03), key=lambda c: c.y)
  bpy.ops.mesh.primitive_grid_add(x_subdivisions=10, y_subdivisions=10, size=1.0)
  b = bpy.context.active_object; b.name = "MattBib"
  b.scale = (0.22, 0.17, 1); b.rotation_euler = (math.radians(90), 0, 0)
  b.location = (0, front.y - 0.03, z)
  select_only(b); bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
  sw = b.modifiers.new("onto_chest", "SHRINKWRAP"); sw.target = ob; sw.wrap_method = "PROJECT"; sw.use_project_y = True
  sw.use_negative_direction = True; sw.use_positive_direction = True; sw.offset = 0.004
  bpy.ops.object.modifier_apply(modifier=sw.name)
  m = bpy.data.materials.new("MattBib"); m.use_nodes = True
  ti = m.node_tree.nodes.new("ShaderNodeTexImage"); ti.image = tex
  m.node_tree.links.new(ti.outputs["Color"], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
  m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.8
  b.data.materials.append(m)
  # skin: copy the body's weights (nearest face, interpolated)
  dt = b.modifiers.new("weights", "DATA_TRANSFER"); dt.object = ob; dt.use_vert_data = True
  dt.data_types_verts = {"VGROUP_WEIGHTS"}; dt.vert_mapping = "POLYINTERP_NEAREST"
  select_only(b)
  bpy.ops.object.datalayout_transfer(modifier=dt.name)
  bpy.ops.object.modifier_apply(modifier=dt.name)
  arm = b.modifiers.new("Armature", "ARMATURE"); arm.object = rig
  b.parent = rig
  bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=4)
  bpy.ops.object.vertex_group_normalize_all(group_select_mode="ALL", lock_active=False)
  log(f"bib: {len(b.data.vertices)} verts at chest z {z:.2f}, {len(b.vertex_groups)} weight groups")
  return b


# --- export -------------------------------------------------------------------------------------------------------

def export(rig, objs):
  select_only(rig, *objs, active=rig)
  bpy.ops.export_scene.fbx(filepath=os.path.join(EXP, "matt.fbx"), use_selection=True, apply_unit_scale=True,
                           apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                           object_types={"MESH", "ARMATURE"}, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                           mesh_smooth_type="FACE", use_tspace=True, use_mesh_modifiers=True, use_armature_deform_only=True,
                           bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                           bake_anim_force_startend_keying=True, path_mode="STRIP")
  bpy.ops.export_scene.gltf(filepath=os.path.join(EXP, "matt.glb"), export_format="GLB", use_selection=True,
                            export_yup=True, export_tangents=True, export_normals=True, export_texcoords=True,
                            export_materials="EXPORT", export_skins=True, export_morph=True, export_animations=True,
                            export_def_bones=True, export_image_format="AUTO")


def check(rig, objs, out):
  sc = bpy.context.scene
  sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 16
  sc.render.resolution_x, sc.render.resolution_y = 360, 540
  sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); sun.data.energy = 3.0
  sun.rotation_euler = (math.radians(50), 0, math.radians(-30)); sc.collection.objects.link(sun)
  fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN")); fill.data.energy = 1.0   # 3:1 key / fill
  fill.rotation_euler = (math.radians(60), 0, math.radians(150)); sc.collection.objects.link(fill)
  w = bpy.data.worlds.new("W"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.65, 0.72, 1)
  sc.world = w
  cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
  shots = {"rest_front": (1, (0, -4.2, 1.0), (90, 0, 0), 50), "walk_side": (12, (4.2, -0.6, 1.0), (90, 0, 90), 50),
           "chest": (1, (0, -1.6, 1.35), (90, 0, 0), 85), "hand": (1, (0.9, -1.4, 0.95), (90, 0, 30), 120)}
  for name, (frame, loc, rot, lens) in shots.items():
    sc.frame_set(frame)
    cam.location = loc; cam.rotation_euler = [math.radians(a) for a in rot]; cam.data.lens = lens
    if name == "walk_side":
      cam.location.y = (rig.matrix_world @ rig.pose.bones["Hips"].head).y
    sc.render.filepath = os.path.join(out, f"look_{name}.png")
    bpy.ops.render.render(write_still=True)


def main():
  argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
  check_dir = argv[argv.index("--check") + 1] if "--check" in argv else None
  os.makedirs(TEXD, exist_ok=True)
  rig = bpy.data.objects["Matt"]
  scan = bpy.data.objects["MattMesh"]
  scan.name = "MattScan"
  for m in list(scan.modifiers):
    if m.type == "ARMATURE":
      scan.modifiers.remove(m)                         # bake from the rest pose
  ob = retopo(scan)
  game_material(ob)
  tex = bakes(scan, ob)
  cut_scanned_phone(ob, rig, tex["albedo"])
  game_rig(rig, ob, scan)
  breathe(ob, rig)
  ph = phone(scan, rig)
  bb = bib(ob, rig, bib_texture())
  bpy.data.objects.remove(scan)                        # matt.blend keeps it; the game file does not need it
  dist = matt_walk.build(rig, ob, cycles=1, root_bone="Root")
  log(f"walk: {matt_walk.CYCLE} frames, {dist:.2f} m per cycle ({dist / (matt_walk.CYCLE / matt_walk.FPS):.2f} m/s)")
  bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RAW, "matt_game.blend"))
  props = [p for p in (ph, bb) if p is not None]
  export(rig, [ob] + props)
  tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
  log(f"exported {EXP}: body {len(ob.data.vertices)} verts / {tris} tris, textures {sorted(os.listdir(TEXD))}")
  if check_dir:
    check(rig, [ob] + props, check_dir)


main()
