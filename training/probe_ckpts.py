"""Pass bars for chosen checkpoints of a run: bank 15 deg, top speed, R1, R2.
  python probe_ckpts.py runs/r5 54067200 90112000
"""
import contextlib, io, sys
from pathlib import Path

import numpy as np
from brax.io import model
from brax.training.agents.ppo import checkpoint as ck

import eval as ev
from go2 import constants as C

run = Path(sys.argv[1])
for step in sys.argv[2:]:
  out = run / f"params_{int(step):012d}.pkl"
  if not out.exists(): model.save_params(str(out), ck.load(str((run / "checkpoints" / f"{int(step):012d}").resolve())))
  params = model.load_params(str(out))
  res = {}
  for name, fn in [("bank", lambda: ev.run_bank(params, 10)), ("r1", lambda: ev.run("r1", params, 10)), ("r2", lambda: ev.run("r2", params, 10))]:
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf): fn()
    res[name] = buf.getvalue().strip().splitlines()[-1].split(":")[1].split("(")[0].strip()
  sim = ev.Sim(params, "joystick"); speeds = []
  for vx in [1.5, 2.0, 2.5]:
    sim.reset(np.random.default_rng(0)); v = []; up = True
    for k in range(300):
      sim.control_step(np.array([vx, 0, 0], np.float32)); up &= bool(sim.upright())
      if k >= 150: v.append(sim.sensor(C.LOCAL_LINVEL_SENSOR)[0])
    speeds.append(f"{vx}->{np.mean(v):.2f}{'' if up else ' FELL'}")
  print(f"{int(step)/1e6:6.1f}M  bank {res['bank']:6s} r1 {res['r1']:6s} r2 {res['r2']:6s}  speed {', '.join(speeds)}", flush=True)
