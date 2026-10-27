"""Does Matt hold his heading on a zero command (the race countdown)? Yaw drift and wander over 3 s and 10 s."""
import sys
import numpy as np
import matt.sim  # noqa: F401
import g1.sim as base
from brax.io import model

sim = base.G1Sim(model.load_params(sys.argv[1]))
def yaw(q): return np.arctan2(2 * (q[0] * q[3] + q[1] * q[2]), 1 - 2 * (q[2] ** 2 + q[3] ** 2))
for seed in range(5):
  sim.reset(np.random.default_rng(seed)); y0 = yaw(sim.d.qpos[3:7]); out = []
  for k in range(int(10 / base.CTRL_DT)):
    sim.control_step([0, 0, 0])
    if k + 1 in (150, 500):
      out.append(f"{(k + 1) * base.CTRL_DT:.0f} s: yaw {np.degrees(yaw(sim.d.qpos[3:7]) - y0):+6.1f} deg, moved {np.linalg.norm(sim.d.qpos[:2]) * 100:5.1f} cm")
  print(f"seed {seed}: " + " | ".join(out) + f" | upright {bool(sim.upright())}")
