"""Trackside dressing for every race map (H.6), built headless:

  blender -b --factory-startup -P tools/blender/trackside_props.py

Real-size props (metres) placed beside the track by BroadcastBuilder.Trackside. Visual only: no MuJoCo geoms,
nothing the racers can touch.
  grandstand  three stepped seating tiers in team colours, back wall, roof on four posts (6 x 2.6 x 3.2 m)
  barrier     slanted advertising board in PoRace red / white / blue bands on two feet (2 x 0.5 x 0.7 m)
Each prop has LOD0 (bevelled) and LOD1 (~35 % of the triangles) and one baked texture: base colour x Cycles
ambient occlusion, so the stands read as solid on phones without real-time AO (SSAO is off, see G.0).
Outputs: Assets/Art/Raw/trackside_props.blend (source) and Assets/Art/Exports/GLB/<prop>_lod{0,1}.fbx plus
<prop>_albedo.png (Unity), Blender axes -Z forward / Y up as Unity expects. The prop's origin is the ground point
under its front edge centre; +Y (Unity -Z after export) points away from the track.
"""
import math
import os

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "Assets", "Art", "Raw")
EXP = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB")
TEX = 512


def material(name, rgb, rough=0.7):
  m = bpy.data.materials.new(name)
  if hasattr(m, "use_nodes"):
    m.use_nodes = True
  bsdf = m.node_tree.nodes["Principled BSDF"]
  bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
  bsdf.inputs["Roughness"].default_value = rough
  m.diffuse_color = (*rgb, 1.0)
  return m


def box(name, size, loc, mat, bevel=0.02):
  bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
  ob = bpy.context.active_object
  ob.name = name
  ob.scale = size
  bpy.ops.object.transform_apply(scale=True)
  if bevel > 0:
    mod = ob.modifiers.new("bevel", "BEVEL")
    mod.width = bevel
    mod.segments = 2
    bpy.ops.object.modifier_apply(modifier="bevel")
  ob.data.materials.append(mat)
  return ob


def join(name, parts):
  bpy.ops.object.select_all(action="DESELECT")
  for p in parts:
    p.select_set(True)
  bpy.context.view_layer.objects.active = parts[0]
  bpy.ops.object.join()
  ob = bpy.context.active_object
  ob.name = name
  return ob


def grandstand():
  concrete = material("stand_concrete", (0.55, 0.55, 0.57), 0.9)
  roof = material("stand_roof", (0.92, 0.92, 0.94), 0.5)
  steel = material("stand_steel", (0.25, 0.27, 0.3), 0.4)
  seats = [material("seat_red", (0.82, 0.14, 0.1)), material("seat_blue", (0.12, 0.35, 0.8)),
           material("seat_yellow", (0.95, 0.75, 0.1))]
  L, depth, rise = 6.0, 0.7, 0.45
  parts = []
  for t in range(3):
    y0 = 0.3 + t * depth
    parts.append(box(f"tier{t}", (L, depth, rise * (t + 1)), (0, y0 + depth / 2, rise * (t + 1) / 2), concrete))
    # Seat row: a thin coloured rail on each tier's front edge.
    parts.append(box(f"seats{t}", (L - 0.2, 0.22, 0.16), (0, y0 + 0.2, rise * (t + 1) + 0.08), seats[t % 3], 0.03))
  back = 0.3 + 3 * depth
  parts.append(box("wall", (L, 0.15, 3.0), (0, back + 0.075, 1.5), concrete))
  for x in (-L / 2 + 0.15, L / 2 - 0.15):
    for y in (0.15, back):
      parts.append(box("post", (0.12, 0.12, 3.1), (x, y, 1.55), steel, 0.01))
  r = box("roof", (L + 0.4, back + 0.5, 0.1), (0, back / 2 + 0.05, 3.15), roof)
  r.rotation_euler.x = math.radians(-6)   # sheds toward the back
  bpy.ops.object.transform_apply(rotation=True)
  parts.append(r)
  return join("grandstand", parts)


def barrier():
  red = material("brand_red", (0.86, 0.12, 0.1), 0.5)
  white = material("brand_white", (0.96, 0.96, 0.96), 0.5)
  blue = material("brand_blue", (0.1, 0.25, 0.7), 0.5)
  steel = material("barrier_steel", (0.3, 0.32, 0.35), 0.4)
  parts = []
  # Three horizontal bands, red / white / blue, on a board leaning 15 degrees back.
  for i, m in enumerate((blue, white, red)):
    parts.append(box(f"band{i}", (2.0, 0.06, 0.2), (0, 0.1, 0.12 + 0.2 * i + 0.1), m, 0.01))
  board = join("board", parts)
  board.rotation_euler.x = math.radians(-15)
  bpy.ops.object.transform_apply(rotation=True)
  feet = [box("foot", (0.08, 0.5, 0.06), (x, 0.25, 0.03), steel, 0.01) for x in (-0.8, 0.8)]
  return join("barrier", [board] + feet)


def uv_and_bake(ob, image_name):
  """Smart-project UVs, then bake base colour and AO into one image: colour x AO."""
  bpy.ops.object.select_all(action="DESELECT")
  ob.select_set(True)
  bpy.context.view_layer.objects.active = ob
  bpy.ops.object.mode_set(mode="EDIT")
  bpy.ops.mesh.select_all(action="SELECT")
  bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
  bpy.ops.object.mode_set(mode="OBJECT")

  scene = bpy.context.scene
  scene.render.engine = "CYCLES"
  scene.cycles.device = "CPU"
  scene.cycles.samples = 32
  bake = scene.render.bake
  bake.margin = 4

  def target(name):
    img = bpy.data.images.new(name, TEX, TEX, alpha=False)
    for m in ob.data.materials:
      nodes = m.node_tree.nodes
      n = nodes.get("bake_target") or nodes.new("ShaderNodeTexImage")
      n.name = "bake_target"
      n.image = img
      nodes.active = n
    return img

  col = target(image_name + "_col")
  bake.use_pass_direct = False
  bake.use_pass_indirect = False
  bake.use_pass_color = True
  bpy.ops.object.bake(type="DIFFUSE")
  ao = target(image_name + "_ao")
  bpy.ops.object.bake(type="AO")

  out = bpy.data.images.new(image_name, TEX, TEX, alpha=False)
  c, a = list(col.pixels), list(ao.pixels)
  px = [0.0] * len(c)
  for i in range(0, len(c), 4):
    k = 0.35 + 0.65 * a[i]   # keep crevices from going black
    px[i], px[i + 1], px[i + 2], px[i + 3] = c[i] * k, c[i + 1] * k, c[i + 2] * k, 1.0
  out.pixels = px
  out.filepath_raw = os.path.join(EXP, image_name + ".png")
  out.file_format = "PNG"
  out.save()
  for m in ob.data.materials:   # leave the .blend showing the baked look
    m.node_tree.nodes.remove(m.node_tree.nodes["bake_target"])
  return out


def lod1(ob, ratio=0.35):
  bpy.ops.object.select_all(action="DESELECT")
  ob.select_set(True)
  bpy.context.view_layer.objects.active = ob
  bpy.ops.object.duplicate()
  lo = bpy.context.active_object
  lo.name = ob.name + "_lod1"
  d = lo.modifiers.new("decimate", "DECIMATE")
  d.ratio = ratio
  bpy.ops.object.modifier_apply(modifier="decimate")
  return lo


def export(ob, name):
  x = ob.location.x
  ob.location.x = 0.0
  bpy.ops.object.select_all(action="DESELECT")
  ob.select_set(True)
  bpy.context.view_layer.objects.active = ob
  bpy.ops.export_scene.fbx(filepath=os.path.join(EXP, name + ".fbx"), use_selection=True, apply_unit_scale=True,
                           apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                           object_types={"MESH"}, mesh_smooth_type="FACE")
  ob.location.x = x
  tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
  print("exported", name, tris, "tris")


def main():
  bpy.ops.wm.read_factory_settings(use_empty=True)
  os.makedirs(RAW, exist_ok=True)
  os.makedirs(EXP, exist_ok=True)
  props = [grandstand(), barrier()]
  for ob in props:
    uv_and_bake(ob, ob.name + "_albedo")
  lods = [lod1(ob) for ob in props]
  for i, (hi, lo) in enumerate(zip(props, lods)):
    hi.location.x = lo.location.x = 8.0 * i
    lo.location.y = -6.0
  bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RAW, "trackside_props.blend"))
  for hi, lo in zip(props, lods):
    y = lo.location.y
    lo.location.y = 0.0
    export(hi, hi.name + "_lod0")
    export(lo, hi.name + "_lod1")
    lo.location.y = y


main()
