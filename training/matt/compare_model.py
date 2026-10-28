"""Diff the model Unity compiles for Matt against the trainer's, element by element (by name).
  Unity: MattTestbed PoRace.exe -batchmode -nographics -seconds 0.1 -dumpModel <abs>/docs/parity/unity_matt_model.mjb
  python matt/compare_model.py /docs/parity/unity_matt_model.mjb
Prints only differences (relative tolerance 1e-5); the race model also holds the dogs and cubes, the testbed only Matt."""
import sys
from pathlib import Path

import mujoco
import numpy as np

A = mujoco.MjModel.from_xml_path(str(Path(__file__).resolve().parent / "assets" / "matt_porace.xml"))
B = mujoco.MjModel.from_binary_path(sys.argv[1])
O = mujoco.mjtObj
n_diff = 0


def cmp(label, a, b, tol=1e-5):
  global n_diff
  a, b = np.atleast_1d(np.asarray(a, float)), np.atleast_1d(np.asarray(b, float))
  if "quat" in label and a.shape == b.shape and a.size == 4 and np.allclose(a, -b, atol=tol): return   # q and -q: same rotation
  if a.shape != b.shape or not np.allclose(a, b, rtol=tol, atol=tol):
    n_diff += 1; print(f"DIFF {label}: train={np.round(a, 6).tolist()} unity={np.round(b, 6).tolist()}")


for f in ("timestep", "impratio", "iterations", "ls_iterations", "integrator", "cone", "solver", "tolerance", "noslip_iterations",
          "ccd_iterations", "disableflags", "enableflags"):
  cmp("opt." + f, getattr(A.opt, f), getattr(B.opt, f))
cmp("opt.gravity", A.opt.gravity, B.opt.gravity)
print("sizes nq nv nu nbody ngeom nexclude:", (A.nq, A.nv, A.nu, A.nbody, A.ngeom, A.nexclude), (B.nq, B.nv, B.nu, B.nbody, B.ngeom, B.nexclude))


def ids(m, t, n): return {mujoco.mj_id2name(m, t, i): i for i in range(n) if mujoco.mj_id2name(m, t, i)}


for name, i in ids(A, O.mjOBJ_BODY, A.nbody).items():
  j = mujoco.mj_name2id(B, O.mjOBJ_BODY, name)
  if j < 0: print("MISSING body", name); continue
  for f in ("body_pos", "body_quat", "body_mass", "body_inertia", "body_ipos", "body_iquat"): cmp(f"{f}[{name}]", getattr(A, f)[i], getattr(B, f)[j])
for name, i in ids(A, O.mjOBJ_JOINT, A.njnt).items():
  j = mujoco.mj_name2id(B, O.mjOBJ_JOINT, name)
  if j < 0: print("MISSING joint", name); continue
  for f in ("jnt_type", "jnt_pos", "jnt_axis", "jnt_range", "jnt_limited", "jnt_stiffness", "jnt_solref", "jnt_solimp", "jnt_margin"):
    cmp(f"{f}[{name}]", getattr(A, f)[i], getattr(B, f)[j])
  da, db = A.jnt_dofadr[i], B.jnt_dofadr[j]; nd = 6 if A.jnt_type[i] == 0 else 1
  for f in ("dof_damping", "dof_armature", "dof_frictionloss"): cmp(f"{f}[{name}]", getattr(A, f)[da:da + nd], getattr(B, f)[db:db + nd])
for name, i in ids(A, O.mjOBJ_GEOM, A.ngeom).items():
  j = mujoco.mj_name2id(B, O.mjOBJ_GEOM, name)
  if j < 0: print("MISSING geom", name); continue
  for f in ("geom_type", "geom_size", "geom_pos", "geom_quat", "geom_friction", "geom_condim", "geom_solref", "geom_solimp", "geom_margin",
            "geom_gap", "geom_priority", "geom_solmix", "geom_contype", "geom_conaffinity"):
    cmp(f"{f}[{name}]", getattr(A, f)[i], getattr(B, f)[j])
for name, i in ids(A, O.mjOBJ_ACTUATOR, A.nu).items():
  j = mujoco.mj_name2id(B, O.mjOBJ_ACTUATOR, name)
  if j < 0: print("MISSING actuator", name); continue
  for f in ("actuator_gainprm", "actuator_biasprm", "actuator_dynprm", "actuator_ctrlrange", "actuator_forcerange", "actuator_ctrllimited",
            "actuator_forcelimited", "actuator_gaintype", "actuator_biastype", "actuator_dyntype", "actuator_gear"):
    cmp(f"{f}[{name}]", getattr(A, f)[i][:3] if f.endswith("prm") else getattr(A, f)[i], getattr(B, f)[j][:3] if f.endswith("prm") else getattr(B, f)[j])
ex = lambda m: sorted(tuple(sorted((mujoco.mj_id2name(m, O.mjOBJ_BODY, int(k >> 16)), mujoco.mj_id2name(m, O.mjOBJ_BODY, int(k & 0xFFFF)))))
                      for k in m.exclude_signature)
if ex(A) != ex(B): n_diff += 1; print("DIFF excludes:", set(ex(A)) ^ set(ex(B)))
print(f"MATT MODEL: {n_diff} differences")
