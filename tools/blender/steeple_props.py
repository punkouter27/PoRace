"""Steeplechase obstacle visuals (Map 04), built headless:

  blender -b --factory-startup -P tools/blender/steeple_props.py

Each prop is a unit-size mesh (1 x 1 x 1 m, centred on the origin) that Unity scales to the MuJoCo
box it decorates (SteepleBuilder in Assets/Editor/TrackBuilder.cs). Visual only: collisions are MuJoCo box geoms.
  hurdle  bevelled beam, 8 red / white bands along its length (the across-track axis)
  step    bevelled slab, yellow hazard edge band around the top
  rubble  chunky block with noise displacement, kept inside the unit box
Outputs: Assets/Art/Raw/steeple_props.blend (source) and Assets/Art/Exports/GLB/<prop>.fbx (Unity), Blender axes
-Z forward / Y up as Unity expects.
"""
import math
import os

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "Assets", "Art", "Raw")
EXP = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB")


def material(name, rgb, rough=0.6):
  m = bpy.data.materials.new(name)
  m.use_nodes = True
  bsdf = m.node_tree.nodes["Principled BSDF"]
  bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
  bsdf.inputs["Roughness"].default_value = rough
  m.diffuse_color = (*rgb, 1.0)
  return m


def unit_box(name, bevel=0.06, segments=2):
  bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
  ob = bpy.context.active_object
  ob.name = name
  mod = ob.modifiers.new("bevel", "BEVEL")
  mod.width = bevel
  mod.segments = segments
  bpy.ops.object.modifier_apply(modifier="bevel")
  return ob


def assign_by(ob, mats, pick):
  """Material slot per face from pick(face centre) -> slot index."""
  for m in mats:
    ob.data.materials.append(m)
  for p in ob.data.polygons:
    p.material_index = pick(p.center)


def hurdle():
  ob = unit_box("hurdle", bevel=0.08)
  # Subdivide along Y so the bands follow the beam: 8 bands across the track once scaled (local Y -> Unity Z).
  bm = bmesh.new()
  bm.from_mesh(ob.data)
  for k in range(1, 8):
    y = -0.5 + k / 8
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, y, 0), plane_no=(0, 1, 0))
  bm.to_mesh(ob.data)
  bm.free()
  assign_by(ob, [material("hurdle_red", (0.85, 0.12, 0.08)), material("hurdle_white", (0.95, 0.95, 0.95))],
            lambda c: int(math.floor((c.y + 0.5) * 8)) % 2)
  return ob


def step():
  ob = unit_box("step", bevel=0.04)
  # Inset the top face: the ring between the inset and the edge (and the bevels) is the yellow hazard band.
  bm = bmesh.new()
  bm.from_mesh(ob.data)
  top = max(bm.faces, key=lambda f: f.calc_center_median().z if f.normal.z > 0.9 else -9)
  bmesh.ops.inset_region(bm, faces=[top], thickness=0.08)
  bm.to_mesh(ob.data)
  bm.free()
  assign_by(ob, [material("step_body", (0.22, 0.45, 0.75)), material("step_edge", (0.98, 0.78, 0.05))],
            lambda c: 1 if c.z > 0.3 and (abs(c.x) > 0.37 or abs(c.y) > 0.37) else 0)
  return ob


def rubble():
  ob = unit_box("rubble", bevel=0.12, segments=3)
  bpy.ops.object.mode_set(mode="EDIT")
  bpy.ops.mesh.subdivide(number_cuts=2)
  bpy.ops.object.mode_set(mode="OBJECT")
  tex = bpy.data.textures.new("rubble_noise", "CLOUDS")
  tex.noise_scale = 0.35
  d = ob.modifiers.new("disp", "DISPLACE")
  d.texture = tex
  d.strength = -0.08   # inward only, so the block stays inside its collision box
  bpy.ops.object.modifier_apply(modifier="disp")
  assign_by(ob, [material("rubble_stone", (0.45, 0.42, 0.38), rough=0.9)], lambda c: 0)
  return ob


def main():
  bpy.ops.wm.read_factory_settings(use_empty=True)
  os.makedirs(RAW, exist_ok=True)
  os.makedirs(EXP, exist_ok=True)
  props = [hurdle(), step(), rubble()]
  for i, ob in enumerate(props):
    ob.location.x = 2.0 * i   # spread out in the .blend for viewing; each export is re-centred below
  bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RAW, "steeple_props.blend"))
  for ob in props:
    x = ob.location.x
    ob.location.x = 0.0
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(EXP, ob.name + ".fbx"), use_selection=True, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             object_types={"MESH"}, mesh_smooth_type="FACE")
    ob.location.x = x
    print("exported", ob.name, len(ob.data.vertices), "verts")


main()
