"""Matt's procedural walk cycle (the "Walk" clip), used by tools/blender/matt_look.py; the race itself is driven by the
trained policy, this is for menus, previews and the roster card.

Keyed joint curves (thigh leads and retracts before heel strike, early-swing knee flexion, heel-first contact with
the toes up, push-off, counter-swinging arms), then:
  - root: the lowest skin point sits on the floor and the material points of the planted foot's contact patch are
    held still, so forward travel comes from the stride itself (no guessed speed)
  - trailing-foot IK in double support: a numeric two-bone solve on hip/knee pitch keeps the foot that is about to
    leave the floor pinned, the foot keeping its world pitch
Measured on matt.blend: 1.21 m/s, hip bob 4.0 cm, contact slip mean 1.0 cm/frame (max 2.2, mostly heel-to-toe roll).
Bone signs (measured): +X swings a limb forward; LowerLeg flexion is -X; Foot +X lifts the toes; LowerArm +X flexes.
"""
import math

import bpy
from mathutils import Vector

FPS, CYCLE = 30, 34      # 34 frames per stride pair (~1.13 s)
D = math.radians


def bump(p, a, peak, b):
  """0 -> 1 -> 0 eased bump over leg phase p (deg, wrapped to [-180, 180)): rises a..peak, falls peak..b."""
  p = (p + 180) % 360 - 180
  if a <= p <= peak:
    return math.sin(0.5 * math.pi * (p - a) / (peak - a)) ** 2
  if peak < p <= b:
    return math.cos(0.5 * math.pi * (p - peak) / (b - peak)) ** 2
  return 0.0


def pose(t):
  """Joint angles (deg) at cycle phase t in [0, 1). Leg phase p: swing -90..90, stance 90..270."""
  ph = 2 * math.pi * t
  rot = {}
  for side, off in (("Left", 0.0), ("Right", math.pi)):
    p = math.degrees(ph + off)
    s = math.sin(math.radians(p + 35))                                # thigh leads: peaks forward at p = 55
    thigh = 22 * s + 4                                                # 26 deg forward, 18 back
    knee = 4 + 42 * bump(p, -150, -45, 48) + 14 * bump(p, 50, 85, 145)    # early-swing flex; loading response
    foot = knee - thigh                                               # sole level with the floor ...
    foot -= 20 * bump(p, -180, -125, -80)                             # ... push off (toes down)
    foot += 10 * bump(p, -70, -15, 35) + 14 * bump(p, 15, 55, 95)     # toe clearance, heel strike (toes up)
    rot[side + "UpperLeg"] = (thigh, 0, 0)
    rot[side + "LowerLeg"] = (-knee, 0, 0)
    rot[side + "Foot"] = (foot, 0, 0)
    sgn = 1 if side == "Left" else -1
    rot[side + "UpperArm"] = (-18 * s - 7, 0, -sgn * 40)               # counter-swing to own leg, centred on the hip
    rot[side + "LowerArm"] = (16 + 14 * max(0.0, -s), 0, 0)            # elbow bends more as the arm comes forward
  rot["Hips"] = (0, 4 * math.sin(ph), 2.5 * math.cos(2 * ph))
  rot["Spine"] = (5, -4 * math.sin(ph), 0)
  rot["Chest"] = (0, -3 * math.sin(ph), 0)
  rot["Head"] = (-3, 3 * math.sin(ph), 0)
  return rot


def build(rig, mesh, cycles=1, root_bone=None, name="Walk"):
  """Key `cycles` stride pairs on `rig` (skinned `mesh` must carry Foot/Toes groups). Returns metres travelled.
  With root_bone, travel and height end up on that bone (root motion) instead of the rig object."""
  sc = bpy.context.scene
  pb = rig.pose.bones
  for b in pb:
    b.rotation_mode = "XYZ"
  rig.animation_data_create()
  rig.animation_data.action = bpy.data.actions.new(name)
  n = CYCLE * cycles
  sc.frame_start, sc.frame_end, sc.render.fps = 1, n + 1, FPS

  # 1) joint keys
  for f in range(n + 1):
    for b in pb:
      b.rotation_euler = (0, 0, 0)
    for k, (x, y, z) in pose((f % CYCLE) / CYCLE).items():
      if k in pb:
        pb[k].rotation_euler = (D(x), D(y), D(z))
    for b in pb:
      b.keyframe_insert("rotation_euler", frame=f + 1)

  # 2) root: floor contact + planted material points
  gi = {g.name: g.index for g in mesh.vertex_groups}
  feet = {s: [v.index for v in mesh.data.vertices
              if sum(g.weight for g in v.groups if g.group in (gi[s + "Foot"], gi[s + "Toes"])) > 0.5] for s in ("Left", "Right")}
  y, prev = 0.0, None
  for f in range(n + 1):
    sc.frame_set(f + 1)
    rig.location = (0, 0, 0)
    bpy.context.view_layer.update()
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    zmin = min((ev.matrix_world @ v.co).z for v in ev.data.vertices)
    t = (f % CYCLE) / CYCLE
    stance = "Left" if 0.12 <= t < 0.62 else "Right"   # hand-over at t 0.12 / 0.62: measured best for slip
    both = {s: {i: ev.matrix_world @ ev.data.vertices[i].co for i in feet[s]} for s in feet}
    if prev is not None:
      pts = both[prev[0]]
      low = min(q.z for q in pts.values())
      patch = [i for i, q in pts.items() if q.z < low + 0.01]
      y -= sum(pts[i].y - prev[1][i].y for i in patch) / len(patch)
    prev = (stance, both[stance])
    rig.location = (0, y, -zmin)
    rig.keyframe_insert("location", frame=f + 1)

  # 3) trailing-foot IK
  def ankle(side):
    return rig.matrix_world @ pb[side + "LowerLeg"].tail

  def solve(side, target, iters=8):
    th, kn = pb[side + "UpperLeg"], pb[side + "LowerLeg"]
    for _ in range(iters):
      bpy.context.view_layer.update()
      a = ankle(side)
      err = Vector((target.y - a.y, target.z - a.z))
      if err.length < 2e-4:
        break
      J = []
      for b in (th, kn):
        b.rotation_euler.x += 1e-3
        bpy.context.view_layer.update()
        dd = ankle(side) - a
        J.append((dd.y / 1e-3, dd.z / 1e-3))
        b.rotation_euler.x -= 1e-3
      (a11, a21), (a12, a22) = J
      det = a11 * a22 - a12 * a21
      if abs(det) < 1e-9:
        break
      th.rotation_euler.x += (a22 * err.x - a12 * err.y) / det
      kn.rotation_euler.x += (-a21 * err.x + a11 * err.y) / det
    bpy.context.view_layer.update()

  anchor = {"Left": None, "Right": None}
  for f in range(n + 1):
    sc.frame_set(f + 1)
    t = (f % CYCLE) / CYCLE
    trail = "Right" if 0.12 <= t < 0.62 else "Left"
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    pts = {i: ev.matrix_world @ ev.data.vertices[i].co for i in feet[trail]}
    touch = [i for i, q in pts.items() if q.z < 0.01]
    if not touch:
      anchor[trail] = None
      continue
    if anchor[trail] is None:
      anchor[trail] = {i: pts[i].copy() for i in touch}
      continue
    common = [i for i in touch if i in anchor[trail]] or touch
    delta = sum(((anchor[trail].get(i, pts[i]) - pts[i]) for i in common), Vector()) / len(common)
    bones = [pb[trail + k] for k in ("UpperLeg", "LowerLeg", "Foot")]
    pitch = sum(b.rotation_euler.x for b in bones)
    solve(trail, ankle(trail) + delta)
    bones[2].rotation_euler.x = pitch - bones[0].rotation_euler.x - bones[1].rotation_euler.x
    for b in bones:
      b.keyframe_insert("rotation_euler", frame=f + 1)
    for i in touch:
      anchor[trail].setdefault(i, pts[i] + delta)

  # 4) root motion onto the root bone (rig object stays at the origin)
  if root_bone is not None:
    rb = pb[root_bone]
    to_local = rig.data.bones[root_bone].matrix_local.to_3x3().inverted()
    locs = []
    for f in range(n + 1):
      sc.frame_set(f + 1)
      locs.append(rig.location.copy())
    fc = rig.animation_data.action
    for c in [c for c in _fcurves(fc) if c.data_path == "location"]:
      _remove_fcurve(fc, c)
    rig.location = (0, 0, 0)
    for f, w in enumerate(locs):
      rb.location = to_local @ w
      rb.keyframe_insert("location", frame=f + 1)
  sc.frame_set(1)
  return abs(y)


def _fcurves(action):
  """Action F-curves (Blender 5 keeps them in layered channelbags)."""
  if hasattr(action, "fcurves"):
    return list(action.fcurves)
  out = []
  for layer in action.layers:
    for strip in layer.strips:
      for bag in strip.channelbags:
        out.extend(bag.fcurves)
  return out


def _remove_fcurve(action, fc):
  if hasattr(action, "fcurves"):
    action.fcurves.remove(fc)
    return
  for layer in action.layers:
    for strip in layer.strips:
      for bag in strip.channelbags:
        if fc in list(bag.fcurves):
          bag.fcurves.remove(fc)
          return
