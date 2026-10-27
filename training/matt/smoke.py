"""Smoke test of the Matt env on the GPU: build, reset, 100 zero-action steps (jit), timing and contact use."""
import time
import jax, jax.numpy as jp
from matt import env as mattenv

cfg = mattenv.default_config(); cfg.naconmax = mattenv.CONTACTS_PER_ENV * 64
env = mattenv.MattJoystick(config=cfg)
print("obs", env.observation_size, "act", env.action_size, "dt", env.dt, "sim_dt", env.sim_dt)
reset = jax.jit(jax.vmap(env.reset)); step = jax.jit(jax.vmap(env.step))
s = reset(jax.random.split(jax.random.PRNGKey(0), 64))
a = jp.zeros((64, env.action_size))
s = step(s, a); jax.block_until_ready(s.obs)
t = time.time(); n = 0
for _ in range(100):
  s = step(s, a); n += 1
jax.block_until_ready(s.obs)
print(f"100 steps x 64 worlds in {time.time() - t:.2f}s; done {int(s.done.sum())}/64; pelvis z mean {float(s.data.qpos[:, 2].mean()):.3f}; "
      f"reward finite {bool(jp.isfinite(s.reward).all())}; nacon max {int(s.data.nacon.max()) if hasattr(s.data, 'nacon') else 'n/a'}")
