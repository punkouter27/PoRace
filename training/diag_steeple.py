"""Per-phase breakdown of steeplechase gate seeds: python diag_steeple.py <params.pkl> <seed> [<seed>...]
Same episode as eval.run_steeple; prints per phase the mean tracking error, the share of 0.1 s windows below 30 % of
the commanded speed (stalled) and the position, plus the tallest obstacle within 0.4 m of the base when stalled."""
import sys
import mujoco
import numpy as np
from brax.io import model
import eval as ev
from go2 import constants as C

params = model.load_params(sys.argv[1])
sim = ev.Sim(params, "joystick", C.ASSET_PATH / "scene_steeple.xml")
m = sim.m
boxes = [g for g in range(m.ngeom) if m.geom_bodyid[g] == 0 and m.geom_type[g] == mujoco.mjtGeom.mjGEOM_BOX]
def tallest_near(p):
  h = 0.0
  for g in boxes:
    c = sim.d.geom_xpos[g]; s = m.geom_size[g]
    if abs(c[0] - p[0]) < s[0] + 0.4 and abs(c[1] - p[1]) < s[1] + 0.4: h = max(h, c[2] + s[2])   # axis-aligned approx
  return h
for seed in map(int, sys.argv[2:]):
  rng = np.random.default_rng(seed); sim.reset(rng)
  sim.d.qpos[0:2] = rng.uniform(-12, 12, 2); sim.d.qpos[2] += 0.1
  yaw = rng.uniform(-np.pi, np.pi); sim.d.qpos[3:7] = [np.cos(yaw / 2), 0, 0, np.sin(yaw / 2)]
  mujoco.mj_forward(sim.m, sim.d); out = []
  for cmd in ([1.0, 0, 0], [0.8, 0, 0.6], [1.3, 0, 0]):
    cmd = np.array(cmd, np.float32); errs = []; vx = []; stall_h = []
    for k in range(int(4 / C.CTRL_DT)):
      sim.control_step(cmd)
      if k >= int(1 / C.CTRL_DT):
        v = sim.sensor(C.LOCAL_LINVEL_SENSOR); w = sim.sensor(C.GYRO_SENSOR)
        errs.append(np.linalg.norm([v[0] - cmd[0], v[1] - cmd[1], 0.5 * (w[2] - cmd[2])])); vx.append(v[0])
        if v[0] < 0.3 * cmd[0]: stall_h.append(tallest_near(sim.d.qpos[:2]))
    out.append(f"cmd {cmd[0]:.1f}/{cmd[2]:.1f}: err {np.mean(errs):.2f} vx {np.mean(vx):.2f} stalled {len(stall_h) / len(errs) * 100:3.0f}%"
               f" (max obstacle {max(stall_h, default=0) * 100:.0f} cm) at ({sim.d.qpos[0]:.1f},{sim.d.qpos[1]:.1f})")
  print(f"seed {seed}: " + " | ".join(out), flush=True)
