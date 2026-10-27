"""Why does the dog top out? python diag_speed.py <params.pkl> [vx...]: flat ground, command vx from standstill for 6 s;
over the last 3 s prints the mean speed, the share of control steps each joint group spends at the actuator force limit
(|force| >= 98 % of forcerange) and the peak |qdot| per group, over 3 seeds."""
import sys

import numpy as np
from brax.io import model

import eval as ev
from go2 import constants as C

params = model.load_params(sys.argv[1])
sim = ev.Sim(params, "joystick"); m = sim.m
fr = m.actuator_forcerange[:12, 1]
for vx in map(float, sys.argv[2:] or ["1.5", "2.0", "2.5"]):
  sat = np.zeros(12); qd = np.zeros(12); speeds = []; n = 0
  for seed in range(3):
    sim.reset(np.random.default_rng(seed)); cmd = np.array([vx, 0, 0], np.float32)
    for k in range(int(6 / C.CTRL_DT)):
      sim.control_step(cmd)
      if k >= int(3 / C.CTRL_DT):
        sat += np.abs(sim.d.actuator_force[:12]) >= 0.98 * fr; n += 1
        qd = np.maximum(qd, np.abs(sim.d.qvel[6:18])); speeds.append(sim.sensor(C.LOCAL_LINVEL_SENSOR)[0])
  s = [float((sat / n)[i::3].mean()) for i in range(3)]; q = [float(qd[i::3].max()) for i in range(3)]
  print(f"cmd {vx:.1f}: speed {np.mean(speeds):.2f} | at force limit hip {s[0] * 100:3.0f}% thigh {s[1] * 100:3.0f}% calf {s[2] * 100:3.0f}%"
        f" | peak qdot hip {q[0]:.1f} thigh {q[1]:.1f} calf {q[2]:.1f} rad/s | forcerange {fr[0]:.0f}/{fr[1]:.0f}/{fr[2]:.0f} N.m", flush=True)
