# PoRace proposals, 2026-10-27

40 options in four tracks. These build on what tasks.md already ships: RaceDirector cuts, Predictions, ChatLink hazards, TrackWeather, RaceReplay and ghosts, ContactImpulse, RaceAudio, StrainRim, scuff decals, and Matt on the grid.
Each entry gives: **Appeal**, **Hook** (sim/telemetry input) and **Tools** (free packages/assets/APIs). ★ = recommended first.

## A. Features (spectator tension, immersion, agent navigation)

1. ★ **Heightfield steeple (E.3).** Appeal: racers that actually *see* the hurdles they clear. Today's s7 steeple policy runs blind. Hook: an 11×7 height scan ahead of the base (MuJoCo `hfield` + ray sensors), and the clearance margin per obstacle goes to the HUD. Tools: mujoco_playground rough-terrain env as a template; org.mujoco `MjHeightField`.
2. ★ **Battery / stamina pacing.** Appeal: strategy you can see (conserve, then a late surge) and "he's fading!" moments. Hook: integrate `RaceSensors` power per racer; the drain caps the cruise command. No retraining, because the policy already tracks any command ≤ 2.5 m/s. Tools: none; it's a `MultiRaceOrchestrator` speed cap plus a HUD bar.
3. **Slipstream / drafting.** Appeal: passing becomes tactical, which gives the commentary something to work with. Hook: `ExternalForces` (already the single writer of base `xfrc_applied`) reduces the headwind drag for a racer inside a cone behind a rival. Wind is already ≤ 10 N and inside the training DR.
4. ★ **Short-horizon racing-line planner.** Appeal: smarter overtakes and fewer pile-ups than heading-error steering. Hook: an MPPI-style sampler over (vx, vy, yaw rate) using rival base positions and velocities from `qpos`/`qvel`, scored on progress, collision risk and effort. Tools: Unity Burst + Jobs (free) for 64 rollouts of a kinematic model per control step.
5. **Rival personalities.** Appeal: viewers pick favourites ("the blocker", "the gambler"). Hook: seed per-racer aggression, block propensity and risk into the existing pass/block tactics, shown as HUD badges and folded into the Predictions odds.
6. **Photo finish.** Appeal: drama on close finishes. Hook: interpolate the finish-line crossing between 4 ms physics steps (sub-tick time) and render a slit-scan strip from the replay ring buffer. Tools: `RaceReplay` snapshot/restore plus one RenderTexture.
7. ★ **Cinemachine 3 camera rigs.** Appeal: broadcast-grade dolly, trackside and orbit shots with less custom code. Hook: spline dollies generated from `MapDefinition` centerlines; `RaceDirector` picks the virtual camera instead of moving one camera. Tools: `com.unity.cinemachine` 3.x and `com.unity.splines` (free registry packages).
8. **Live win-probability graph + auto highlight reel.** Appeal: the "momentum swing" graphic from sports broadcasts. Hook: `Predictions` live win % at 2 Hz as a sparkline; the three largest swings trigger `RaceReplay` clips at the finish.
9. **Async ghost leaderboard.** Appeal: race other people's best runs; replay value. Hook: 10 Hz base-pose ghosts (already in persistentDataPath) uploaded per map with a seed, then downloaded as ghost racers. Tools: Azure Table/Blob (the telemetry path exists), or Unity Gaming Services Leaderboards (free tier).
10. **Twitch polls and channel points.** Appeal: structured, fair audience control instead of chat spam. Hook: the poll result picks a hazard zone or weather, reusing HazardDesk caps (30 N·s). Tools: Twitch EventSub (WebSocket), which replaces anonymous IRC for points and polls.

## B. GFX & audio (mobile-first)

1. ★ **Cinemachine Impulse for shake.** Replace the hand-rolled camera shake with an impulse source fed by `ContactImpulse` J (racer-scaled, distance-attenuated). It's cheap on mobile and stays consistent across camera cuts.
2. ★ **Trail-map footprints.** One 1024² RenderTexture per track that foot strikes stamp into (dust on dry, darkening on wet), sampled by the track shader. It scales to thousands of marks at a fixed cost, where the pooled quads cap at 24/64.
3. **Thermal replay view.** In replay, toggle a false-colour mode: per-link `MaterialPropertyBlock` value = effort (|force|/24), smoothed. This extends StrainRim from "hot links only" to a full gradient for analysis shots.
4. **Weather-driven sky and time of day.** Open-Meteo already returns `is_day`, cloud cover and precipitation, so drive a gradient skybox, fog density and sun angle from them. Night maps get two tracking spotlights (leader and battle) on PC, and blob shadows plus one realtime spot on mobile.
5. **Wet-track look.** Rain ramps a global `_Wetness` value: lower albedo, higher smoothness, and screen-space puddle reflections only on the PC tier. The same value already drives friction ×0.7–1.
6. **Steam Audio spatialisation.** HRTF and grandstand occlusion for impacts and crowd. Steam Audio is free and supports Android/Windows. Fall back to Unity's built-in panner on WebGL.
7. **Reactive crowd bed + reverb zones.** Crowd gain and excitement layer follow the `RaceDirector` event score and the gap to the finish; add an `AudioReverbZone` under each grandstand. Both are built in and cost nothing.
8. **Granular surface friction audio.** Drive the slip scrape with slip speed × normal force through a small grain synth (OnAudioFilterRead), so steel ramps, grass and wet tarmac each get a distinct texture without sample libraries.
9. **Blender prop kit v2.** Headless scripts (like `trackside_props.py`) for barriers, cones, banners and a finish gantry with LOD0/1. Archive the `.blend` in `Assets/Art/Raw/` and export GLB to `Assets/Art/Exports/GLB/`. Use CC0 textures from the Poly Haven API.
10. ★ **Crisp type.** Move HUD text from `LegacyRuntime.ttf` uGUI Text to TextMeshPro SDF (or UI Toolkit), with Inter plus a tabular-figure mono for timers (both OFL). Text stays sharp at every DPI and digits stop jittering. On Android, add `com.unity.adaptiveperformance` for thermal-aware quality stepping.

## C. UI (mobile portrait, zero scroll)

The HUD is uGUI at a 720×1280 reference with **no safe-area handling** (`Screen.safeArea` appears nowhere in the scripts).

1. ★ **Safe-area root.** One `SafeAreaFitter` RectTransform under the HUD canvas that re-fits on resolution or orientation change. Every anchor parents to it. Verify with Device Simulator at 16:9, 19.5:9, 20:9 and 21:9 with punch-hole and notch cutouts.
2. ★ **Anchor remap to the spec.** TL title + compact standings strip · TC FPS / step tick + race clock · TR settings drawer · BL telemetry overlay · BR `YYMMDD`. Today BL holds Drop/Cube/Shove/Restart, so those move to (3).
3. **Tabbed bottom sheet** (peek 96 px, thumb zone): Race · Hazards · Bets · Telemetry. One sheet replaces the scattered button rows and the betting modal.
4. **Badges over text.** State pills (RACING / STUMBLE / RECOVERING) as coloured icons, effort as a ring per racer, and PASS/BLOCK as arrow glyphs. This saves roughly 40% of horizontal space in standings.
5. **TR settings drawer.** Gfx tier, volume, camera mode, chat link, weather override. These secondary prefs leave the HUD, with defaults set the way they are today.
6. **Single-screen pre-race.** Map carousel + racer/lap steppers + Start as an overlay in the race scene. This removes the `Menu.unity` load and one transition that can race.
7. ★ **One owner for game flow.** A `GameFlow` state machine (Menu → Countdown → Racing → Finished → Reset) that every component subscribes to. An edit-mode test cycles it 50× and asserts no null bindings, no duplicate listeners and no orphaned coroutines.
8. **Result card.** Placing, time, CoT, best split, and Rematch (same `-seed`) / New race / Menu, all in the thumb zone.
9. **BL telemetry overlay.** Leader speed sparkline plus an effort bar per racer; tap to expand into the full TAB telemetry.
10. ★ **Layout regression in CI.** `ShotProbe` captures at 5 aspect ratios with cutout masks, and an overlap checker asserts no RectTransform intersections between anchor groups. It writes the before/after HTML report to `SCREENSHOTS/`, as CLAUDE.md requires.

## D. Natural-animal realism (Matt first; Go2 where it applies)

Matt's MJCF already has knee 0–2.4 rad (no hyperextension), Winter segment masses and primitive geoms. The gaps below are about motion quality.

1. ★ **Joint-limit audit vs. human ROM.** Ankle pitch is −0.6..0.8 rad. Check the sign convention against the human ~20° dorsiflexion / ~50° plantarflexion, and also hip yaw and lumbar. Extend `matt/check_model.py` with a pose sweep (rest, T-pose, every limit extreme) that asserts zero penetration, with self-collision on for all non-parent/child pairs.
2. ★ **Muscle-like actuation.** Use first-order activation (`dyntype="filter"`, τ ≈ 40 ms) and a torque–velocity falloff (gain scaled by 1 − |q̇|/q̇max). Joints get smooth onset and no instant torque reversals.
3. **Fatigue envelope.** Per-joint torque budget that decays with integrated positive work and recovers at rest; the fatigue level goes into the observations. This pairs with Feature 2 (stamina pacing).
4. **Anti-flail shaping.** Penalties on the action second difference (jerk), arm-swing energy beyond the counter-rotation need, and non-foot ground contact (knees, hands). Report each penalty separately so tuning stays visible.
5. ★ **Heel-to-toe gait.** Split each sole box into heel and toe geoms with touch sensors. Reward the heel-strike → toe-off order and centre-of-pressure progression during stance.
6. **Clean proprioception.** Observations are gyro, gravity vector, joint q/q̇, last action, 4 foot contacts and the gait clock; drop anything redundant. Add sensor noise and a 0–2 control-step action delay (human reflex latency is ~30–50 ms).
7. ★ **Ladder with gates (like the Go2 rungs).** R1: stand plus push recovery plus getup. R2: walk 0.5–1.5 m/s with a gait-symmetry gate (left/right step time within 5%). R3: jog 2.5–3.5 m/s with a flight phase, turns and ±10% slopes. Add each as an `eval.py --rung`.
8. **Energy efficiency.** A cost-of-transport reward term, plus CoT in the session report against human reference values. Mechanical CoT for walking should sit near the low end of the human range.
9. **Mass and inertia check.** COM height ≈ 0.55 × stature, segment inertias from Winter radii of gyration, not just masses. Replace the 0.1 kg intermediate hip/ankle links' placeholder inertias with values folded from the parent.
10. **Run hygiene.** `train.py` already writes `config.json` and `ppo_params.json`. Also stamp the git hash and MJCF sha256. Run multiple seeds as parallel containers when GPU memory allows. Keep the editor closed; TensorBoard starts with the run.
