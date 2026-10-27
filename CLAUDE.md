# PoRace agent directives (Unity)

Read `docs/` and `tasks.md` before starting a task.

## Workflow
- Work on `main` (this repo has no `master`). Stage and commit pending work before any git pull/sync.
- Build watermark: show an automated `YYMMDD` build-date watermark in the bottom-right of the HUD.
- Keep Unity's **Run In Background** enabled. Drive headless runs and automation through the Unity CLI (`tools/ed.sh`, `-batchmode` player builds).
- Build scenes, prefabs and hierarchies with CLI automation. Use headless Blender (or Blender MCP) when the main scene needs changes.
- Blender source files go in `Assets/Art/Raw/`. Runtime GLB/FBX exports go in `Assets/Art/Exports/GLB/`.
- For UI changes, export an HTML change report with annotated before/after captures to `SCREENSHOTS/`.

## Training & runtime
- Train in MuJoCo (Docker trainer in `training/`). Unity never simulates with PhysX: use `org.mujoco` (`mjModel`/`mjData`) plus ONNX inference (Unity Inference Engine).
- Before training, check skeletons, joint frames and bone orientations against the MJCF. Use primitive colliders (capsule/sphere/box) only, never visual meshes.
- Close the Unity editor during pure RL training, for runs over 30 min, or whenever it frees critical memory.
- Delete abandoned run folders, dummy checkpoints and TensorBoard logs under 5,000 steps.
- Start TensorBoard when a run launches. Export ONNX only after graduation: mean reward ≥ target for 100 consecutive episodes, zero NaN gradients.
- For sessions over 30 min, write an HTML report: 3 milestone tiers (Toddler/Beginner, Child/Intermediate, Adult/Advanced), top-3 metric charts, and deltas versus the previous run.
- Log kinematics, joint actions, observations and episode metrics to local disk (or Azure Table Storage).

## Physics
- Gravity −9.81 m/s². Use realistic mass distributions, joint limits and torque/actuator ranges.
- No collision penetration at rest, T-pose or joint-limit extremes. Turn on self-collision for all segment pairs except directly connected parent/child links.

## Output
- After changes over 100 lines, when the user hasn't asked anything else, propose 3 optional high-level questions (product direction, architecture, roadmap).
- End any reply over 100 words with a ~20-word TL;DR.
