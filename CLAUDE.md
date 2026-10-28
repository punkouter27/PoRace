# CLAUDE.md

@AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# PoRace agent directives (Unity)

Read `docs/` and `tasks.md` before starting a task. `AGENTS.md` holds the same rules for other agents; keep the two in sync.

## Commands
- Editor automation (Git Bash, editor open): `source tools/ed.sh`, then `recompile`, `ueval 'C#;'`, `ulog <substr>`, `ubuild Build/X/PoRace.exe '["Assets/Scenes/A.unity"]'`.
- Player build, editor closed: `Unity.exe -batchmode -quit -projectPath . -executeMethod BuildCli.Windows [-buildPath Build/PoRace/PoRace.exe] [-scenes A.unity,B.unity]` (the first scene is the start scene). Unity 6000.6.0f1.
- Headless race test: `Build/PoRace/PoRace.exe -batchmode -nographics -raceTest [-timeScale 4] [-seed 7] [-racers 4 | -dogMask 5] [-matt] [-laps 2] [-noTactics]`. Other probes: `-replay <json>` (parity), `-parityDir <dir>`, `-shots <dir>`, `-frameProbe 20`, `-gfx mobile|pc`.
- Editor tests: `Assets/Tests/Editor` (`NoPhysXTest`); run them with Unity's `-runTests -testPlatform EditMode -testFilter <Name>`.
- Trainer (Docker image `porace-trainer`): `docker run --rm --init --log-driver none --gpus all -v <repo>\training:/work porace-trainer python train.py --env joystick --logdir runs/<name>`. Use `--init --log-driver none`, because without them the container became an unkillable zombie.
- Gate a run (export ONNX, eval, reference, Unity replay): `training/gate.sh runs/<name>`. Variants: `gate100.sh`, `gatespeed.sh`, `gateall.sh`. Matt has its own ladder probe in `training/matt/probe_ckpts.py`.

## Codebase map
- `Assets/Scripts/`: runtime. `Go2Controller` / `MattController` / `G1Controller` (obs build, 50 Hz policy on `MjScene.ctrlCallback`), `PolicyRunner` (ONNX), `MultiRaceOrchestrator` (1-5 racers in one MuJoCo model, tactics), `MapDefinition` + `CreatureDefinition` registries (`Assets/Resources/{Maps,Creatures}`). `Broadcast/`: spectator layer (director cams, strain, hazards/chat, predictions, weather, replay, telemetry, audio, VFX).
- `Assets/Editor/`: CLI builders (`TrackBuilder`, `BroadcastBuilder`, `CreatureBuilder`, `MenuBuilder`, `BuildCli`, `WebGLBuild`). Scenes are composed by these, not by hand.
- `Assets/MuJoCo/`: Unity-flattened MJCF (`training/flatten_for_unity.py`). `Assets/Policies/`: shipped ONNX.
- `training/`: Docker trainer (`train.py --env ...`, `eval.py --rung ...`, `export_onnx.py`), Go2 envs in `go2/`, Matt in `matt/`, G1 in `g1/`. Runs land in `training/runs/` (gitignored). History and decisions: `rl_optimization_log.md`.
- Gates: replay parity < 1e-4 (`ReplayHarness`), `NoPhysXTest`, headless race tests (`PoRace.exe -batchmode -nographics -raceTest`).
- Proposals backlog: `docs/proposals_*.md`.

## Workflow
- Work on `master`. Stage and commit pending work before any git pull/sync.
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
