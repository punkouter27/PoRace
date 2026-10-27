"""Matt on CPU MuJoCo with the exported numpy policy: rung eval, graduation gate and the Unity reference trajectory.
Same simulator as g1/sim.py (Matt has the G1 joint layout), switched to Matt's model and the race contract
(4 ms physics, decimation 5). Observation, action, command slew and gait clock are what MattController.cs does.

  python matt/sim.py eval     --params runs/m1/params.pkl [--seeds 10]
  python matt/sim.py graduate --params runs/m1/params.pkl          # 100 episodes, >= 90 % pass, zero NaN
  python matt/sim.py record   --params runs/m1/params.pkl --command 1.2 0 0 --seconds 5 --out /docs/parity/matt_reference.json
"""
import argparse
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import g1.sim as base  # noqa: E402

base.XML = Path(__file__).resolve().parent / "assets" / "matt_porace.xml"
base.SIM_DT, base.DECIMATION = 0.004, 5
base.CTRL_DT = base.SIM_DT * base.DECIMATION
base.CMD_SLEW_STEP = 6.0 * base.CTRL_DT
PUSH = 30.0        # N.s: the race's push bar (G.4 caps chat shoves at 30 N.s); ~0.34 m/s on Matt's 89.6 kg
RACE_VX = (0.8, 1.6)


def graduate(params, episodes=100, first=0):
  """Graduation gate (CLAUDE.md): 100 consecutive episodes, >= 90 % pass, zero NaN. One episode = 12 s of racing:
  standstill start, then three 4 s commands drawn from the race range (vx 0.8-1.6, |vy| <= 0.25, |yaw| <= 0.8,
  the orchestrator's steering envelope). Pass = never fallen and mean velocity error < 0.25 after each 1 s ramp."""
  sim = base.G1Sim(params); passes = 0; nans = 0; errs_all = []
  for ep in range(first, first + episodes):
    rng = np.random.default_rng(ep)
    sim.reset(rng); ok = True; errs = []
    for _ in range(3):
      cmd = [rng.uniform(*RACE_VX), rng.uniform(-0.25, 0.25), rng.uniform(-0.8, 0.8)]
      for k in range(int(4 / base.CTRL_DT)):
        o, act = sim.control_step(cmd)
        if not (np.isfinite(o).all() and np.isfinite(act).all()):
          nans += 1; ok = False; break
        ok &= bool(sim.upright())
        if k >= int(1 / base.CTRL_DT):
          v, w = sim.vel(); errs.append(np.linalg.norm([v[0] - cmd[0], v[1] - cmd[1], 0.5 * (w[2] - cmd[2])]))
      if not ok:
        break
    e = float(np.mean(errs)) if errs else float("inf"); errs_all.append(e)
    passes += ok and e < 0.25
    print(f"seed {ep}: {'pass' if ok and e < 0.25 else 'FAIL'} err {e:.3f}", flush=True)
  need = int(0.9 * episodes)
  fin = [e for e in errs_all if np.isfinite(e)]
  print(f"MATT-GRADUATE: {passes}/{episodes} passed (need {need}), mean err {np.mean(fin):.3f}, NaN episodes {nans} -> "
        f"{'PASS' if passes >= need and nans == 0 else 'FAIL'}")
  return passes >= need and nans == 0


if __name__ == "__main__":
  from brax.io import model as brax_model
  ap = argparse.ArgumentParser(); ap.add_argument("what", choices=["eval", "graduate", "record"]); ap.add_argument("--params", required=True)
  ap.add_argument("--seeds", type=int, default=10); ap.add_argument("--command", type=float, nargs=3, default=[1.2, 0, 0])
  ap.add_argument("--seconds", type=float, default=5.0); ap.add_argument("--out", default="/docs/parity/matt_reference.json")
  ap.add_argument("--first_seed", type=int, default=0)
  a = ap.parse_args(); P = brax_model.load_params(a.params)
  if a.what == "eval": raise SystemExit(0 if base.evaluate(P, a.seeds, push=PUSH, label="MATT") else 1)
  if a.what == "graduate": raise SystemExit(0 if graduate(P, 100, a.first_seed) else 1)
  base.record(P, a.command, a.seconds, a.out, creature="matt")
