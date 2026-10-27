"""Matt model gate (run before any training; trainer container):
  python matt/check_model.py [--fix-keys]
- counts, joint order = G1, total mass, actuator ranges
- --fix-keys: sets each keyframe's pelvis height so the lowest sole corner sits 1 mm above the floor (in place)
- self-penetration (non-floor contacts with dist < -1 mm) at home, knees_bent, T-pose and every joint at each range
  limit (others at home): project rule "no penetration at rest, T-pose or joint-limit extremes"
- 2 s PD hold at knees_bent: finite, and how long it stays upright
Writes docs/matt/model_summary.json (same fields as docs/g1/model_summary.json)."""
import json
import re
import sys
from pathlib import Path

import mujoco
import numpy as np

XML = Path(__file__).resolve().parent / "assets" / "matt_porace.xml"
G1_ORDER = [j for j in json.load(open("/docs/g1/model_summary.json"))["joints"]]


def load():
  return mujoco.MjModel.from_xml_path(str(XML))


def sole_min(m, d):
  z = []
  for g in ("left_foot", "right_foot"):
    gid = m.geom(g).id
    half = m.geom_size[gid]
    R = d.geom_xmat[gid].reshape(3, 3)
    for sx in (-1, 1):
      for sy in (-1, 1):
        for sz in (-1, 1):
          z.append((d.geom_xpos[gid] + R @ (half * [sx, sy, sz]))[2])
  return min(z)


def fix_keys():
  m = load(); d = mujoco.MjData(m)
  txt = XML.read_text()
  for k in range(m.nkey):
    d.qpos[:] = m.key_qpos[k]; mujoco.mj_forward(m, d)
    z = m.key_qpos[k][2] - sole_min(m, d) + 0.001
    name = m.key(k).name
    txt = re.sub(rf'(<key name="{name}" qpos=")(\S+) (\S+) (\S+)', lambda mo: f"{mo.group(1)}{mo.group(2)} {mo.group(3)} {z:.4f}", txt)
    print(f"key {name}: pelvis z -> {z:.4f}")
  XML.write_text(txt)


def self_pen(m, d, tol=-0.001):
  mujoco.mj_forward(m, d)
  floor = m.geom("floor").id
  out = []
  for i in range(d.ncon):
    c = d.contact[i]
    if floor in (c.geom1, c.geom2) or c.dist >= tol:
      continue
    out.append((m.geom(c.geom1).name, m.geom(c.geom2).name, round(float(c.dist), 4)))
  return out


def main():
  if "--fix-keys" in sys.argv:
    fix_keys()
  m = load(); d = mujoco.MjData(m)
  joints = [m.joint(i).name for i in range(1, m.njnt)]
  print("nq nv nu nbody ngeom nsensor nkey", m.nq, m.nv, m.nu, m.nbody, m.ngeom, m.nsensor, m.nkey)
  print("opt dt", m.opt.timestep, "iter", m.opt.iterations, "ls", m.opt.ls_iterations, "impratio", m.opt.impratio,
        "cone", m.opt.cone, "integrator", m.opt.integrator)
  print("joint order == G1:", joints == G1_ORDER, "| actuator->joint identity:", all(m.actuator_trnid[i, 0] == i + 1 for i in range(m.nu)))
  print("total mass %.2f kg" % m.body_mass.sum(), "| heaviest", sorted(((round(float(m.body_mass[i]), 2), m.body(i).name) for i in range(m.nbody)), reverse=True)[:4])
  bad = 0
  # skeleton vs MJCF: at zero pose (pelvis at the rig's Hips head, identity orientation) every joint body must sit
  # on the rig joint it was built from (tools/blender/matt_mjcf.py, landmarks in fit.json)
  fit = json.load(open(Path(__file__).resolve().parent / "fit.json"))["landmarks"]
  d.qpos[:] = 0; d.qpos[0:3] = fit["pelvis"]; d.qpos[3] = 1; mujoco.mj_forward(m, d)
  pairs = {"waist_yaw_link": "lumbar"}
  for s in ("left", "right"):
    pairs.update({f"{s}_hip_pitch_link": f"{s}_hip", f"{s}_knee_link": f"{s}_knee", f"{s}_ankle_pitch_link": f"{s}_ankle",
                  f"{s}_shoulder_pitch_link": f"{s}_shoulder", f"{s}_elbow_link": f"{s}_elbow", f"{s}_wrist_roll_link": f"{s}_wrist"})
  err = max(float(np.linalg.norm(d.xpos[m.body(b).id] - np.array(fit[l]))) for b, l in pairs.items())
  frames = all(np.allclose(d.xquat[i], [1, 0, 0, 0]) for i in range(1, m.nbody))
  print(f"[frames] {len(pairs)} joint bodies vs rig joints: max error {err * 1000:.2f} mm; all body frames axis-aligned: {frames}")
  bad += err > 1e-3 or not frames
  home = m.key("home").qpos.copy()
  for name in ("home", "knees_bent"):
    d.qpos[:] = m.key(name).qpos; p = self_pen(m, d); bad += bool(p)
    print(f"[{name}] self-penetration: {p or 'none'}; sole min z {sole_min(m, d):.4f}")
  tpose = home.copy()
  tpose[7 + joints.index("left_shoulder_roll_joint")] = 0.87; tpose[7 + joints.index("right_shoulder_roll_joint")] = -0.87
  tpose[7 + joints.index("left_elbow_joint")] = 0; tpose[7 + joints.index("right_elbow_joint")] = 0
  d.qpos[:] = tpose; p = self_pen(m, d); bad += bool(p)
  print(f"[T-pose] self-penetration: {p or 'none'}")
  ext = []
  for j, jn in enumerate(joints):
    for lim in m.jnt_range[j + 1]:
      q = home.copy(); q[7 + j] = lim; d.qpos[:] = q
      p = self_pen(m, d)
      if p:
        ext.append((jn, float(lim), p))
  bad += len(ext)
  print(f"[limits] {len(ext)} joint extremes with self-penetration")
  for e in ext:
    print("   ", e)
  # PD hold at knees_bent
  mujoco.mj_resetDataKeyframe(m, d, m.key("knees_bent").id)
  up = 0.0
  for i in range(int(2.0 / m.opt.timestep)):
    mujoco.mj_step(m, d)
    if not np.isfinite(d.qpos).all():
      print("PD hold: NaN at step", i); bad += 1; break
    if d.qpos[2] > 0.7:
      up = (i + 1) * m.opt.timestep
  print(f"[hold] knees_bent PD hold: upright (pelvis > 0.7 m) for {up:.2f} s of 2.00; final pelvis z {d.qpos[2]:.3f}")
  kid = m.key("knees_bent").id
  summary = {"timestep": m.opt.timestep, "nq": m.nq, "nv": m.nv, "nu": m.nu, "mass": float(m.body_mass.sum()),
             "joints": joints, "actuators": [m.actuator(i).name for i in range(m.nu)],
             "default_pose": m.key_qpos[kid][7:].tolist(), "init_qpos": m.key_qpos[kid].tolist(),
             "kp": m.actuator_gainprm[:, 0].tolist(), "kd_bias": m.actuator_biasprm[:, 2].tolist(),
             "ctrlrange": m.actuator_ctrlrange.tolist(), "forcerange": m.actuator_forcerange.tolist(),
             "jnt_range": m.jnt_range[1:].tolist()}
  out = Path("/docs/matt"); out.mkdir(parents=True, exist_ok=True)
  (out / "model_summary.json").write_text(json.dumps(summary, indent=1))
  print("GATE", "PASS" if bad == 0 else f"FAIL ({bad} problems)")


main()
