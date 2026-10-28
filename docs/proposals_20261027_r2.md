# PoRace proposals, round 2, 2026-10-27

This file has 50 options in five tracks. They follow on from `proposals_20261027.md` (round 1) and do not repeat it. Round 1 already covers heightfield steeple, stamina, drafting, MPPI planner, Cinemachine, the safe-area root, the GameFlow state machine, muscle actuation, heel-to-toe gait and others.

Each entry says, in plain words, why it matters (**Why**), what in the sim drives it (**Hook**), and what to build it with (**Tools**). ★ marks the ones to do first.

## A. Features (tension, immersion, smarter racers)

1. ★ **Commentary captions from race events.** Why: viewers get a voice telling the story, even with the sound off. Hook: `Overtook`, `Fell`, `Recovered` and `Finished` events, `StrainMonitor` REDLINING, and gap changes go into a small template bank ("Red takes Blue into turn 2!"), with cooldowns so captions don't spam. Tools: no new package; an optional TTS later (Android `TextToSpeech`, Windows SAPI).
2. ★ **Lap-delta gate timing.** Why: the classic "+0.42 behind your best" box creates tension every few seconds. Hook: sub-tick crossing times at every 10 m gate along `MapDefinition`, compared with the stored best ghost (`RaceReplay` already keeps one per map). Tools: none.
3. **Recovery race.** Why: a fall turns into a mini-drama ("can he get up before Green passes?"). Hook: `RaceState.FALLEN_RECOVERING` plus the getup policy's time-to-upright, shown as a countdown ring over the fallen racer, with the record getup per map. Tools: none.
4. **Track hazards that move.** Why: a reason to rewatch, since every race plays out differently. Hook: MuJoCo mocap bodies (a swinging gate or sliding barrier) driven from C# in `ctrlCallback`; the obstacle stays a primitive box geom. The steeple policy already handles static boxes, so start slow. Tools: `mocap="true"` bodies in the flattened MJCF.
5. **Pit lane / cool-down zone.** Why: a pacing choice that pairs with round-1 stamina. Hook: a lane where the cruise cap drops and stamina refills faster; tactics decide when to pit, based on remaining distance. Tools: a `MapDefinition` field and a tactic in `MultiRaceOrchestrator`.
6. ★ **Creature-vs-creature handicap.** Why: Matt and four dogs racing evenly is the hook of the show. Hook: per-creature start offsets solved from each policy's measured cruise speed, so expected finish times match; the result is displayed as "Matt +12 m head start". Tools: none; read `CreatureDefinition`.
7. **Season mode.** Why: a reason to come back, with points tables and rivalries. Hook: a series of seeded races across the map registry; points per place; `RaceTelemetry` summaries roll up into a season JSON. Tools: persistentDataPath, plus the optional Azure Table already wired.
8. **Spectator "nudge" vote, physically bounded.** Why: the crowd influences the race without breaking it. Hook: a vote picks which racer gets a +5 % cruise boost for 10 s, clamped inside the policy's trained command range (≤ 2.5 m/s for Go2, Matt's own cap). Tools: the existing `ChatLink`.
9. **Racer cam picture-in-picture.** Why: two stories on screen at once (leader and battle). Hook: `RaceDirector` picks the secondary subject from its battle scoring and renders it to a small RenderTexture. Mobile tier at 1/4 resolution, 15 fps. Tools: a second Camera and a RawImage.
10. **Waypoint-free navigation by track SDF.** Why: racers that steer from the track shape itself, ready for open maps. Hook: a 2D signed-distance field baked from the centreline and rails; the command comes from the SDF gradient plus progress, not from the heading-error-to-waypoint logic. Tools: bake in `TrackBuilder`, sample in C#.

## B. Graphics & audio (mobile first)

1. ★ **Speed lines + FOV kick.** Why: a cheap way to make speed feel fast. Hook: leader base speed vs. cruise; a +4° FOV and a radial line overlay above 2 m/s. Tools: a URP full-screen pass on PC; on Mobile, a single additive UI sprite.
2. ★ **Motor heat shimmer.** Why: you can *see* a racer working hard. Hook: `RaceSensors` effort (|force|/24) above 0.8 drives a small distortion sprite on the hot joint. Tools: URP Shader Graph refraction on PC; Mobile skips it (or uses a tint pulse only).
3. **Footfall pressure rings on the track.** Why: shows gait rhythm at a glance. Hook: `ContactImpulse` foot strikes, impulse → ring radius, stamped into the round-1 trail map. Tools: the same RenderTexture.
4. **Finish-line confetti burst.** Why: a payoff moment. Hook: `Finished` event and finish place (1st gets the big burst). Tools: one pooled ParticleSystem, ≤ 200 particles on Mobile.
5. **Rim-light team colours.** Why: racers read at a glance on small screens. Hook: each `Racer.color` drives a subtle Fresnel rim (reusing the `PoRace/StrainRim` shader path at low intensity). Tools: an existing shader.
6. ★ **Procedural servo layer keyed on joint velocity.** Why: every joint sounds alive, without sample libraries. Hook: per-leg |q̇| and torque into a band-passed noise and sine whine (OnAudioFilterRead), with pitch following q̇. Tools: built-in audio; 1 voice per racer on Mobile.
7. **Crowd reaction stingers.** Why: the crowd feels like it is *watching*. Hook: a short gasp on a fall, cheers on an overtake, a rising roar in the last 10 m. All triggered by events, with cooldowns. Tools: CC0 crowd clips (freesound.org CC0 filter), 3 clips each.
8. **Surface-aware dust colour.** Why: maps look distinct. Hook: the world geom name already gives the surface (`ContactImpulse`), mapped to a dust palette (tan dirt, grey concrete, no dust on wet). Tools: existing `ImpactFx` particles.
9. **Photogrammetry-style track textures via Blender bake.** Why: a richer look without runtime cost. Hook: none (art). Tools: headless Blender bakes Poly Haven CC0 materials into 1K atlases. The `.blend` goes in `Assets/Art/Raw/` and GLB in `Assets/Art/Exports/GLB/`.
10. **Adaptive audio mix by tier.** Why: Mobile keeps clarity. Hook: `GfxTier` caps voices (8 Mobile / 24 PC) with priority = event impulse × proximity. Tools: AudioSource.priority plus a voice manager.

## C. UI (portrait, zero scroll)

1. ★ **One-thumb radial hazard picker.** Why: three hazard buttons become one press-and-slide in the BR thumb zone. Hook: `HazardDesk` cooldowns show as a ring per wedge. Tools: uGUI.
2. **Standings as colour chips.** Why: fits 5 racers in one row at the top. Hook: chip = colour + place + a tiny effort bar, with a gap in seconds under the leader. Tools: uGUI layout group.
3. ★ **Automatic text scale check.** Why: crisp text on every phone. Hook: `ShotProbe` captures at 3 DPIs and asserts every label is at least 11 pt physical size and none overflows its RectTransform. Tools: the existing `-shots` probe.
4. **Haptics on key events.** Why: you feel the race (Android). Hook: a short buzz when your bet racer passes or falls, and a strong one on the finish. Tools: `Handheld.Vibrate` or the Android VibrationEffect via JNI.
5. **Pause sheet.** Why: one place for Pause / Replay last 8 s / Restart / Menu. Hook: `RaceReplay` is ready; `Time.timeScale = 0` while the physics step is paused. Tools: uGUI bottom sheet.
6. **Orientation lock + landscape fallback.** Why: tablets and desktop users get a sane layout. Hook: portrait is the primary layout; on landscape the standings move to a left column and the sheet to the right. Tools: one `ScreenOrientation` handler.
7. **First-run coach marks.** Why: new viewers learn the 3 gestures in 5 seconds. Hook: shown once, flagged in PlayerPrefs. Tools: uGUI overlays.
8. **Colour-blind safe palette toggle.** Why: 5 racer colours must stay distinguishable. Hook: swap `RaceConfig` colours for an Okabe-Ito set, with shape glyphs added to the chips. Tools: none.
9. ★ **UI binding self-test at boot.** Why: catches the null-reference and missing-wiring class of bug. Hook: in `-raceTest`, reflect over every `[SerializeField]`/public UI field on HUD components and log any nulls as errors (it fails the test). Tools: C# reflection.
10. **Compact countdown → GO → result transition.** Why: one animated element instead of three text swaps. Hook: `PhaseChanged` events drive a single TC badge. Tools: uGUI + a small tween.

## D. Natural-animal realism

1. ★ **Contact-force sanity gate.** Why: the race should look physically believable. Hook: per-foot normal force peaks vs. body weight (human walking ~1.2×, running ~2.5×; Go2 trot ~1.5×) checked in `eval.py`. A policy that stomps at 5× fails. Tools: MuJoCo `cfrc_ext`/contact forces.
2. ★ **Symmetry loss for Matt.** Why: limping or asymmetric gaits look wrong instantly. Hook: mirror the obs and action (left↔right) and penalise ‖π(mirror(s)) − mirror(π(s))‖. Tools: a few lines in `training/matt/env.py` (mirror map from `MattSpec` joint order).
3. **Torso stability reward.** Why: humans keep head and torso steady while running. Hook: penalise torso angular acceleration and head-height variance at speed. Tools: the gyro sensor already in obs.
4. **Arm swing coupling.** Why: natural counter-rotation instead of rigid or flailing arms. Hook: reward shoulder pitch in anti-phase with the opposite hip (correlation over the gait cycle). Tools: none.
5. **Foot clearance target.** Why: no toe-dragging, and clean steps over steeple boxes. Hook: swing-foot height ≥ 5 cm at mid-swing (Go2), ≥ 8 cm (Matt). Tools: foot site positions, already sensors.
6. **Actuator thermal model.** Why: realistic "fade" over a long race, and it feeds the strain visuals. Hook: per-motor temperature from ∫τ² dt with cooling; torque limits derate above a threshold. Run the same model in training DR and in Unity. Tools: C# + JAX, a few lines each.
7. ★ **Reflex delay randomisation.** Why: robustness to latency and human-like reaction. Hook: in training, randomise observation delay 0–20 ms and action delay 0–1 control step; in Unity, the policy runs on the 50 Hz step, so this protects parity margins. Tools: env wrapper.
8. **Terrain-aware stride.** Why: shorter strides on slopes and banks, like real animals. Hook: reward stride length scaled by cos(slope) on bank maps; tilt-gravity training (Phase F.8) already exists. Tools: none.
9. **Natural stop and start.** Why: no instant 0→2 m/s. Hook: limit commanded acceleration in training to human/dog values (~2 m/s² jog start) and match `CmdSlewStep` in Unity. Tools: command sampler.
10. **Motion-capture reference prior (Matt only).** Why: the gait looks human, not just "works". Hook: an AMP-style discriminator reward against a CC0 walking/jogging clip retargeted to Matt's 22-bone rig. Tools: CMU MoCap (free), headless Blender retarget, a small discriminator in the trainer.

## E. CLEAN (measured 2026-10-27)

Current footprint: `Builds/` 818 MB, `Build/` 532 MB, `training/runs/` 236 MB (415 entries), `Logs/` 120 MB, `SCREENSHOTS/` 19 MB, `Assets/Policies` 4.6 MB (7 ONNX). `Build/` and `Builds/` are gitignored.

1. ★ **Prune `training/runs/`.** Keep only runs referenced in `rl_optimization_log.md` or shipped as policies. Delete runs with < 5,000 steps and their TB logs (the CLAUDE.md rule), then report count and MB. Script it as `training/prune_runs.py --dry-run`.
2. ★ **One build folder.** `Build/` and `Builds/` both hold players (1.35 GB). Pick `Build/` (what BuildCli and the docs use), delete stale players in `Builds/`, and keep only the last 2 builds per target.
3. **Rotate `Logs/`.** Unity editor and player logs total 120 MB; keep 7 days.
4. **Audit the policies.** `go2_loco_bank`, `go2_loco_fast` and `zero_policy` have no scene/prefab GUID references. Confirm they are assigned by path (`PolicyAssign`, `MapDefinition`) or used by tests (zero policy = B.9 harness) before removing any. `go2_loco_fast` is pending a parity check (F.9), so keep it.
5. ★ **One command-line parser.** About 25 hand-written `args[i] == "-x"` loops across 20 files, with inconsistent bounds checks and error handling. Add a static `CliArgs` (`Has`, `TryFloat`, `TryInt`, `TryEnum`, `RaceTest`).
6. ★ **Creature facts on `IRacer`, not `matt != null`.** About 15 `r.matt != null` branches in the orchestrator, sensors, contacts, strain and HUD. Move yaw envelope, foot geoms, actuator ids, force maxima and flat-only terrain onto IRacer / `CreatureDefinition`, filled by each controller (they stay separate classes). This is the change that lets G1 join a race.
7. **One owner for the MuJoCo options and the sim clock.** `ls_iterations`, `impratio`, `ccd_iterations` and `Time.fixedDeltaTime` are written by several controllers. Give the scene one contract component and use `m->opt.timestep` instead of `Go2Controller.SimDt` in the broadcast code.
8. **Remove dead code (confirm first).** `Predictions.cs` is no longer added by `BroadcastBuilder`. `RaceOrchestrator` only drives `Track01.unity`, which is not in the build. `RaceSensors.FootStrike` / `CubeImpact` have no listeners. Delete them, or re-wire them if the feature should ship.
9. **Fail loudly instead of `FindAnyObjectByType` fallbacks.** About 25 fallbacks in `Broadcast/` hide wiring mistakes; `BroadcastBuilder` wires everything. Replace them with asserts (pairs with C.9).
10. **Zero-allocation UI text.** Standings, director label, weather text and ticker rebuild strings every frame. Rebuild them only when values change, or at 5–10 Hz, and add a GC-alloc check to `FrameProbe` (target: 0 B/frame while racing).
