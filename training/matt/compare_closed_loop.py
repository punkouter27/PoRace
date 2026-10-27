"""Matt closed-loop gate (C.9 contract, as compare_trajectory.py does for the Go2): Unity run vs trainer reference over
the same seconds from the same initial state and command.
  python matt/compare_closed_loop.py /docs/parity/matt_reference.json /docs/parity/unity_matt_closed_loop.json
Pass: upright the whole time on both sides; mean forward speed within 0.1 m/s; foot touchdown cadence within 10 %;
mean |actuator force| within 15 %. Contacts and forces are recomputed from qpos/qvel/ctrl with CPU MuJoCo so both
sides are measured identically (actuators: kp (ctrl - q), clipped to forcerange; no velocity term)."""
import json
import sys
from pathlib import Path

import mujoco
import numpy as np

XML = Path(__file__).resolve().parent / "assets" / "matt_porace.xml"
m = mujoco.MjModel.from_xml_path(str(XML)); d = mujoco.MjData(m)
FEET = [m.geom("left_foot").id, m.geom("right_foot").id]
KP = m.actuator_gainprm[:, 0]; FMAX = m.actuator_forcerange[:, 1]
QADR = np.array([m.jnt_qposadr[m.actuator_trnid[i, 0]] for i in range(m.nu)])


def sole(g):
  R = d.geom_xmat[g].reshape(3, 3); half = m.geom_size[g]
  return min((d.geom_xpos[g] + R @ (half * [sx, sy, -1]))[2] for sx in (-1, 1) for sy in (-1, 1))


def analyse(frames):
  vx, contacts, forces, upright = [], [], [], []
  for f in frames:
    d.qpos[:] = f["qpos"]; d.qvel[:] = f["qvel"]; d.ctrl[:] = f["ctrl"]
    mujoco.mj_forward(m, d)
    vx.append(d.qvel[0])
    upright.append(d.sensor("upvector_torso").data[2] > 0)
    contacts.append([sole(g) < 0.005 for g in FEET])
    forces.append(np.clip(KP * (d.ctrl - d.qpos[QADR]), -FMAX, FMAX))
  c = np.array(contacts)
  touchdowns = int(np.sum(c[1:] & ~c[:-1]))
  secs = frames[-1]["t"] - frames[0]["t"]
  return dict(upright=bool(np.all(upright)), mean_vx=float(np.mean(vx)), cadence_hz=touchdowns / max(secs, 1e-6) / 2,
              mean_abs_force=float(np.mean(np.abs(forces))))


def main(ref_path, unity_path):
  ref = json.load(open(ref_path))["frames"]; uni = json.load(open(unity_path))["frames"]
  n = min(len(ref), len(uni)); a, b = analyse(ref[:n]), analyse(uni[:n])
  checks = {
    "upright": a["upright"] and b["upright"],
    "speed": abs(a["mean_vx"] - b["mean_vx"]) < 0.1,
    "cadence": abs(a["cadence_hz"] - b["cadence_hz"]) <= 0.1 * max(a["cadence_hz"], 1e-6),
    "force": abs(a["mean_abs_force"] - b["mean_abs_force"]) <= 0.15 * max(a["mean_abs_force"], 1e-6),
  }
  for k in ("mean_vx", "cadence_hz", "mean_abs_force"):
    print(f"{k:15s} trainer {a[k]:8.3f}   unity {b[k]:8.3f}")
  print(f"upright         trainer {a['upright']}   unity {b['upright']}   ({n} frames)")
  print("MATT CLOSED LOOP", "PASS" if all(checks.values()) else f"FAIL {[k for k, v in checks.items() if not v]}")
  return all(checks.values())


if __name__ == "__main__":
  raise SystemExit(0 if main(sys.argv[1], sys.argv[2]) else 1)
