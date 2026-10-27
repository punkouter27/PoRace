"""Matt training session report: docs/report/matt_session_20261027.html.

  python report_matt.py      (in the trainer image: needs tensorboard to read runs/*/tb)

Matt's locomotion runs (tasks E.4): m4 on the dogs' contact contract, m5 / m6 on impratio 1: eval reward, time upright and velocity tracking from TensorBoard,
the CPU rung evals and the graduation gate (runs/m6_gates.log, written by the eval commands), and the Unity parity
gates (docs/parity/matt_parity.log). Three milestone tiers, the three charts that carry the result, deltas against the
last run on the dogs' contact contract (m4) and between checkpoints. m1-m3 (see rl_optimization_log.md) are deleted.
"""
import datetime
import html
import re
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT = Path(__file__).resolve().parent
DOCS = Path("/docs") if Path("/docs").exists() else ROOT.parent / "docs"
OUT = DOCS / "report" / "matt_session_20261027.html"
RUNS = {"m4": ("#9ca3af", "m4: impratio 100 (floor forces up to 700x body weight)", 0.0),
        "m5": ("#2563eb", "m5: impratio 1 probe", 0.0),
        "m6": ("#ea8a2b", "m6: continues m5 @ 40.5M", 40.46848e6)}
CHARTS = [("eval/episode_reward", "Eval episode reward", "higher is better; the termination penalty dominates while he falls"),
          ("eval/avg_episode_length", "Eval episode length (control steps, max 1000)", "1000 = upright for the whole 20 s episode"),
          ("eval/episode_reward/tracking_lin_vel", "Linear-velocity tracking reward", "how well he follows the joystick command")]


def scalars(run, tag):
  p = ROOT / "runs" / run / "tb"
  if not p.exists():
    return []
  ea = EventAccumulator(str(p), size_guidance={"scalars": 0}); ea.Reload()
  return [(e.step + RUNS[run][2], e.value) for e in ea.Scalars(tag)] if tag in ea.Tags()["scalars"] else []


def chart(tag, title, note):
  series = {r: scalars(r, tag) for r in RUNS}
  pts = [p for s in series.values() for p in s]
  if not pts:
    return f"<p>No data for {html.escape(tag)} yet.</p>"
  x0, x1 = 0.0, max(p[0] for p in pts) or 1.0
  y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
  if y1 - y0 < 1e-9: y1 = y0 + 1
  W, H, L, B = 640, 260, 64, 34
  X = lambda x: L + (x - x0) / (x1 - x0) * (W - L - 12)
  Y = lambda y: H - B - (y - y0) / (y1 - y0) * (H - B - 12)
  g = [f'<line x1="{L}" y1="{H - B}" x2="{W - 12}" y2="{H - B}" class="ax"/><line x1="{L}" y1="12" x2="{L}" y2="{H - B}" class="ax"/>']
  for k in range(5):
    v = y0 + (y1 - y0) * k / 4
    g.append(f'<text x="{L - 6}" y="{Y(v) + 4:.1f}" text-anchor="end">{v:,.0f}</text><line x1="{L}" y1="{Y(v):.1f}" x2="{W - 12}" y2="{Y(v):.1f}" class="grid"/>')
    xv = x0 + (x1 - x0) * k / 4
    g.append(f'<text x="{X(xv):.1f}" y="{H - B + 18}" text-anchor="middle">{xv / 1e6:.0f}M</text>')
  for r, s in series.items():
    if s:
      d = " ".join(f"{'M' if i == 0 else 'L'}{X(x):.1f},{Y(y):.1f}" for i, (x, y) in enumerate(s))
      g.append(f'<path d="{d}" fill="none" stroke="{RUNS[r][0]}" stroke-width="2.5"/>')
      x, y = s[-1]; g.append(f'<circle cx="{X(x):.1f}" cy="{Y(y):.1f}" r="3.5" fill="{RUNS[r][0]}"/>')
  legend = " ".join(f'<span class="key"><i style="background:{RUNS[r][0]}"></i>{html.escape(RUNS[r][1])}</span>' for r in RUNS if series[r])
  return (f'<figure><figcaption><b>{html.escape(title)}</b> <span class="note">{html.escape(note)}</span></figcaption>'
          f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{html.escape(title)}">{"".join(g)}</svg><div>{legend}</div></figure>')


def last(run, tag):
  s = scalars(run, tag)
  return s[-1] if s else (None, None)


def read(p):
  return Path(p).read_text() if Path(p).exists() else ""


def main():
  gates = read(ROOT / "runs" / "m6_gates.log"); parity = read(DOCS / "parity" / "matt_parity.log")
  r0 = re.findall(r"MATT-R0: (\d+)/(\d+)", gates); r1 = re.findall(r"MATT-R1: (\d+)/(\d+).*?err ([\d.]+)", gates)
  r2 = re.findall(r"MATT-R2: (\d+)/(\d+)", gates); grad = re.findall(r"MATT-GRADUATE: (\d+)/(\d+).*?NaN episodes (\d+) -> (\w+)", gates)
  ok = lambda m, need_all=True: bool(m) and (int(m[-1][0]) >= (int(m[-1][1]) if need_all else int(0.9 * int(m[-1][1]))))
  tiers = [
    ("Toddler / Beginner", "Stands: 10 s on a zero command, every seed (MATT-R0); eval episodes reach the 1000-step limit.",
     ok(r0), f"R0 {r0[-1][0]}/{r0[-1][1]}" if r0 else "not evaluated yet"),
    ("Child / Intermediate", "Walks and steers: standstill start to 1.0 m/s plus four random commands, never falls, mean velocity error < 0.25 (MATT-R1); survives a 30 N.s shove (MATT-R2).",
     ok(r1) and ok(r2, False), (f"R1 {r1[-1][0]}/{r1[-1][1]} (err {r1[-1][2]}), " if r1 else "R1 -, ") + (f"R2 {r2[-1][0]}/{r2[-1][1]}" if r2 else "R2 -")),
    ("Adult / Advanced", "Races: graduation gate 100 consecutive race-range episodes, >= 90 % pass, zero NaN; Unity replay + closed-loop parity; finishes a race with the dogs.",
     bool(grad) and grad[-1][3] == "PASS" and "PASS" in parity, (f"graduate {grad[-1][0]}/{grad[-1][1]}, NaN {grad[-1][2]}; " if grad else "graduate -; ") + ("parity: " + ", ".join(re.findall(r"(\w[\w -]*PARITY[^\n]*|MATT CLOSED LOOP \w+|\[Replay\] \w+)", parity)) if parity else "parity -")),
  ]
  rows = []
  for tag, title, _ in CHARTS:
    (sa, a), (sb, b) = last("m4", tag), last("m6", tag)
    first_b = scalars("m6", tag)
    d = f"{b - a:+,.1f}" if a is not None and b is not None else "-"
    dd = f"{b - first_b[0][1]:+,.1f}" if first_b and b is not None else "-"
    rows.append(f"<tr><td>{html.escape(title)}</td><td>{'-' if a is None else f'{a:,.1f}'}</td><td>{'-' if b is None else f'{b:,.1f}'}</td><td>{d}</td><td>{dd}</td></tr>")
  steps = last("m6", "eval/episode_reward")[0]
  page = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Matt Training Session</title><style>
:root{{--bg:#fbfaf8;--fg:#1f2328;--mut:#5f6670;--line:#e3e0da;--ok:#15803d;--no:#b45309}}
@media (prefers-color-scheme:dark){{:root{{--bg:#16181c;--fg:#e8e6e3;--mut:#9aa1ab;--line:#30343b;--ok:#4ade80;--no:#fbbf24}}}}
body{{background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,sans-serif;max-width:980px;margin:0 auto;padding:24px 16px}}
h1{{font-size:26px;margin:0 0 4px}}.note,.mut{{color:var(--mut)}}figure{{margin:18px 0}}svg{{width:100%;height:auto}}
svg text{{fill:var(--mut);font-size:11px}}.ax{{stroke:var(--mut)}}.grid{{stroke:var(--line)}}.key{{margin-right:14px;font-size:13px}}
.key i{{display:inline-block;width:12px;height:12px;border-radius:2px;margin-right:5px;vertical-align:-1px}}
table{{border-collapse:collapse;width:100%}}td,th{{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left}}
.tier{{border:1px solid var(--line);border-radius:8px;padding:10px 14px;margin:8px 0}}.ok{{color:var(--ok);font-weight:600}}.no{{color:var(--no);font-weight:600}}
</style></head><body>
<h1>Matt: first locomotion training session</h1>
<p class="mut">Generated {datetime.date.today()} by training/report_matt.py. Body: tools/blender/matt_mjcf.py (1.89 m, 89.6 kg, 29 joints in the
G1 layout, capsules / spheres / boxes, full self-collision); env: training/matt/env.py (stock G1 joystick rewards, race physics
4 ms / 50 Hz). Latest eval at {'-' if steps is None else f'{steps / 1e6:.1f}M'} cumulative steps.</p>
<h2>Milestones</h2>
{''.join(f'<div class="tier"><b>{html.escape(n)}</b> <span class="{"ok" if p else "no"}">{"reached" if p else "not yet"}</span><br><span class="mut">{html.escape(desc)}</span><br>{html.escape(ev)}</div>' for n, desc, p, ev in tiers)}
<h2>Top metrics</h2>
{''.join(chart(*c) for c in CHARTS)}
<h2>Deltas</h2>
<table><tr><th>Metric</th><th>m4 last</th><th>m6 last</th><th>m6 vs m4</th><th>m6 since its first eval</th></tr>{''.join(rows)}</table>
<h2>Gate logs</h2><pre>{html.escape(gates[-3000:] or "none yet")}</pre><pre>{html.escape(parity[-2000:] or "no Unity parity run yet")}</pre>
</body></html>"""
  OUT.parent.mkdir(parents=True, exist_ok=True); OUT.write_text(page)
  print("wrote", OUT)


if __name__ == "__main__":
  main()
