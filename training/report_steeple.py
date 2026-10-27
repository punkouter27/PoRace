"""Steeplechase training report (E.3): docs/report/steeple_report.html.

  python report_steeple.py      (in the trainer image: needs tensorboard to read runs/s*/tb)

Reads TensorBoard scalars of runs/s1 and runs/s2, the checkpoint probes (runs/s*_probe*.log from probe_ckpts.py),
the start-point baseline (runs/r7_steeple_baseline.log) and the graduation gate (runs/s2_gate.log, if present).
Three milestone tiers, the three charts that tell the story, and deltas against the starting policy.
"""
import datetime
import glob
import html
import re
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT = Path(__file__).resolve().parent
OUT = Path("/docs/report/steeple_report.html") if Path("/docs").exists() else ROOT.parent / "docs" / "report" / "steeple_report.html"
RUNS = {"s1": "#d97706", "s2": "#2563eb"}
LABEL = {"s1": "s1: obstacles, swing 0.12 m", "s2": "s2: + progress reward, graded field"}


def scalars(run, tag):
  ea = EventAccumulator(str(ROOT / "runs" / run / "tb")); ea.Reload()
  if tag not in ea.Tags()["scalars"]: return []
  return sorted({(e.step, e.value) for e in ea.Scalars(tag)})


PROBE = re.compile(r"^\s*([\d.]+)M\s+steeple (\d+)/(\d+) passed\s+bank (\d+)/10 passed\s+r1 (\d+)/10 passed\s+r2 (\d+)/10 passed\s+speed (.*)$")


def probes(run):
  rows = {}
  for f in sorted(glob.glob(str(ROOT / "runs" / f"{run}_probe*.log"))):
    for line in Path(f).read_text(errors="ignore").splitlines():
      m = PROBE.match(line)
      if m: rows[float(m[1])] = dict(step=float(m[1]), steeple=int(m[2]), of=int(m[3]), bank=int(m[4]), r1=int(m[5]), r2=int(m[6]), speed=m[7].strip())
  return [rows[k] for k in sorted(rows)]


def last_line(path, key):
  p = ROOT / "runs" / path
  if not p.exists(): return None
  hits = [l for l in p.read_text(errors="ignore").splitlines() if key in l]
  return hits[-1].strip() if hits else None


def line_chart(series, title, ylabel, hline=None):
  W, H, L, B, T, R = 680, 300, 60, 40, 40, 20
  pts = [p for _, s, _ in series for p in s]
  if not pts: return f"<p>No data for {html.escape(title)}.</p>"
  x1 = max(x for x, _ in pts) * 1.03; y0 = min(min(y for _, y in pts), hline[0] if hline else 1e9); y1 = max(y for _, y in pts)
  pad = (y1 - y0) * 0.12 or 1; y0 -= pad; y1 += pad
  sx = lambda x: L + x / x1 * (W - L - R); sy = lambda y: H - B - (y - y0) / (y1 - y0) * (H - B - T)
  g = [f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{html.escape(title)}"><text x="{L}" y="22" class="ct">{html.escape(title)}</text>']
  for i in range(5):
    y = y0 + (y1 - y0) * i / 4
    g.append(f'<line x1="{L}" y1="{sy(y):.1f}" x2="{W-R}" y2="{sy(y):.1f}" class="gl"/><text x="{L-6}" y="{sy(y)+4:.1f}" class="tk" text-anchor="end">{y:.3g}</text>')
  for i in range(6):
    x = x1 * i / 5
    g.append(f'<text x="{sx(x):.1f}" y="{H-B+16}" class="tk" text-anchor="middle">{x/1e6:.0f}M</text>')
  g.append(f'<text x="{(W+L)/2:.0f}" y="{H-6}" class="tk" text-anchor="middle">training steps</text>')
  g.append(f'<text transform="translate(14,{(H-B+T)/2:.0f}) rotate(-90)" class="tk" text-anchor="middle">{html.escape(ylabel)}</text>')
  if hline:
    g.append(f'<line x1="{L}" y1="{sy(hline[0]):.1f}" x2="{W-R}" y2="{sy(hline[0]):.1f}" class="bar"/><text x="{W-R-4}" y="{sy(hline[0])-5:.1f}" class="tk" text-anchor="end">{html.escape(hline[1])}</text>')
  for name, s, color in series:
    if len(s) > 1: g.append(f'<polyline fill="none" stroke="{color}" stroke-width="2.5" points="{" ".join(f"{sx(x):.1f},{sy(y):.1f}" for x, y in s)}"/>')
    g += [f'<circle cx="{sx(x):.1f}" cy="{sy(y):.1f}" r="3.5" fill="{color}"><title>{name} {x/1e6:.1f}M: {y:.3g}</title></circle>' for x, y in s]
  g.append("</svg>")
  legend = " ".join(f'<span class="key"><i style="background:{c}"></i>{html.escape(LABEL.get(n, n))}</span>' for n, _, c in series)
  return "\n".join(g) + f'<div class="legend">{legend}</div>'


def main():
  base = last_line("r7_steeple_baseline.log", "STEEPLE:")
  base_pass = int(re.search(r"(\d+)/(\d+) passed", base)[1]) if base else None
  gate = last_line("s2_gate.log", "STEEPLE:")
  gate_ok = gate is not None and "PASS" in gate
  pr = {r: probes(r) for r in RUNS}
  nan = {r: sum(v for _, v in scalars(r, "eval/episode_nan_resets")) for r in RUNS}

  reward = [(r, [(s, v) for s, v in scalars(r, "eval/episode_reward")], c) for r, c in RUNS.items()]
  track = [(r, [(s, v) for s, v in scalars(r, "eval/episode_reward/tracking_lin_vel")], c) for r, c in RUNS.items()]
  steeple = [(r, [(p["step"] * 1e6, 100.0 * p["steeple"] / p["of"]) for p in pr[r]], c) for r, c in RUNS.items()]

  best = max(pr["s2"], key=lambda p: (p["steeple"], p["r1"], p["r2"]), default=None)
  s1best = max(pr["s1"], key=lambda p: (p["steeple"], p["r1"]), default=None)
  def tier(ok, text): return f'<li class="{"ok" if ok else "no"}"><b>{"reached" if ok else "not reached"}</b> {text}</li>'
  beginner = best is not None and best["r1"] >= 9
  intermediate = best is not None and base_pass is not None and best["steeple"] > base_pass
  advanced = gate_ok

  rows = []
  def row(name, p, extra=""):
    if p is None: return
    rows.append(f"<tr><td>{html.escape(name)}</td><td>{p.get('step', '-')}</td><td>{p['steeple']}/{p['of']}</td><td>{p['bank']}/10</td><td>{p['r1']}/10</td><td>{p['r2']}/10</td><td>{html.escape(p['speed'])}</td><td>{extra}</td></tr>")
  if base_pass is not None:
    rows.append(f"<tr><td>start: r7 @ 29.5M (bank policy)</td><td>-</td><td>{base_pass}/20</td><td>6/10</td><td>10/10</td><td>10/10</td><td>1.5->1.36, 2.0->1.52</td><td>rl_optimization_log.md</td></tr>")
  row("s1 best checkpoint", s1best, "gate failed")
  row("s2 best checkpoint", best, html.escape(gate or "gate not run"))

  doc = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Steeplechase Training Report</title><style>
:root{{--bg:#f6f7f9;--card:#fff;--ink:#1d2330;--muted:#5d6675;--line:#dde1e7}}
@media (prefers-color-scheme:dark){{:root{{--bg:#12151b;--card:#1b2029;--ink:#e8ebf0;--muted:#9aa3b2;--line:#2c3340}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 system-ui,-apple-system,"Segoe UI",sans-serif}}
main{{max-width:980px;margin:0 auto;padding:24px 16px 60px}} h1{{font-size:26px;margin:0}} h2{{font-size:19px;margin:30px 0 8px}}
.lede{{color:var(--muted)}} .card{{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px;margin:10px 0;overflow-x:auto}}
svg{{width:100%;height:auto}} .ct{{font-weight:600;fill:var(--ink);font-size:14px}} .tk{{fill:var(--muted);font-size:11px}} .gl{{stroke:var(--line)}} .bar{{stroke:#dc2626;stroke-dasharray:5 4}}
.legend{{font-size:13px;color:var(--muted)}} .key{{margin-right:16px}} .key i{{display:inline-block;width:12px;height:12px;border-radius:2px;margin-right:6px;vertical-align:-1px}}
table{{border-collapse:collapse;width:100%;font-size:14px}} th,td{{text-align:left;padding:7px 9px;border-bottom:1px solid var(--line)}} th{{color:var(--muted)}}
ul.tiers{{list-style:none;padding:0}} ul.tiers li{{padding:8px 10px;border-radius:8px;margin:6px 0;background:var(--card);border:1px solid var(--line)}}
li.ok b{{color:#059669}} li.no b{{color:#dc2626}} code{{font-size:13px}}
</style></head><body><main>
<h1>Steeplechase policy: training report</h1>
<p class="lede">Generated {datetime.date.today():%Y-%m-%d}. Blind (proprioceptive, same 48-dim obs) Go2 locomotion on a 28 x 28 m field of static
MuJoCo boxes: hurdles and steps 3-8 cm, rubble 2-5 cm (<code>training/make_steeple.py</code>). Both runs start from the bank policy
r7 @ 29.5M with pushes, cubes, domain randomization and slopes to 17 deg kept on; 1024 envs on an RTX 2060, editor closed.
Gate: <code>eval.py --rung steeple</code>, 1.0 / 0.8+turn / 1.3 m/s, upright and mean tracking error under 0.35 m/s;
graduation = 90 of 100 consecutive episodes with zero NaN.</p>

<h2>Milestones</h2>
<ul class="tiers">
{tier(beginner, "<b>Toddler / Beginner</b>: still walks at the commanded speed on flat ground after obstacle training (R1 9/10 or better).")}
{tier(intermediate, f"<b>Child / Intermediate</b>: beats the starting policy on the obstacle field (more than {base_pass}/20).")}
{tier(advanced, "<b>Adult / Advanced</b>: graduation gate, 90/100 episodes and zero NaN; ONNX exported for Map 04.")}
</ul>

<h2>Top 3 metrics</h2>
<div class="card">{line_chart(steeple, "Steeplechase pass rate per checkpoint (20 episodes)", "% passed", hline=(90, "graduation 90 %"))}</div>
<div class="card">{line_chart(reward, "Eval episode reward", "reward")}</div>
<div class="card">{line_chart(track, "Velocity tracking reward (the term that collapses when the policy creeps or freezes)", "reward term")}</div>

<h2>Deltas against the starting policy</h2>
<div class="card"><table><tr><th>policy</th><th>step (M)</th><th>steeple</th><th>bank 15 deg</th><th>R1 flat</th><th>R2 push+cube</th><th>speed asked -> reached</th><th>note</th></tr>
{"".join(rows)}</table></div>
<p class="lede">NaN resets during evaluation: s1 {nan["s1"]:.0f}, s2 {nan["s2"]:.0f}.</p>

<h2>What happened</h2>
<div class="card"><p><b>s1 failed.</b> With a 0.12 m swing target and only the Gaussian tracking term, the policy learned to cross the field slowly
(34M: 0/20 steeple, every episode about 0.45 m/s under the command) and then to stand still: from 68.8M it no longer walks at 1.5 m/s even on flat
ground (R1 5/10, then 2/10). On rough ground a fall costs more than creeping, and exp(-err^2/0.25) has almost no slope once the error is large,
so nothing pulled the speed back up. Training reward still rose (15.2 to 16.4), which is why the reward curve alone looked fine.</p>
<p><b>s2 changes:</b> a progress term (fraction of the commanded speed made along the command, 0..1, weight 1, steeple only), the original 0.10 m
swing target, and a field whose obstacle heights grow from 25 % at the centre to full height at the edge, so every batch mixes easy and hard ground.</p></div>
</main></body></html>"""
  OUT.parent.mkdir(parents=True, exist_ok=True)
  OUT.write_text(doc)
  print("wrote", OUT)


if __name__ == "__main__":
  main()
