"""Matt's MuJoCo body (tasks E.4), derived headless from the rig and skin in Assets/Art/Raw/matt.blend:

  blender -b Assets/Art/Raw/matt.blend -P tools/blender/matt_mjcf.py

Joint layout and names are the Unitree G1's (29 actuated DoF, same bodies, sites, sensors and keyframe name), so the
playground G1 joystick env, export_onnx.py and G1Controller run on it unchanged. Everything physical is Matt's:
  - joint centres = rig joint heads (hips, knees, ankles, lumbar, shoulders, elbows, wrists)
  - collision = primitives fitted to the skin vertices each bone owns (weight >= 0.5): capsules for limbs, pelvis,
    abdomen, chest and neck, a sphere for the head, boxes for the feet. No meshes.
  - masses = Winter's segment fractions of BODY_MASS; inertia from the primitive shapes
  - human joint ranges and peak torques (forcerange), PD position actuators
  - self-collision on every segment pair except anatomically connected ones (explicit <exclude> where the G1 chain
    puts massless intermediate links between them, MuJoCo's parent filter for the rest)
  - physics: the race model's (4 ms, Euler, pyramidal, iterations 1, ls 5, floor friction 0.6, contype 1 /
    conaffinity 0 - Matt shares the race's single MuJoCo model with the dogs) except impratio 1: at the dogs' impratio
    100 a 90 kg body standing still gets floor forces of 3.7x its weight (median), spikes to 700x; impratio 1 gives
    0.98x / 1.7x. A race with Matt compiles the shared model at impratio 1 (rl_optimization_log.md).
Frames: Blender (x right, y back, z up; Matt faces -Y) -> MuJoCo (x forward, y left, z up): (x, y, z) -> (-y, x, z).
Writes training/matt/assets/matt_porace.xml (trainer scene) and training/matt/fit.json (landmarks and fits).
"""
import json
import math
import os

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "training", "matt")
BODY_MASS = 88.0      # kg: BMI 25 at 1.89 m (Matt's weight unknown)
FRAC = {"pelvis": 0.142, "abdomen": 0.139, "chest": 0.216, "neck": 0.012, "head": 0.069, "thigh": 0.100,
        "shin": 0.0465, "foot": 0.0145, "upper_arm": 0.028, "forearm": 0.016, "hand": 0.006}   # Winter (2009)
LINK_MASS = 0.1       # intermediate joint links (G1 chain bodies without geometry)

rig = bpy.data.objects["Matt"]
mesh = bpy.data.objects["MattMesh"]
M = rig.matrix_world


def mj(v):
  return Vector((-v.y, v.x, v.z))


def joint(bone, tail=False):
  b = rig.data.bones[bone]
  return mj(M @ (b.tail_local if tail else b.head_local))


gi = {g.name: g.index for g in mesh.vertex_groups}
VERTS = [mj(mesh.matrix_world @ v.co) for v in mesh.data.vertices]
WEIGHTS = [{g.group: g.weight for g in v.groups} for v in mesh.data.vertices]


def owned(*bones):
  ids = [gi[b] for b in bones]
  return [VERTS[i] for i, w in enumerate(WEIGHTS) if sum(w.get(g, 0.0) for g in ids) >= 0.5]


def pct(vals, q):
  s = sorted(vals)
  return s[min(len(s) - 1, int(q * len(s)))]


def limb_radius(pts, a, b, q=0.4):
  """Radius of a capsule on segment a-b: a percentile of the owned vertices' distance to the segment (middle 80 %
  of its length only, so joint bulges and neighbours do not inflate it). Low percentile: the scan's baggy sleeves
  and shorts are cloth, not limb."""
  ab = b - a
  d = []
  for p in pts:
    t = (p - a).dot(ab) / ab.length_squared
    if 0.1 < t < 0.9:
      d.append((p - (a + ab * t)).length)
  return pct(d, q)


def torso_band(pts, z0, z1, k=0.45):
  """Horizontal capsule (along y) for a torso band z0..z1: radius 0.45 x the front-back depth (a little under half:
  soft tissue), ends at the band's width minus that radius."""
  band = [p for p in pts if z0 <= p.z <= z1]
  xs, ys = [p.x for p in band], [p.y for p in band]
  x0, x1 = pct(xs, 0.05), pct(xs, 0.95)
  r = k * (x1 - x0)
  half = max(0.02, (pct(ys, 0.97) - pct(ys, 0.03)) / 2 - r)
  return Vector(((x0 + x1) / 2, (pct(ys, 0.03) + pct(ys, 0.97)) / 2, (z0 + z1) / 2)), half, r


# --- landmarks (MuJoCo frame, metres) ---------------------------------------------------------------------------
L = {}
for s, side in (("left", "Left"), ("right", "Right")):
  L[s + "_hip"] = joint(side + "UpperLeg")
  L[s + "_knee"] = joint(side + "LowerLeg")
  L[s + "_ankle"] = joint(side + "Foot")
  L[s + "_shoulder"] = joint(side + "UpperArm")
  L[s + "_elbow"] = joint(side + "LowerArm")
  L[s + "_wrist"] = joint(side + "Hand")
  L[s + "_handtip"] = joint(side + "Hand", tail=True)
L["pelvis"] = joint("Hips")
L["lumbar"] = joint("Spine")
L["neck"] = joint("Neck")
L["head"] = joint("Head")
L["crown"] = joint("Head", tail=True)

# --- fits -------------------------------------------------------------------------------------------------------
F = {}
pel = owned("Hips")
F["pelvis"] = torso_band(pel, L["left_hip"].z - 0.02, L["pelvis"].z + 0.06)
trunk = owned("Spine", "Chest", "UpperChest")
F["abdomen"] = torso_band(trunk, L["lumbar"].z, L["lumbar"].z + 0.16, 0.38)
F["chest"] = torso_band(trunk, L["lumbar"].z + 0.20, L["neck"].z - 0.06)
headv = owned("Head")
hc = sum(headv, Vector()) / len(headv)
F["head"] = (hc, pct([(p - hc).length for p in headv], 0.45))
F["neck"] = limb_radius(owned("Neck"), L["neck"], L["head"])
for s, side in (("left", "Left"), ("right", "Right")):
  F[s + "_thigh"] = limb_radius(owned(side + "UpperLeg"), L[s + "_hip"], L[s + "_knee"])
  F[s + "_shin"] = limb_radius(owned(side + "LowerLeg"), L[s + "_knee"], L[s + "_ankle"])
  F[s + "_upper_arm"] = limb_radius(owned(side + "UpperArm"), L[s + "_shoulder"], L[s + "_elbow"])
  F[s + "_forearm"] = limb_radius(owned(side + "LowerArm"), L[s + "_elbow"], L[s + "_wrist"])
  F[s + "_hand"] = limb_radius(owned(side + "Hand"), L[s + "_wrist"], L[s + "_handtip"], 0.5)
  # foot: in its own frame (the feet toe out): long axis = ankle -> toe tip direction on the floor
  fv = owned(side + "Foot", side + "Toes")
  tipv = joint(side + "Toes", tail=True)
  ax = Vector((tipv.x - L[s + "_ankle"].x, tipv.y - L[s + "_ankle"].y, 0)).normalized()
  ay = Vector((-ax.y, ax.x, 0))
  us, vs = [p.dot(ax) for p in fv], [p.dot(ay) for p in fv]
  u0, u1, v0, v1 = pct(us, 0.03), pct(us, 0.97), pct(vs, 0.1), pct(vs, 0.9)
  sole = pct([p.z for p in fv], 0.02)
  ctr = ax * ((u0 + u1) / 2) + ay * ((v0 + v1) / 2)
  F[s + "_foot"] = (Vector((ctr.x, ctr.y, sole)), (u1 - u0) / 2, (v1 - v0) / 2, math.atan2(ax.y, ax.x))

# The abdomen is on torso_link, which may collide with the thighs (not adjacent segments): lift it until its bottom
# clears the thigh capsules' tops by 4.5 cm at rest (room for trunk roll and flexion). The pelvis capsule covers the lower belly.
c, half, r = F["abdomen"]
clear = max(L["left_hip"].z + F["left_thigh"], L["right_hip"].z + F["right_thigh"]) + 0.045 + r
F["abdomen"] = (Vector((c.x, c.y, max(c.z, clear))), half, r)

# --- MJCF ---------------------------------------------------------------------------------------------------------


def f3(v):
  return " ".join(f"{c:.5g}" for c in v)


def capsule(name, a, b, r, mass, origin, extra=""):
  return (f'<geom name="{name}" type="capsule" fromto="{f3(a - origin)} {f3(b - origin)}" size="{r:.4f}" '
          f'mass="{mass:.4f}"{extra}/>')


def link(name):
  return f'<inertial pos="0 0 0" mass="{LINK_MASS}" diaginertia="1e-4 1e-4 1e-4"/>'


# joint class: (axis, range by side (left, right), kp, damping, armature, forcerange)
# Ranges: active human ROM, trimmed where segments would meet (feet at full adduction, forearm into the belly, trunk
# into the thighs). kp / damping: the G1's (75 / 2 legs, waist and arms; 20 / 1 ankles; 2 / 0.2 wrists) x 3.9 = Matt's mass x
# height over the G1's, so one unit of action is as aggressive relative to his inertia as it is for the G1 the stock rewards
# were tuned on (kp 600-800 threw him over during early exploration: m1b, rl_optimization_log.md). Static sag is the
# policy's job, as for the G1. forcerange (Nm) caps every joint at human peak torque.
J = {
  "hip_pitch":      ("0 1 0", ((-1.8, 0.5), (-1.8, 0.5)), 300, 8.0, 0.05, 250),
  "hip_roll":       ("1 0 0", ((-0.2, 0.8), (-0.8, 0.2)), 300, 8.0, 0.05, 200),
  "hip_yaw":        ("0 0 1", ((-0.6, 0.6), (-0.6, 0.6)), 300, 8.0, 0.03, 100),
  "knee":           ("0 1 0", ((0.0, 2.4), (0.0, 2.4)), 300, 8.0, 0.05, 250),
  # dorsiflexion (-) to 0.6 rad: the loaded, knee-bent ankle (lunge / late stance), not the 0.35 of a relaxed one. From
  # the -0.25 crouch, 0.35 left 6 deg: m7 pressed the ankles 0.13 rad into that stop on every 1.5 m/s stride.
  "ankle_pitch":    ("0 1 0", ((-0.6, 0.8), (-0.6, 0.8)), 80, 4.0, 0.02, 180),
  "ankle_roll":     ("1 0 0", ((-0.35, 0.35), (-0.35, 0.35)), 80, 4.0, 0.02, 60),
  "waist_yaw":      ("0 0 1", ((-0.5, 0.5),) * 2, 300, 8.0, 0.05, 100),
  "waist_roll":     ("1 0 0", ((-0.3, 0.3),) * 2, 300, 8.0, 0.05, 150),
  "waist_pitch":    ("0 1 0", ((-0.3, 0.6),) * 2, 300, 8.0, 0.05, 200),
  "shoulder_pitch": ("0 1 0", ((-3.0, 0.9), (-3.0, 0.9)), 100, 4.0, 0.02, 90),
  "shoulder_roll":  ("1 0 0", ((-0.45, 2.5), (-2.5, 0.45)), 100, 4.0, 0.02, 80),
  "shoulder_yaw":   ("0 0 1", ((-1.0, 1.0), (-1.0, 1.0)), 100, 4.0, 0.02, 50),
  "elbow":          ("0 1 0", ((-2.4, 0.0), (-2.4, 0.0)), 80, 4.0, 0.02, 70),
  "wrist_roll":     ("1 0 0", ((-0.6, 0.6),) * 2, 8, 0.8, 0.01, 10),
  "wrist_pitch":    ("0 1 0", ((-0.6, 0.6),) * 2, 8, 0.8, 0.01, 10),
  "wrist_yaw":      ("0 0 1", ((-0.6, 0.6),) * 2, 8, 0.8, 0.01, 10),
}
ACT = []


def jnt(prefix, cls, side_i):
  name = f"{prefix}{cls}_joint"
  axis, ranges, kp, kd, arm, fr = J[cls]
  lo, hi = ranges[side_i]
  ACT.append((name, lo, hi, kp, fr))
  return f'<joint name="{name}" axis="{axis}" range="{lo} {hi}" damping="{kd}" armature="{arm}"/>'


m = lambda k: BODY_MASS * FRAC[k]
P0 = L["pelvis"]
out = []
w = out.append
w(f'<body name="pelvis" pos="{f3(P0)}">')
w('<freejoint name="floating_base_joint"/>')
w('<site name="imu_in_pelvis" size="0.01" pos="0 0 0"/>')
c, half, r = F["pelvis"]
w(capsule("pelvis_collision", c - Vector((0, half, 0)), c + Vector((0, half, 0)), r, m("pelvis"), P0))
for side_i, s in enumerate(("left", "right")):
  hip, knee, ank = L[s + "_hip"], L[s + "_knee"], L[s + "_ankle"]
  fc, fhx, fhy, yaw = F[s + "_foot"]
  w(f'<body name="{s}_hip_pitch_link" pos="{f3(hip - P0)}">{link(0)}{jnt(s + "_", "hip_pitch", side_i)}')
  w(f'<body name="{s}_hip_roll_link">{link(0)}{jnt(s + "_", "hip_roll", side_i)}')
  w(f'<body name="{s}_hip_yaw_link">{jnt(s + "_", "hip_yaw", side_i)}')
  w(capsule(f"{s}_thigh", hip, knee, F[s + "_thigh"], m("thigh"), hip))
  w(f'<body name="{s}_knee_link" pos="{f3(knee - hip)}">{jnt(s + "_", "knee", side_i)}')
  w(capsule(f"{s}_shin", knee, ank, F[s + "_shin"], m("shin"), knee))
  w(f'<body name="{s}_ankle_pitch_link" pos="{f3(ank - knee)}">{link(0)}{jnt(s + "_", "ankle_pitch", side_i)}')
  w(f'<body name="{s}_ankle_roll_link">{jnt(s + "_", "ankle_roll", side_i)}')
  hz = 0.03                                                    # sole plate: 6 cm tall, bottom on the sole
  box_c = Vector((fc.x, fc.y, fc.z + hz)) - ank
  w(f'<geom name="{s}_foot" type="box" size="{fhx:.4f} {fhy:.4f} {hz}" pos="{f3(box_c)}" '
    f'quat="{math.cos(yaw / 2):.5f} 0 0 {math.sin(yaw / 2):.5f}" mass="{m("foot"):.4f}"/>')
  w(f'<site name="{s}_foot" size="0.01" pos="{f3(Vector((fc.x, fc.y, fc.z + 0.02)) - ank)}"/>')
  w('</body></body></body></body></body></body>')
lum = L["lumbar"]
w(f'<body name="waist_yaw_link" pos="{f3(lum - P0)}">{link(0)}{jnt("", "waist_yaw", 0)}')
w(f'<body name="waist_roll_link">{link(0)}{jnt("", "waist_roll", 0)}')
w(f'<body name="torso_link">{jnt("", "waist_pitch", 0)}')
c, half, r = F["abdomen"]
w(capsule("abdomen_collision", c - Vector((0, half, 0)), c + Vector((0, half, 0)), r, m("abdomen"), lum))
c, half, r = F["chest"]
w(capsule("chest_collision", c - Vector((0, half, 0)), c + Vector((0, half, 0)), r, m("chest"), lum))
w(f'<site name="imu_in_torso" size="0.01" pos="{f3(Vector((c.x, 0, c.z)) - lum)}"/>')
w(capsule("neck_collision", L["neck"], L["head"], F["neck"], m("neck"), lum))
hc, hr = F["head"]
w(f'<geom name="head_collision" type="sphere" size="{hr:.4f}" pos="{f3(hc - lum)}" mass="{m("head"):.4f}"/>')
for side_i, s in enumerate(("left", "right")):
  sh, el, wr, tip = L[s + "_shoulder"], L[s + "_elbow"], L[s + "_wrist"], L[s + "_handtip"]
  w(f'<body name="{s}_shoulder_pitch_link" pos="{f3(sh - lum)}">{link(0)}{jnt(s + "_", "shoulder_pitch", side_i)}')
  w(f'<body name="{s}_shoulder_roll_link">{link(0)}{jnt(s + "_", "shoulder_roll", side_i)}')
  w(f'<body name="{s}_shoulder_yaw_link">{jnt(s + "_", "shoulder_yaw", side_i)}')
  w(capsule(f"{s}_upper_arm", sh, el, F[s + "_upper_arm"], m("upper_arm"), sh))
  w(f'<body name="{s}_elbow_link" pos="{f3(el - sh)}">{jnt(s + "_", "elbow", side_i)}')
  w(capsule(f"{s}_forearm", el, wr, F[s + "_forearm"], m("forearm"), el))
  w(f'<body name="{s}_wrist_roll_link" pos="{f3(wr - el)}">{link(0)}{jnt(s + "_", "wrist_roll", side_i)}')
  w(f'<body name="{s}_wrist_pitch_link">{link(0)}{jnt(s + "_", "wrist_pitch", side_i)}')
  w(f'<body name="{s}_wrist_yaw_link">{jnt(s + "_", "wrist_yaw", side_i)}')
  w(capsule(f"{s}_hand_collision", wr, tip, F[s + "_hand"], m("hand"), wr))
  w(f'<site name="{s}_palm" size="0.01" pos="{f3((tip - wr) * 0.6)}"/>')
  w('</body></body></body></body></body></body></body>')
w('</body></body></body>')     # torso, waist_roll, waist_yaw
w('</body>')                   # pelvis
body_xml = "\n".join(out)

EXCLUDE = [("pelvis", "{s}_hip_yaw_link"), ("{s}_knee_link", "{s}_ankle_roll_link"), ("pelvis", "torso_link"),
           ("torso_link", "{s}_shoulder_yaw_link"), ("{s}_elbow_link", "{s}_wrist_yaw_link")]
excl = sorted({(a.format(s=s), b.format(s=s)) for a, b in EXCLUDE for s in ("left", "right")})

# keyframes: arms in toward the body, slight crouch (knees_bent = env default pose and reset)
order = [a[0] for a in ACT]


def pose(**kw):
  return " ".join(f"{kw.get(n.replace('_joint', ''), 0.0):.4g}" for n in order)


# knees_bent is the env's default pose: the PD targets are default + action * scale and the pose costs pull towards
# it. An upright stance (knee 0.25) was tried (m10, m11) and both settled into standing still: with the stock 2 m/s
# pushes (see matt/env.py) an upright body cannot ride out a kick. Revisit with the race-scale pushes.
bent = dict(left_hip_pitch=-0.25, right_hip_pitch=-0.25, left_knee=0.5, right_knee=0.5, left_ankle_pitch=-0.25,
            right_ankle_pitch=-0.25, left_shoulder_roll=-0.3, right_shoulder_roll=0.3, left_elbow=-0.3, right_elbow=-0.3)
home = dict(left_shoulder_roll=-0.3, right_shoulder_roll=0.3, left_elbow=-0.15, right_elbow=-0.15)

sensors = "\n".join([
  '<framezaxis objtype="site" objname="imu_in_torso" name="upvector_torso"/>',
  '<velocimeter site="imu_in_torso" name="local_linvel_torso"/>',
  '<velocimeter site="imu_in_pelvis" name="local_linvel_pelvis"/>',
  '<accelerometer site="imu_in_torso" name="accelerometer_torso"/>',
  '<accelerometer site="imu_in_pelvis" name="accelerometer_pelvis"/>',
  '<gyro site="imu_in_torso" name="gyro_torso"/>',
  '<gyro site="imu_in_pelvis" name="gyro_pelvis"/>',
  '<framezaxis objtype="site" objname="imu_in_pelvis" name="upvector_pelvis"/>',
  '<framexaxis objtype="site" objname="imu_in_torso" name="forwardvector_torso"/>',
  '<framexaxis objtype="site" objname="imu_in_pelvis" name="forwardvector_pelvis"/>',
  '<framequat objtype="site" objname="imu_in_torso" name="orientation_torso"/>',
  '<framequat objtype="site" objname="imu_in_pelvis" name="orientation_pelvis"/>',
  '<framelinvel objtype="site" objname="imu_in_torso" name="global_linvel_torso"/>',
  '<framelinvel objtype="site" objname="imu_in_pelvis" name="global_linvel_pelvis"/>',
  '<frameangvel objtype="site" objname="imu_in_torso" name="global_angvel_torso"/>',
  '<frameangvel objtype="site" objname="imu_in_pelvis" name="global_angvel_pelvis"/>',
  '<framelinvel objtype="site" objname="left_foot" name="left_foot_global_linvel"/>',
  '<framelinvel objtype="site" objname="right_foot" name="right_foot_global_linvel"/>',
  '<framezaxis objtype="site" objname="left_foot" name="left_foot_upvector"/>',
  '<framezaxis objtype="site" objname="right_foot" name="right_foot_upvector"/>',
  '<force site="left_foot" name="left_foot_force"/>',
  '<force site="right_foot" name="right_foot_force"/>',
  '<contact geom1="left_foot" geom2="floor" reduce="mindist" name="left_foot_floor_found"/>',
  '<contact geom1="right_foot" geom2="floor" reduce="mindist" name="right_foot_floor_found"/>',
  '<contact geom1="right_foot" geom2="left_foot" reduce="mindist" name="right_foot_left_foot_found"/>',
  '<contact geom1="left_foot" geom2="right_shin" reduce="mindist" name="left_foot_right_shin_found"/>',
  '<contact geom1="right_foot" geom2="left_shin" reduce="mindist" name="right_foot_left_shin_found"/>',
  '<contact geom1="left_hand_collision" geom2="left_thigh" reduce="mindist" name="left_hand_left_thigh_found"/>',
  '<contact geom1="right_hand_collision" geom2="right_thigh" reduce="mindist" name="right_hand_right_thigh_found"/>',
])
actuators = "\n".join(
  f'<general name="{n}" joint="{n}" ctrlrange="{lo} {hi}" forcerange="-{fr} {fr}" forcelimited="true" '
  f'biastype="affine" gainprm="{kp}" biasprm="0 -{kp}"/>' for n, lo, hi, kp, fr in ACT)

xml = f"""<mujoco model="matt scene">
  <!-- generated by tools/blender/matt_mjcf.py from Assets/Art/Raw/matt.blend: do not edit by hand -->
  <compiler angle="radian"/>
  <option timestep="0.004" integrator="Euler" cone="pyramidal" impratio="1" iterations="1" ls_iterations="5">
    <flag eulerdamp="disable"/>
  </option>
  <size nkey="2"/>
  <statistic meansize="0.05" extent="2" center="0 0 0.9"/>
  <default>
    <geom contype="1" conaffinity="1" condim="3" friction="0.6 0.005 0.0001" rgba="0.75 0.55 0.45 1"/>
    <site group="5" rgba="1 0 0 1"/>
  </default>
  <asset>
    <texture type="2d" name="groundplane" builtin="checker" mark="edge" rgb1="0.2 0.3 0.4" rgb2="0.1 0.2 0.3" width="300" height="300"/>
    <material name="groundplane" texture="groundplane" texrepeat="5 5" texuniform="true"/>
  </asset>
  <worldbody>
    <geom name="floor" size="0 0 0.01" type="plane" material="groundplane" priority="1" friction="0.6" condim="3" contype="1" conaffinity="0"/>
    <light name="spotlight" target="pelvis" pos="0 -2 4" exponent="20" mode="targetbodycom"/>
{body_xml}
  </worldbody>
  <contact>
{chr(10).join(f'    <exclude body1="{a}" body2="{b}"/>' for a, b in excl)}
  </contact>
  <actuator>
{actuators}
  </actuator>
  <sensor>
{sensors}
  </sensor>
  <keyframe>
    <key name="home" qpos="0 0 {P0.z:.4f} 1 0 0 0 {pose(**home)}" ctrl="{pose(**home)}"/>
    <key name="knees_bent" qpos="0 0 {P0.z:.4f} 1 0 0 0 {pose(**bent)}" ctrl="{pose(**bent)}"/>
  </keyframe>
</mujoco>
"""
os.makedirs(os.path.join(OUT, "assets"), exist_ok=True)
with open(os.path.join(OUT, "assets", "matt_porace.xml"), "w") as fh:
  fh.write(xml)
fit = {"landmarks": {k: list(v) for k, v in L.items()},
       "fits": {k: (v if isinstance(v, float) else [list(x) if isinstance(x, Vector) else x for x in v]) for k, v in F.items()},
       "body_mass": BODY_MASS, "joint_order": order}
with open(os.path.join(OUT, "fit.json"), "w") as fh:
  json.dump(fit, fh, indent=1)
print(f"[mjcf] {len(order)} joints, {len(excl)} excludes; pelvis z {P0.z:.3f}; radii thigh {F['left_thigh']:.3f} "
      f"shin {F['left_shin']:.3f} upper arm {F['left_upper_arm']:.3f} forearm {F['left_forearm']:.3f} head {F['head'][1]:.3f}; "
      f"pelvis r {F['pelvis'][2]:.3f} half {F['pelvis'][1]:.3f}, abdomen r {F['abdomen'][2]:.3f}, chest r {F['chest'][2]:.3f} "
      f"half {F['chest'][1]:.3f}; foot {2 * F['left_foot'][1]:.3f} x {2 * F['left_foot'][2]:.3f} m, toe-out {math.degrees(F['left_foot'][3]):.0f} deg")
