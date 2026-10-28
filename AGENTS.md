# AGENTS.md — Unity / Unreal agent directives

## 1. Workflow, Tooling & Asset Pipeline
- **Git & Docs:** Work on `main` only (this repo has no `master`). Stage/commit before any sync/pull. Check `docs/` and `tasks.md` first.
- **Automation & Headless:** Drive scene/prefab/level setup via CLI (`tools/ed.sh`, `-batchmode`). Use headless Blender or Blender MCP.
- **UI Changes:** Export an HTML report with annotated before/after screenshots to `SCREENSHOTS/`.
- **Build Watermark:** Show an automated `YYMMDD` build-date watermark bottom-right of the HUD.

### Platform Setup Matrix

| Dimension | Unity Pipeline | Unreal Engine 5 Pipeline |
|---|---|---|
| Background Throttling | Enable **Run In Background**; run headless via CLI/MCP | Disable **Use Less CPU when in Background** |
| Raw Asset Path | `Assets/Art/Raw/` | `Content/Art/Raw/` |
| Export Asset Path | `Assets/Art/Exports/GLB/` | `Content/Art/Exports/` |
| Physics Solver | Bypass PhysX → MuJoCo (`org.mujoco`) | Bypass Chaos → MuJoCo / URLab |
| Neural Inference | Unity Inference Engine (Sentis) / ONNX | NNE / ONNX Runtime / C++ |

### Commands
See the **Commands** section of `CLAUDE.md` for the editor helpers (`tools/ed.sh`), batch builds (`BuildCli.Windows`), headless race tests (`-raceTest`), the Docker trainer, and the run gates (`training/gate.sh`). Always start the trainer with `docker run --init --log-driver none`; without those flags the container became an unkillable zombie.

## 2. Training Lifecycle & Resource Management
- **Trainer:** Train in MuJoCo (Docker trainer in `training/`). Start TensorBoard when a run launches.
- **Headless Execution:** Close the editor for pure RL, runs > 30 min, or to reclaim memory.
- **Pruning & Cleanup:** Delete abandoned run dirs, scratch checkpoints, and logs with < 5,000 steps.
- **Telemetry:** Stream kinematics, actions, observations, and episode metrics to local disk or Azure Table Storage.

## 3. Physics, Collisions & Verification Gates
- **Pre-training Check:** Verify skeletons, joint frames, and bone orientations against the MJCF.
- **Collision Rules:** Primitive colliders only (capsules, spheres, boxes). Enable self-collision on all non-adjacent (non parent/child) pairs. Zero mesh penetration at rest, T-pose, and joint limits.
- **Physical Fidelity:** Gravity −9.81 m/s², anatomical mass distribution, realistic joint/torque/actuator limits.
- **Graduation Gate:** Export ONNX only when mean reward ≥ target for 100 consecutive episodes with zero NaN gradients.
- **HTML Milestones:** For runs > 30 min, generate an HTML report with 3 milestone tiers (Toddler/Beginner, Child/Intermediate, Adult/Advanced), top-3 metric charts, and run-over-run deltas.

## 4. Output Directives
- **Strategic Direction:** If a code change exceeds 100 lines with no pending questions, offer 3 architecture/roadmap questions.
- **TL;DR:** Append a ~20-word TL;DR to responses over 100 words.
