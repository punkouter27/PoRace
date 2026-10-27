"""Race tracking of chosen checkpoints of a Matt run (CPU): mean vx / vy / yaw rate per command, then the graduation
test on a seed subset.
  python matt/probe_ckpts.py runs/m7 24084480 48168960 [--grad 20]
"""
import contextlib, io, sys
from pathlib import Path

import numpy as np
from brax.io import model
from brax.training.agents.ppo import checkpoint as ck

import matt.sim as ms
import g1.sim as base

args = sys.argv[1:]; grad = 20
if "--grad" in args: i = args.index("--grad"); grad = int(args[i + 1]); del args[i:i + 2]
run = Path(args[0])
for step in args[1:]:
  out = run / f"params_{int(step):012d}.pkl"
  if not out.exists(): model.save_params(str(out), ck.load(str((run / "checkpoints" / f"{int(step):012d}").resolve())))
  params = model.load_params(str(out)); sim = base.G1Sim(params); rows = []
  for cmd in ([0, 0, 0], [1.0, 0, 0], [1.5, 0, 0], [1.5, 0, 0.6], [1.5, 0, -0.6], [1.0, 0.2, 0]):
    sim.reset(np.random.default_rng(0)); m = []; up = True
    for k in range(int(5 / base.CTRL_DT)):
      sim.control_step(cmd); up &= bool(sim.upright())
      if k >= int(1.5 / base.CTRL_DT): v, w = sim.vel(); m.append([v[0], v[1], w[2]])
    vx, vy, wz = np.mean(m, 0)
    rows.append(f"{cmd} -> {vx:.2f} {vy:+.2f} {wz:+.2f}{'' if up else ' FELL'}")
  buf = io.StringIO()
  with contextlib.redirect_stdout(buf): ms.graduate(params, grad)
  print(f"{int(step) / 1e6:6.1f}M  " + " | ".join(rows) + "\n         " + buf.getvalue().strip().splitlines()[-1], flush=True)
