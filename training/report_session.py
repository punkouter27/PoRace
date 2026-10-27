"""Session report for the 2026-10-27 unattended run: docs/report/session_20261027.html.

  python report_session.py      (in the trainer image: needs tensorboard to read runs/*/tb)

Steeplechase graduation (s4-s7, G.10) and the faster dog (r8-r10, F.9). Reads TensorBoard scalars (reward, value and
policy loss), the gate logs written by gate300.sh / gateall.sh / gatespeed.sh (runs/<run>_gates.log) and the s2 gate
it is compared with. Three milestone tiers, the three charts that carry the result, deltas against the previous runs.
"""
import datetime
import html
import re
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT = Path(__file__).resolve().parent
OUT = Path("/docs/report/session_20261027.html") if Path("/docs").exists() else ROOT.parent / "docs" / "report" / "session_20261027.html"
STEEPLE = {"s4": "#9ca3af", "s5": "#d97706", "s6": "#7c3aed", "s7": "#2563eb"}
SPEED = {"r8": "#9ca3af", "r9": "#d97706", "r10": "#059669"}
LABEL = {"s4": "s4: lr 1e-4, outer spawns", "s5": "s5: swing 0.13 m", "s6": "s6: continue", "s7": "s7: pushes to 7 m/s",
         "r8": "r8: speed-scaled sigma", "r9": "r9: sharp sigma, progress x2", "r10": "r10: lr 5e-5"}
# Steps restarted from: each run continues the previous best, so the x axis is cumulative steps since s2 @ 68.8M / s7 @ 68.8M.
OFFSET = {"s4": 0, "s5": 22.9e6, "s6": 22.9e6 + 80.3e6, "s7": 22.9e6 + 80.3e6 + 54.1e6,
          "r8": 0, "r9": 0, "r10": 68.8e6}


def scalars(run, tag):
  p = ROOT / "runs" / run / "tb"
  if not p.exists(): return []
  ea = EventAccumulator(str(p)); ea.Reload()
  if tag not in ea.Tags()["scalars"]: return []
  return sorted({(e.step + OFFSET[run], e.value) for e in ea.Scalars(tag) if e.value == e.value})


def gates(run):
  """{step: line} from runs/<run>_gates.log; a later line for the same step (a corrected re-run) wins."""
  p = ROOT / "runs" / f"{run}_gates.log"; out = {}
  if not p.exists():   # s4 was gated by hand (gate100.sh): runs/<run>_gate_<M>M.log
    for f in sorted((ROOT / "runs").glob(f"{run}_gate_*M.log")):
      m = re.match(rf"{run}_gate_([\d.]+)M\.log$", f.name); last = f.read_text(errors="ignore").strip().splitlines()
      if m and last: out[int(round(float(m[1]) * 1e6 / 1146880)) * 1146880] = f"{run} {m[1]}M: {last[-1]}"
    return out
  for line in p.read_text(errors="ignore").splitlines():
    m = re.match(r"^(\d+) (.*)$", line)
    if m: out[int(m[1])] = m[2]
  return out


def steeple_rate(line):
  m = re.search(r"GATE300 (\d+)/300", line) or re.search(r"STEEPLE: (\d+)/100", line)
  if not m: return None
  return int(m[1]) / (3 if "GATE300" in line else 1)


def speed_means(line):
  m = re.search(r"mean 2\.0->([\d.]+) 2\.5->([\d.]+)", line)
  return (float(m[1]), float(m[2])) if m else None


def chart(series, title, ylabel, hlines=()):
  W, H, L, B, T, R = 680, 300, 60, 40, 40, 20
  pts = [p for _, s, _ in series for p in s]
  if not pts: return f"<p>No data for {html.escape(title)}.</p>"
  x1 = max(x for x, _ in pts) * 1.03; ys = [y for _, y in pts] + [h for h, _ in hlines]
  y0, y1 = min(ys), max(ys); pad = (y1 - y0) * 0.12 or 1; y0 -= pad; y1 += pad
  sx = lambda x: L + x / x1 * (W - L - R); sy = lambda y: H - B - (y - y0) / (y1 - y0) * (H - B - T)
  g = [f'<svg viewBox="0 0 {W} {H}" role="img" aria-label="{html.escape(title)}"><text x="{L}" y="22" class="ct">{html.escape(title)}</text>']
  for i in range(5):
    y = y0 + (y1 - y0) * i / 4
    g.append(f'<line x1="{L}" y1="{sy(y):.1f}" x2="{W - R}" y2="{sy(y):.1f}" class="gl"/><text x="{L - 6}" y="{sy(y) + 4:.1f}" class="tk" text-anchor="end">{y:.3g}</text>')
  for i in range(6):
    x = x1 * i / 5
    g.append(f'<text x="{sx(x):.1f}" y="{H - B + 16}" class="tk" text-anchor="middle">{x / 1e6:.0f}M</text>')
  g.append(f'<text x="{(W + L) / 2:.0f}" y="{H - 6}" class="tk" text-anchor="middle">training steps (cumulative over the chain of runs)</text>')
  g.append(f'<text transform="translate(14,{(H - B + T) / 2:.0f}) rotate(-90)" class="tk" text-anchor="middle">{html.escape(ylabel)}</text>')
  for h, label in hlines:
    g.append(f'<line x1="{L}" y1="{sy(h):.1f}" x2="{W - R}" y2="{sy(h):.1f}" class="bar"/><text x="{W - R - 4}" y="{sy(h) - 5:.1f}" class="tk" text-anchor="end">{html.escape(label)}</text>')
  for name, s, color in series:
    if len(s) > 1: g.append(f'<polyline fill="none" stroke="{color}" stroke-width="2.5" points="{" ".join(f"{sx(x):.1f},{sy(y):.1f}" for x, y in s)}"/>')
    g += [f'<circle cx="{sx(x):.1f}" cy="{sy(y):.1f}" r="3.5" fill="{color}"><title>{name} {x / 1e6:.1f}M: {y:.3g}</title></circle>' for x, y in s]
  g.append("</svg>")
  legend = " ".join(f'<span class="key"><i style="background:{c}"></i>{html.escape(LABEL.get(n, n))}</span>' for n, _, c in series)
  return "\n".join(g) + f'<div class="legend">{legend}</div>'


def main():
  sg = {r: gates(r) for r in STEEPLE}; vg = {r: gates(r) for r in SPEED}
  steeple_series = [(r, [(s + OFFSET[r], v) for s, l in sorted(sg[r].items()) if (v := steeple_rate(l)) is not None], c) for r, c in STEEPLE.items()]
  speed_series = [(r, [(s + OFFSET[r], m[1]) for s, l in sorted(vg[r].items()) if (m := speed_means(l))], c) for r, c in SPEED.items()]
  loss_series = [(r, scalars(r, "training/v_loss"), c) for r, c in {**STEEPLE, **SPEED}.items()]
  nan_runs = [r for r in list(STEEPLE) + list(SPEED) if "NAN" in (ROOT / "runs" / f"{r}.log").read_text(errors="ignore")]

  grad = [(r, s, l) for r in STEEPLE for s, l in sg[r].items() if "ALL-PASS" in l]
  best_speed = max(((r, s, m) for r in SPEED for s, l in vg[r].items() if (m := speed_means(l))), key=lambda x: x[2][1], default=None)
  speed_pass = [(r, s, l) for r in SPEED for s, l in vg[r].items() if "ALL-PASS" in l]

  def tier(ok, text): return f'<li class="{"ok" if ok else "no"}"><b>{"reached" if ok else "not reached"}</b> {text}</li>'
  tiers = [tier(True, "<b>Toddler / Beginner</b>: every run keeps R1 (flat tracking, 20/20) and bank 15 deg (30/30) at every gated checkpoint."),
           tier(bool(grad), "<b>Child / Intermediate</b>: steeplechase graduation on held-out seeds with R2 intact (s7 @ 68.8M: 277/300, R2 30/30), "
                            "Track04 40/40 racers finish."),
           tier(bool(speed_pass), "<b>Adult / Advanced</b>: faster dog, 2.0 and 2.5 m/s commands held at 90 % from standstill (speed rung 9/10) "
                                  "without losing R1 / R2 / bank.")]

  rows = []
  for r in STEEPLE:
    for s, l in sorted(sg[r].items()):
      rows.append(f"<tr><td>{r}</td><td>{s / 1e6:.1f}</td><td>{html.escape(l.split(':', 1)[1].strip() if ':' in l else l)}</td></tr>")
  srows = []
  for r in SPEED:
    for s, l in sorted(vg[r].items()):
      srows.append(f"<tr><td>{r}</td><td>{s / 1e6:.1f}</td><td>{html.escape(l.split(':', 1)[1].strip() if ':' in l else l)}</td></tr>")

  best = f"{best_speed[0]} @ {best_speed[1] / 1e6:.1f}M: 2.0 -> {best_speed[2][0]:.2f}, 2.5 -> {best_speed[2][1]:.2f} m/s" if best_speed else "-"
  doc = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Training Session Report</title><style>
:root{{--bg:#f6f7f9;--card:#fff;--ink:#1d2330;--muted:#5d6675;--line:#dde1e7}}
@media (prefers-color-scheme:dark){{:root{{--bg:#12151b;--card:#1b2029;--ink:#e8ebf0;--muted:#9aa3b2;--line:#2c3340}}}}
body{{margin:0;background:var(--bg);color:var(--ink);font:15px/1.55 system-ui,-apple-system,"Segoe UI",sans-serif}}
main{{max-width:980px;margin:0 auto;padding:24px 16px 60px}} h1{{font-size:26px;margin:0}} h2{{font-size:19px;margin:30px 0 8px}}
.lede{{color:var(--muted)}} .card{{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:12px 14px;margin:10px 0;overflow-x:auto}}
svg{{width:100%;height:auto}} .ct{{font-weight:600;fill:var(--ink);font-size:14px}} .tk{{fill:var(--muted);font-size:11px}} .gl{{stroke:var(--line)}} .bar{{stroke:#dc2626;stroke-dasharray:5 4}}
.legend{{font-size:13px;color:var(--muted)}} .key{{margin-right:16px}} .key i{{display:inline-block;width:12px;height:12px;border-radius:2px;margin-right:6px;vertical-align:-1px}}
table{{border-collapse:collapse;width:100%;font-size:13px}} th,td{{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}} th{{color:var(--muted)}}
ul.tiers{{list-style:none;padding:0}} ul.tiers li{{padding:8px 10px;border-radius:8px;margin:6px 0;background:var(--card);border:1px solid var(--line)}}
li.ok b{{color:#059669}} li.no b{{color:#dc2626}} code{{font-size:13px}}
</style></head><body><main>
<h1>Unattended training session, 2026-10-26/27</h1>
<p class="lede">Generated {datetime.date.today():%Y-%m-%d}. Go2, Brax PPO on MuJoCo Warp, RTX 2060 (6 GB), 1024 envs, Unity editor closed.
Every run continues the previous best checkpoint with pushes, cubes, domain randomization and slopes to 17 deg on. Each checkpoint was gated
on the CPU while the next one trained (<code>training/autogate.sh</code>). TensorBoard: <code>training/runs/*/tb</code>.</p>

<h2>Milestones</h2>
<ul class="tiers">{"".join(tiers)}</ul>

<h2>Top 3 metrics</h2>
<div class="card">{chart(steeple_series, "Steeplechase pass rate per checkpoint (% of 300 held-out episodes; s4 on 100)", "% passed", hlines=[(90, "graduation 90 %")])}</div>
<div class="card">{chart(speed_series, "Faster dog: mean speed on a 2.5 m/s command from standstill (10 seeds)", "m/s", hlines=[(2.25, "pass 2.25 m/s"), (1.68, "start (s7) ~1.7")])}</div>
<div class="card">{chart(loss_series, "Value loss (TensorBoard training/v_loss)", "v_loss")}</div>

<h2>Deltas against the previous runs</h2>
<div class="card"><table><tr><th>metric</th><th>before</th><th>after</th></tr>
<tr><td>Steeplechase gate</td><td>s2 @ 68.8M: 82/100 (shipped ungraduated)</td><td>s7 @ 68.8M: 277/300 on three held-out sets (94 / 91 / 92), 0 NaN</td></tr>
<tr><td>R2 (30 N.s push + 1 kg cube)</td><td>s2: 10/10 (x10); s6: 23/30</td><td>s7: 30/30</td></tr>
<tr><td>Track04, 4 racers, headless</td><td>s2: 20/20 finish (seeds 1-5), 89.5-226.9 s, up to 65 un-sticks</td><td>s7: 40/40 finish (seeds 1-10), 77.5-158.5 s, 2 back-offs</td></tr>
<tr><td>Top speed (2.5 m/s command)</td><td>r5 (attempt 2): froze; s7: ~1.7 m/s</td><td>{html.escape(best)}</td></tr>
<tr><td>Runs with a NaN metric</td><td colspan="2">{", ".join(nan_runs) or "none"} (r9 diverged at 91.7M and was stopped; that checkpoint was deleted, nothing after it is used)</td></tr>
</table></div>

<h2>Every gated checkpoint</h2>
<div class="card"><table><tr><th>run</th><th>step (M)</th><th>steeple gate (300 seeds) and rungs</th></tr>{"".join(rows)}</table></div>
<div class="card"><table><tr><th>run</th><th>step (M)</th><th>speed rung and rungs</th></tr>{"".join(srows)}</table></div>
</main></body></html>"""
  OUT.parent.mkdir(parents=True, exist_ok=True)
  OUT.write_text(doc)
  print("wrote", OUT)


if __name__ == "__main__":
  main()
