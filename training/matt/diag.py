"""Why do episodes end? Runs a policy in the trainer-equivalent CPU sim and reports, per seed, the first termination the
stock G1 joystick env would fire: torso upside down, foot-foot contact, or foot-shin contact (its contact sensors).
  python matt/diag.py --params runs/m1b/params_<step>.pkl [--seeds 5]"""
import argparse
import numpy as np
import matt.sim  # noqa: F401  (switches g1.sim to Matt's model and timing)
import g1.sim as base

ap = argparse.ArgumentParser(); ap.add_argument("--params", required=True); ap.add_argument("--seeds", type=int, default=5)
a = ap.parse_args()
from brax.io import model
sim = base.G1Sim(model.load_params(a.params)); m, d = sim.m, sim.d
S = {n: m.sensor_adr[m.sensor(n).id] for n in ("right_foot_left_foot_found", "left_foot_right_shin_found", "right_foot_left_shin_found")}
for seed in range(a.seeds):
  rng = np.random.default_rng(seed); sim.reset(rng)
  cmd = [rng.uniform(0, 1.0), 0, 0]; why = "survived 10 s"; t = 10.0
  for k in range(int(10 / base.CTRL_DT)):
    sim.control_step(cmd)
    hit = [n for n, adr in S.items() if d.sensordata[adr] > 0]
    if d.sensor("upvector_torso").data[2] < 0: why, t = "fell (torso upside down)", d.time; break
    if hit: why, t = f"contact {hit}", d.time; break
  print(f"seed {seed}: cmd vx {cmd[0]:.2f} -> {why} at {t:.2f} s; pelvis z {d.qpos[2]:.2f}")
