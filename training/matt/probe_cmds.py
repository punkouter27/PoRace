"""Which race commands make Matt fall? Holds each command for 10 s from a standstill in the CPU sim (same model and
timing as the race) and reports the first fall."""
import sys
import numpy as np
import matt.sim  # noqa: F401
import g1.sim as base
from brax.io import model

sim = base.G1Sim(model.load_params(sys.argv[1]))
for cmd in ([1.47, 0, 0], [1.47, 0, -0.8], [1.47, 0, 0.8], [1.2, 0, -0.5], [0.8, 0, 0], [1.47, 0.25, 0], [0, 0, 0]):
  falls = []
  for seed in range(5):
    sim.reset(np.random.default_rng(seed)); t = None
    for k in range(int(10 / base.CTRL_DT)):
      sim.control_step(cmd)
      if not sim.upright(): t = sim.d.time; break
    falls.append(t)
  v, _ = sim.vel()
  print(f"cmd {cmd}: falls {sum(f is not None for f in falls)}/5 at {[round(f, 1) for f in falls if f]}; last vx {v[0]:.2f}")
