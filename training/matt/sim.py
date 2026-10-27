"""Matt on CPU MuJoCo with the exported numpy policy: rung eval, graduation gate and the Unity reference trajectory.
Same simulator as g1/sim.py (Matt has the G1 joint layout), switched to Matt's model and the race contract
(4 ms physics, decimation 5). Observation, action, command slew and gait clock are what MattController.cs does.

  python matt/sim.py eval     --params runs/m1/params.pkl [--seeds 10]
  python matt/sim.py graduate --params runs/m1/params.pkl          # 100 episodes, >= 90 % pass, zero NaN
  python matt/sim.py ladder   --params runs/m6/params.pkl [--upto child] [--ladder_out /docs/matt/ladder_m6.json]
  python matt/sim.py record  --params runs/m1/params.pkl --command 1.2 0 0 --seconds 5 --out /docs/parity/matt_reference.json
"""
import argparse
import json
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


# Matt's milestone ladder (project report tiers). Each tier must pass before training the next one is worth a run.
#   toddler (balance / recovery): stand 10 s; 30 N.s push standing and while walking 0.5 m/s, still up 4 s later
#   child (natural walk): 0.5 -> 1.0 -> 1.5 m/s from standstill, 5 s each: tracking, left/right stance symmetry,
#                         no knee / hand / torso on the floor
#   adult (jog, turn, slope): 2.5 m/s jog, 1.0 m/s with a 0.8 rad/s turn, 1.0 m/s on a 10 % grade
# Fallen = pelvis below 0.7 m (the knees_bent hold height in check_model.py) or the torso tipped past horizontal:
# kneeling counts as fallen, which a bare up-vector test lets through.
PELVIS_MIN = 0.7
SYMMETRY_MAX = 0.05   # |stance share L - R| / mean
TIERS = ("toddler", "child", "adult")


def episode(sim, rng, segments, push_at=None, grade=0.0, ramp=1.0):
  """Run [(command, seconds), ...] from a fresh reset. Returns upright, mean velocity error (each segment after a
  `ramp` s settle), stance symmetry index, control steps with a non-foot floor contact, mean |action jerk|."""
  sim.reset(rng); sim.m.opt.gravity[:] = sim.g0
  if grade:  # a slope as tilted gravity over the flat floor (as the Go2 bank rung); random heading to the fall line
    az, t = rng.uniform(0, 2 * np.pi), np.arctan(grade)
    sim.m.opt.gravity[:] = 9.81 * np.array([np.sin(t) * np.cos(az), np.sin(t) * np.sin(az), -np.cos(t)])
  ok, errs, stance, other, jerk, acts, push, n = True, [], np.zeros(2), 0, [], [], 0, 0
  for cmd, secs in segments:
    for k in range(int(secs / base.CTRL_DT)):
      if push_at is not None and n == int(push_at / base.CTRL_DT):
        ang = rng.uniform(0, 2 * np.pi)
        sim.d.xfrc_applied[sim.pelvis, :3] = np.array([np.cos(ang), np.sin(ang), 0]) * PUSH / 0.1; push = int(round(0.1 / base.SIM_DT))
      _, a = sim.control_step(cmd); n += 1
      if push > 0:
        push -= base.DECIMATION
        if push <= 0: sim.d.xfrc_applied[:] = 0
      ok &= bool(sim.upright()) and sim.d.qpos[2] > PELVIS_MIN
      feet, nonfoot = np.zeros(2, bool), False
      for g1, g2 in zip(sim.d.contact.geom1[:sim.d.ncon], sim.d.contact.geom2[:sim.d.ncon]):
        if sim.floor not in (g1, g2): continue
        g = g2 if g1 == sim.floor else g1
        if g in sim.feet: feet[sim.feet.index(g)] = True
        else: nonfoot = True
      other += nonfoot
      acts.append(a)
      if len(acts) >= 3: jerk.append(float(np.mean(np.abs(acts[-1] - 2 * acts[-2] + acts[-3]))))
      if k >= int(ramp / base.CTRL_DT):
        v, w = sim.vel(); errs.append(np.linalg.norm([v[0] - cmd[0], v[1] - cmd[1], 0.5 * (w[2] - cmd[2])]))
        if abs(cmd[0]) > 0.3: stance += feet
  sim.m.opt.gravity[:] = sim.g0
  share = stance / max(stance.sum(), 1)
  sym = abs(share[0] - share[1]) / max(share.mean(), 1e-6) if stance.sum() else 0.0
  return dict(ok=bool(ok), err=float(np.mean(errs)) if errs else 0.0, sym=float(sym), nonfoot=int(other),
              jerk=float(np.mean(jerk)) if jerk else 0.0)


def ladder(params, seeds=10, upto="adult", out=None):
  """Run the tiers in order up to `upto`; stops at the first failing tier (the next one would not mean anything)."""
  sim = base.G1Sim(params); sim.g0 = sim.m.opt.gravity.copy()
  sim.floor = sim.m.geom("floor").id; sim.feet = [sim.m.geom("left_foot").id, sim.m.geom("right_foot").id]
  checks = {
    "toddler": [("stand 10 s", seeds, lambda r: episode(sim, r, [([0, 0, 0], 10)]), lambda e: e["ok"] and not e["nonfoot"]),
                ("push standing", int(0.9 * seeds), lambda r: episode(sim, r, [([0, 0, 0], 6)], push_at=2), lambda e: e["ok"]),
                ("push walking", int(0.9 * seeds), lambda r: episode(sim, r, [([0.5, 0, 0], 6)], push_at=2), lambda e: e["ok"])],
    "child": [("walk 0.5/1.0/1.5", int(0.9 * seeds), lambda r: episode(sim, r, [([v, 0, 0], 5) for v in (0.5, 1.0, 1.5)]),
               lambda e: e["ok"] and e["err"] < 0.2 and e["sym"] < SYMMETRY_MAX and not e["nonfoot"])],
    "adult": [("jog 2.5", int(0.9 * seeds), lambda r: episode(sim, r, [([2.5, 0, 0], 6)], ramp=2.0), lambda e: e["ok"] and e["err"] < 0.3),
              ("turn 0.8 rad/s", int(0.9 * seeds), lambda r: episode(sim, r, [([1.0, 0, 0.8], 6)]), lambda e: e["ok"] and e["err"] < 0.25),
              ("10 % grade", int(0.9 * seeds), lambda r: episode(sim, r, [([1.0, 0, 0], 8)], grade=0.10), lambda e: e["ok"] and e["err"] < 0.25)],
  }
  report, passed_all = {}, True
  for tier in TIERS[:TIERS.index(upto) + 1]:
    tier_ok = True
    for name, need, run, ok in checks[tier]:
      eps = [run(np.random.default_rng(s)) for s in range(seeds)]
      passes = sum(bool(ok(e)) for e in eps)
      stat = {k: round(float(np.mean([e[k] for e in eps])), 4) for k in ("err", "sym", "nonfoot", "jerk")}
      report[f"{tier}/{name}"] = dict(passes=passes, need=need, **stat)
      print(f"MATT-{tier.upper()} {name}: {passes}/{seeds} (need {need}) -> {'PASS' if passes >= need else 'FAIL'}  "
            f"err {stat['err']:.3f} sym {stat['sym']:.3f} nonfoot {stat['nonfoot']:.1f} jerk {stat['jerk']:.3f}", flush=True)
      tier_ok &= passes >= need
    passed_all &= tier_ok
    if not tier_ok:
      print(f"MATT-LADDER: stopped at {tier}"); break
  if out:
    Path(out).parent.mkdir(parents=True, exist_ok=True); Path(out).write_text(json.dumps(report, indent=1))
  return passed_all


if __name__ == "__main__":
  from brax.io import model as brax_model
  ap = argparse.ArgumentParser(); ap.add_argument("what", choices=["eval", "graduate", "record", "ladder"]); ap.add_argument("--params", required=True)
  ap.add_argument("--upto", choices=TIERS, default="adult"); ap.add_argument("--ladder_out", default=None)
  ap.add_argument("--seeds", type=int, default=10); ap.add_argument("--command", type=float, nargs=3, default=[1.2, 0, 0])
  ap.add_argument("--seconds", type=float, default=5.0); ap.add_argument("--out", default="/docs/parity/matt_reference.json")
  ap.add_argument("--first_seed", type=int, default=0)
  a = ap.parse_args(); P = brax_model.load_params(a.params)
  if a.what == "eval": raise SystemExit(0 if base.evaluate(P, a.seeds, push=PUSH, label="MATT") else 1)
  if a.what == "graduate": raise SystemExit(0 if graduate(P, 100, a.first_seed) else 1)
  if a.what == "ladder": raise SystemExit(0 if ladder(P, a.seeds, a.upto, a.ladder_out) else 1)
  base.record(P, a.command, a.seconds, a.out, creature="matt")
