using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mujoco;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

[Serializable]
public class Racer {
  public string name = "Racer";
  public Go2Controller controller;
  [Tooltip("Set instead of `controller` for Matt (the human racer, MattController).")]
  public MattController matt;
  /// <summary>The racing body, whichever creature it is.</summary>
  public IRacer Body => matt != null ? (IRacer)matt : controller;
  public AutoReset autoReset;
  [Tooltip("Root of this racer's MuJoCo subtree. Null for the first racer, which is always present.")]
  public GameObject mjRoot;
  public float cruiseSpeed = 1.5f;
  public Color color = Color.white;
  [NonSerialized] public float lane;           // lateral offset from the centerline, +left (m)
  [NonSerialized] public float speed;          // this race's cruise speed after the random draw
  [NonSerialized] public float distance;       // metres raced, laps included
  [NonSerialized] public int segment, lap, checkpoint, place;
  [NonSerialized] public double finishTime = -1;
  [NonSerialized] public RaceState state;
  [NonSerialized] public bool active = true;
  [NonSerialized] public float stuckFor; [NonSerialized] public int unsticks;
  [NonSerialized] public float unstickDist, backoff; [NonSerialized] public int unstickChain, backoffs;   // pinned-racer back-off (G.10)
  // Tactics (lane choice): offset is added to the racer's racing-line lane; intent is what the HUD arrow shows.
  [NonSerialized] public float lateral, offset, aggression;
  [NonSerialized] public string intent = "";
  [NonSerialized] public Racer passing;          // racer this one is trying to get past (null = none)
  [NonSerialized] public int falls, overtakes;
}

/// <summary>
/// Multi-racer race on any MapDefinition (PRD 4.2 / FR-13): N robots in ONE MuJoCo model, each driven by its own copy
/// of the validated locomotion + getup policies. Racers bump through solver contact only. Steering is the joystick
/// command: vx = cruise, yaw rate toward a look-ahead point on the racer's lane of the map centerline.
/// Lane draw and speed variation are random per race (seeded). MuJoCo frame: x, y on the ground, z up.
/// Headless: PoRace.exe -batchmode -nographics -raceTest [-timeScale 4] [-seed 7] [-racers 4] [-laps 2] [-raceFlipAt 20] [-noTactics]
/// </summary>
public unsafe class MultiRaceOrchestrator : MonoBehaviour {
  public MapDefinition map;
  public List<Racer> racers = new List<Racer>();
  public Text raceText, standingsText;
  public FollowCam followCam;
  public float countdownSeconds = 3f;
  public float steerGain = 1.5f;
  [Tooltip("Each race draws a cruise speed in [1 - speedSpread, 1] x the racer's cruiseSpeed.")]
  public float speedSpread = 0.06f;
  public bool randomizeGrid = true;

  [Tooltip("Lane choice: pass slower or fallen racers ahead, block a racer attacking from behind. Off = fixed lanes.")]
  public bool tactics = true;
  [Tooltip("Max lateral command (m/s, body frame) used to move between lines; the policy was trained on |vy| <= 0.8.")]
  public float maxLateralCmd = 0.25f;

  // Race events for the broadcast layer (director, replay, audio, predictions, telemetry), raised from the physics
  // step (FixedUpdate, main thread).
  public event Action<Racer, Racer> Overtook;   // (passer, passed)
  public event Action<Racer> Fell, Recovered, Finished;
  public event Action<RacePhase> PhaseChanged;
  public event Action Restarted;
  /// <summary>Extra text appended to each standings row (strain marks, live win chance).</summary>
  public readonly List<Func<Racer, string>> standingsExtras = new List<Func<Racer, string>>();
  public IReadOnlyList<Racer> Live => _live;
  public float TotalLength => _total;
  /// <summary>Seconds left in the countdown (0 once racing).</summary>
  public float CountdownLeft => Phase == RacePhase.Countdown ? Mathf.Max(0f, countdownSeconds - (float)(_sim - Math.Max(0, _t0))) : 0f;

  public RacePhase Phase { get; private set; } = RacePhase.Countdown;
  public double RaceTime { get; private set; }
  public int Laps { get; private set; } = 1;
  public int Seed { get; private set; }
  public int Bumps { get; private set; }
  public int BumpSteps { get; private set; }
  float _total; double _t0 = -1, _raceStart, _sim, _flipAt = -1; int _finished, _flips; bool _test, _testDone, _inBump;
  readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
  double _stepMs, _maxMs; long _steps;
  List<Racer> _live = new List<Racer>();
  Transform _anchor; Vector3 _camForward = Vector3.right;

  void Awake() {
    int dogs = racers.Count(r => r.matt == null);
    int n = Mathf.Clamp(RaceConfig.fromMenu ? RaceConfig.racers : dogs, 1, dogs);
    bool matt = RaceConfig.fromMenu && RaceConfig.matt;
    int laps = RaceConfig.fromMenu ? RaceConfig.laps : 0; int seed = RaceConfig.fromMenu ? RaceConfig.seed : -1;
    // Time.timeScale outlives scene loads: a load during an instant replay (0) or an INCIDENT slow motion (0.4) - the
    // autoplay's next race, the Menu button - would otherwise freeze or slow the new race for good.
    Time.timeScale = 1f;
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-raceTest") _test = true;
      if (args[i] == "-noTactics") tactics = false;   // fixed lanes, as before tactics existed (regression runs)
      if (args[i] == "-matt") matt = true;
      if (i + 1 >= args.Length) continue;
      if (args[i] == "-timeScale") Time.timeScale = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
      if (args[i] == "-raceFlipAt") _flipAt = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
      if (args[i] == "-seed") seed = int.Parse(args[i + 1]);
      if (args[i] == "-racers") n = Mathf.Clamp(int.Parse(args[i + 1]), 1, dogs);
      if (args[i] == "-laps") laps = int.Parse(args[i + 1]);
    }
    Laps = map.closed ? Mathf.Clamp(laps > 0 ? laps : map.defaultLaps, 1, 5) : 1;
    _total = map.Length * Laps;
    Seed = seed >= 0 ? seed : Environment.TickCount & 0xFFFF;
    // Matt's policy is trained on flat ground only (training/matt/env.py): no banks, no obstacles.
    if (matt && map.terrain != "flat") { Debug.Log($"[Race] Matt sits out {map.displayName} ({map.terrain}): flat-ground policy"); matt = false; }
    // One MuJoCo model for all racers. Matt trained at impratio 1 (at the dogs' 100 a 90 kg body's floor forces explode,
    // rl_optimization_log.md); with him on the grid the shared model compiles at 1, the dogs' gates at 1: R0 / R2 10/10,
    // R1 9/10. Dogs-only races keep 100.
    if (matt) { var gs = MjGlobalSettings.Instance; var o = gs.GlobalOptions; o.ImpRatio = MattSpec.ImpRatio; gs.GlobalOptions = o; }

    // Racers not on the grid are removed from the MuJoCo model before MjScene compiles it (in Start): the first n dogs
    // race, Matt joins when picked on the roster (-matt).
    for (int i = 0, k = 0; i < racers.Count; i++) {
      racers[i].active = racers[i].matt != null ? matt : k++ < n;
      if (!racers[i].active) { if (racers[i].mjRoot != null) racers[i].mjRoot.SetActive(false); racers[i].Body.gameObject.SetActive(false); }
    }
    _live = racers.Where(r => r.active).ToList();
    Draw();
    MjScene.Instance.preUpdateEvent += (s, a) => _sw.Restart();
    MjScene.Instance.postUpdateEvent += OnPostStep;
  }

  /// <summary>Random lane draw and speed variation for this race, reproducible from Seed.</summary>
  void Draw() {
    var rng = new System.Random(Seed);
    var lanes = map.LaneOffsets(_live.Count).ToList();
    if (randomizeGrid) lanes = lanes.OrderBy(_ => rng.Next()).ToList();
    for (int i = 0; i < _live.Count; i++) {
      var r = _live[i];
      r.lane = lanes[i];
      r.speed = r.cruiseSpeed * (1f - speedSpread * (float)rng.NextDouble());
      r.distance = 0; r.segment = 0; r.lap = 0; r.checkpoint = 0; r.finishTime = -1; r.place = 0; r.stuckFor = 0; r.unsticks = 0;
      r.unstickDist = 0; r.backoff = 0; r.unstickChain = 0; r.backoffs = 0;
      r.offset = 0; r.lateral = r.lane; r.intent = ""; r.passing = null; r.falls = 0; r.overtakes = 0; r.state = RaceState.RACING;
      Spawn(r, 0f);
    }
    // Drawn after the grid so the lane / speed draw of a seed stays exactly as before.
    foreach (var r in _live) r.aggression = (float)rng.NextDouble();
    _ahead.Clear();
  }

  void Spawn(Racer r, float s) {
    var p = map.LanePoint(s, r.lane, out var t);
    // On a bank the uphill feet of a level-spawned dog would start inside the surface: lift by the slope over the stance.
    float lateral = Vector2.Dot(p - map.PointAt(s, out _), MapDefinition.Left(t)), bank = Mathf.Abs(map.BankAt(s)) * Mathf.Deg2Rad;
    // Obstacles (steeplechase): spawn 10 cm above the tallest block under the stance and drop onto it, as in training.
    float block = map.ObstacleHeightAt(p, 0.45f); if (block > 0f) block += 0.1f;
    r.Body.SpawnMj = new Vector3(p.x, p.y, r.Body.HomeHeight + map.SurfaceHeight(s, lateral) + 0.2f * Mathf.Tan(bank) + block);
    r.Body.SpawnYaw = Mathf.Atan2(t.y, t.x);
  }

  public void Restart() {
    Phase = RacePhase.Countdown; _t0 = -1; RaceTime = 0; _finished = 0; Bumps = 0; BumpSteps = 0; _firstFinish = -1; _overFor = 0;
    Seed = (Seed * 1103515245 + 12345) & 0xFFFF;   // a new draw each restart
    Draw();
    foreach (var r in _live) { r.Body.StartLocomotion(); r.Body.ResetToHome(); }
    Restarted?.Invoke(); PhaseChanged?.Invoke(Phase);
  }

  /// <summary>Advance the racer's segment so its projection stays inside it; returns metres raced including laps.</summary>
  float Progress(Racer r, Vector2 pos, out float lateral) {
    int n = map.SegmentCount; float t = 0, len = 1; Vector2 dir = Vector2.right, a = Vector2.zero;
    for (int guard = 0; guard < n + 1; guard++) {
      a = map.P(r.segment); dir = (map.P(r.segment + 1) - a); len = dir.magnitude; dir /= len;
      t = Vector2.Dot(pos - a, dir);
      if (t <= len) break;
      if (r.segment + 1 < n) r.segment++;
      else if (map.closed) { r.segment = 0; r.lap++; }
      else break;   // past the end of an open track
    }
    lateral = Vector2.Dot(pos - a, MapDefinition.Left(dir));
    float along = map.closed ? Mathf.Clamp(t, 0f, len) : Mathf.Max(0f, t);
    return r.lap * map.Length + map.SegmentStart(r.segment) + along;
  }

  void OnPostStep(object sender, MjStepArgs a) {
    double ms = _sw.Elapsed.TotalMilliseconds;
    var d = a.data; double t = d->time; _sim = t;
    if (_live.Count == 0 || _live.Any(r => !r.Body.Ready)) return;
    if (t > 1.0) { _stepMs += ms; _steps++; if (ms > _maxMs) _maxMs = ms; }
    if (_t0 < 0) _t0 = t;

    if (Phase == RacePhase.Countdown && t - _t0 >= countdownSeconds) { Phase = RacePhase.Racing; _raceStart = t; PhaseChanged?.Invoke(Phase); }
    if (Phase == RacePhase.Racing) RaceTime = t - _raceStart;
    if (Phase == RacePhase.Racing && _flipAt >= 0 && RaceTime >= _flipAt) { _flipAt = -1; Leader().Body.KnockOver(); _flips++; }

    foreach (var r in _live) {
      var c = r.Body;
      var pos = new Vector2((float)d->qpos[c.BaseQpos], (float)d->qpos[c.BaseQpos + 1]);
      float yaw = (float)RacerMath.Yaw(d->qpos, c.BaseQpos);
      var was = r.state;
      r.state = r.autoReset != null ? r.autoReset.State : RaceState.RACING;
      if (Phase == RacePhase.Racing && r.finishTime < 0) {
        if (r.state == RaceState.FALLEN_RECOVERING && was != RaceState.FALLEN_RECOVERING) { r.falls++; Fell?.Invoke(r); }
        if (r.state == RaceState.RACING && was == RaceState.FALLEN_RECOVERING) Recovered?.Invoke(r);
      }
      if (Phase == RacePhase.Countdown && r.matt != null) {
        // Matt's policy marches in place on a zero command and drifts 15-60 deg left in 3 s (training/matt/probe_stand.py):
        // hold his heading along the track through the countdown instead, so he starts pointing the right way.
        map.PointAt(0f, out var t0);
        float e0 = Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, Mathf.Atan2(t0.y, t0.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        c.Command = new Vector3(0f, 0f, Mathf.Clamp(steerGain * e0, -0.8f, 0.8f)); continue;
      }
      if (Phase == RacePhase.Countdown || r.finishTime >= 0) { c.Command = Vector3.zero; continue; }

      float dist = Progress(r, pos, out float lateral);
      r.distance = Mathf.Min(dist, _total); r.lateral = lateral;
      if (dist >= _total) { r.finishTime = RaceTime; r.place = ++_finished; c.Command = Vector3.zero; Finished?.Invoke(r); continue; }
      int cp = Mathf.FloorToInt(dist / map.checkpointSpacing);
      if (cp > r.checkpoint) { r.checkpoint = cp; Spawn(r, cp * map.checkpointSpacing); }

      // Lane choice: the racing line (lane x the map's squeeze) plus this racer's tactical offset, slewed at 0.6 m/s.
      // Banked sections are a no-manoeuvre zone: the slope policy's weak spot is restarting uphill from a standstill
      // (rl_optimization_log.md, F.8), and sideways moves on the bank caused exactly that (Banked Oval seed 2 DNF).
      float line = LineAt(r, dist); bool banked = Mathf.Abs(map.BankAt(dist)) > 0.5f || Mathf.Abs(map.BankAt(dist + map.lookAhead)) > 0.5f;
      if (tactics) r.offset = Mathf.MoveTowards(r.offset, banked ? 0f : Tactic(r, line) - line, 0.6f * Go2Controller.SimDt);
      if (banked) { r.intent = ""; r.passing = null; }
      var target = map.LanePoint(dist + map.lookAhead, r.lane, out var tanAhead) + MapDefinition.Left(tanAhead) * r.offset;
      float err = Mathf.DeltaAngle(yaw * Mathf.Rad2Deg, Mathf.Atan2(target.y - pos.y, target.x - pos.x) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
      // Small sideways step toward the chosen line (body frame ~ track frame while the heading error is small).
      float latErr = line + r.offset - lateral;
      float latCmd = tactics && !banked && Mathf.Abs(latErr) > 0.1f && Mathf.Abs(err) < 0.5f ? Mathf.Clamp(latErr, -maxLateralCmd, maxLateralCmd) : 0f;
      // Yaw-rate envelope: the dogs trained to 1.2 rad/s, Matt to 1.0 and was graduated on 0.8 (training/matt/sim.py).
      float yawMax = r.matt != null ? 0.8f : 1.2f;
      c.Command = new Vector3(r.speed * Mathf.Max(0.3f, Mathf.Cos(err)), latCmd, Mathf.Clamp(steerGain * err, -yawMax, yawMax));
      // Un-stick: upright, asked to go fast, but (nearly) stopped for half a second -> restart the command ramp.
      float vx = (float)d->qvel[c.BaseDof], vy = (float)d->qvel[c.BaseDof + 1];
      // Dogs only: the Go2 policy braces when asked for >= 1.3 m/s from rest; Matt (slow to accelerate) would be reset forever.
      bool stuck = r.matt == null && c.Locomoting && c.FilteredCommand.x > 1.0f && vx * vx + vy * vy < 0.3f * 0.3f;
      r.stuckFor = stuck ? r.stuckFor + Go2Controller.SimDt : 0f;
      if (r.stuckFor > 0.5f) {
        c.RestartCommandRamp(); r.stuckFor = 0f; r.unsticks++;
        // Pinned (six restarts in a row, under 0.3 m gained): restarting from a standstill against an 8 cm hurdle never
        // clears it (Steeplechase seed 3, Blue at 90 m: 796 restarts, DNF). Back off ~0.6 m, then come again with a run-up.
        r.unstickChain = dist - r.unstickDist < 0.3f ? r.unstickChain + 1 : 1; r.unstickDist = dist;
        if (r.unstickChain >= 6) { r.backoff = 1.2f; r.unstickChain = 0; r.backoffs++; }
      }
      if (r.backoff > 0f) { r.backoff -= Go2Controller.SimDt; c.Command = new Vector3(-0.5f, 0f, Mathf.Clamp(steerGain * err, -1.2f, 1.2f)); }
      if (Mathf.Abs(lateral) > map.halfWidth + 1f) c.ResetToHome();   // left the track: back to the last checkpoint
    }
    if (Phase == RacePhase.Racing) DetectOvertakes();
    if (Phase == RacePhase.Racing && _finished == _live.Count) { Phase = RacePhase.Finished; PhaseChanged?.Invoke(Phase); }

    // Robot-vs-robot contacts this step (bump mechanics are pure solver contact; this only counts them).
    if (Phase == RacePhase.Racing) {
      var m = a.model; bool any = false;
      for (int i = 0; i < d->ncon; i++) {
        int b1 = m->body_rootid[m->geom_bodyid[d->contact[i].geom1]], b2 = m->body_rootid[m->geom_bodyid[d->contact[i].geom2]];
        if (b1 != b2 && IsRobotRoot(b1) && IsRobotRoot(b2)) { any = true; break; }
      }
      if (any) { BumpSteps++; if (!_inBump) { Bumps++; _inBump = true; } } else _inBump = false;
    }

    if (_test && !_testDone && (Phase == RacePhase.Finished || RaceTime > 600)) {
      _testDone = true;
      bool ok = Phase == RacePhase.Finished;
      Debug.Log($"[Race4] {(ok ? "FINISHED" : "DNF")} map={map.displayName} laps={Laps} length={_total:F1}m seed={Seed} | " + string.Join(" | ", Standings().Select(r =>
          $"{r.name} {(r.finishTime >= 0 ? r.finishTime.ToString("F2", CultureInfo.InvariantCulture) + "s" : "DNF@" + r.distance.ToString("F0") + "m")} lane={r.lane:F1} v={r.speed:F2} resets={(r.autoReset != null ? r.autoReset.Resets : 0)} unstuck={r.unsticks}{(r.backoffs > 0 ? " backoffs=" + r.backoffs : "")} falls={r.falls} passes={r.overtakes}"))
          + $" | flips={_flips} bumps={Bumps} bumpTime={BumpSteps * Go2Controller.SimDt:F2}s");
      double mean = _steps > 0 ? _stepMs / _steps : 0;
      Debug.Log($"[Perf4] {_live.Count} racers: physics step mean {mean:F4} ms max {_maxMs:F3} ms | per 50 Hz control step {mean * Go2Controller.Decimation:F3} ms (budget 5.0)");
      Application.Quit(ok ? 0 : 1);
    }
  }

  /// <summary>Lateral position of the racer's racing line at arc length s (lane x the lap's squeeze).</summary>
  float LineAt(Racer r, float s) {
    float u = map.closed ? Mathf.Repeat(s, map.Length) / map.Length : Mathf.Clamp01(s / map.Length);
    return r.lane * map.laneScale.Evaluate(u);
  }

  const float PassGap = 0.65f, Margin = 0.45f;

  /// <summary>Where this racer wants to be across the track (lateral, + left). Pass a slower or fallen racer that is
  /// up to 2.5 m (5 m if it is down) ahead and in the way, on the side with more room; an aggressive racer with an
  /// attacker within 1.5 m behind moves across to cover that line. Otherwise hold the racing line.</summary>
  float Tactic(Racer r, float line) {
    float edge = map.halfWidth - Margin, me = line + r.offset;
    if (r.state != RaceState.RACING) { r.intent = ""; r.passing = null; return me; }   // no manoeuvres while stumbling
    Racer block = null; float bestGap = float.MaxValue;
    foreach (var o in _live) {
      if (o == r || o.finishTime >= 0) continue;
      float ds = o.distance - r.distance; bool down = o.state != RaceState.RACING;
      if (ds <= 0f || ds > (down ? 5f : 2.5f) || Mathf.Abs(o.lateral - me) > (down ? 0.8f : 0.55f)) continue;
      if ((down || o.speed < r.speed - 0.01f) && ds < bestGap) { bestGap = ds; block = o; }
    }
    if (block != null) {
      float gap = block.state != RaceState.RACING ? 0.85f : PassGap;
      float left = block.lateral + gap, right = block.lateral - gap;
      float roomL = left <= edge ? Room(r, left) : -1f, roomR = right >= -edge ? Room(r, right) : -1f;
      if (roomL >= 0f || roomR >= 0f) {
        bool goLeft = roomL > roomR || (Mathf.Approximately(roomL, roomR) && me >= block.lateral);
        r.passing = block; r.intent = block.state != RaceState.RACING ? "AVOID" : goLeft ? "PASS L" : "PASS R";
        return goLeft ? left : right;
      }
    }
    r.passing = null;
    if (r.aggression > 0.5f) {
      foreach (var p in _live) {
        if (p == r || p.passing != r) continue;
        float behind = r.distance - p.distance;
        if (behind > 0f && behind < 1.5f) { r.intent = "BLOCK"; return Mathf.Clamp(Mathf.Lerp(line, p.lateral, 0.4f), -edge, edge); }
      }
    }
    r.intent = "";
    return line;
  }

  /// <summary>Clearance (m) at a candidate lateral position from the nearest other racer within 3 m along the track.</summary>
  float Room(Racer r, float lat) {
    float best = 5f;
    foreach (var o in _live) if (o != r && o.finishTime < 0 && Mathf.Abs(o.distance - r.distance) < 3f) best = Mathf.Min(best, Mathf.Abs(o.lateral - lat));
    return best;
  }

  // Pairwise "who is ahead" with 0.25 m hysteresis, so two racers side by side do not fire overtakes every step.
  readonly Dictionary<(Racer, Racer), int> _ahead = new Dictionary<(Racer, Racer), int>();
  void DetectOvertakes() {
    for (int i = 0; i < _live.Count; i++) for (int j = i + 1; j < _live.Count; j++) {
      Racer a = _live[i], b = _live[j];
      if (a.finishTime >= 0 || b.finishTime >= 0) continue;
      float d = a.distance - b.distance; int now = d > 0.25f ? 1 : d < -0.25f ? -1 : 0;
      if (now == 0) continue;
      if (_ahead.TryGetValue((a, b), out int before) && before != now) {
        var passer = now > 0 ? a : b; var passed = now > 0 ? b : a; passer.overtakes++;
        Overtook?.Invoke(passer, passed);
      }
      _ahead[(a, b)] = now;
    }
  }

  bool IsRobotRoot(int body) { foreach (var r in _live) if (r.Body.BaseBody == body) return true; return false; }
  List<Racer> Standings() => _live.OrderByDescending(r => r.finishTime >= 0 ? 1e6f - (float)r.finishTime : r.distance).ToList();
  public Racer Leader() { var s = Standings(); return s.FirstOrDefault(r => r.finishTime < 0) ?? s[0]; }

  double _firstFinish = -1; float _overFor;

  /// <summary>Self-running app (RaceConfig.autoplay): a race ends when everyone is home, 45 s after the winner, or at
  /// 5 minutes (Matt cannot finish yet); 6 s later the next race loads.</summary>
  void AutoplayNext() {
    if (!RaceConfig.autoplay || _test) return;
    // 30 fps cap: re-applied here because the Mobile quality level (GfxTier) switches vsync back on when it loads
    if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != 30) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30; }
    if (_firstFinish < 0 && _finished > 0) _firstFinish = RaceTime;
    bool over = Phase == RacePhase.Finished || (Phase == RacePhase.Racing && ((_firstFinish >= 0 && RaceTime - _firstFinish > 45) || RaceTime > 300));
    if (!over) return;
    _overFor += Time.unscaledDeltaTime;
    if (_overFor > 6f) { _overFor = float.NegativeInfinity; RaceConfig.LaunchNextAutoplay(); }
  }

  void Update() {
    if (_live.Count == 0) return;
    AutoplayNext();
    var order = Standings();
    if (followCam != null) {
      // Frame the pack: an anchor at the mean position of the racers still running, looking along the track there.
      if (_anchor == null) {
        _anchor = new GameObject("PackAnchor").transform; followCam.target = _anchor;
        followCam.groundHeight = u => map.SurfaceHeightAt(new Vector2(u.x, u.z));   // Unity (x, z) = MuJoCo (x, y)
      }
      var run = order.Where(r => r.finishTime < 0 && r.Body.BaseTransform != null).ToList();
      if (run.Count == 0) run = order.Where(r => r.Body.BaseTransform != null).ToList();
      if (run.Count > 0) {
        var sum = Vector3.zero; float dsum = 0; foreach (var r in run) { sum += r.Body.BaseTransform.position; dsum += r.distance; }
        _anchor.position = sum / run.Count;
        map.PointAt(dsum / run.Count, out var tan);
        _camForward = Vector3.Slerp(_camForward, new Vector3(tan.x, 0f, tan.y), 1f - Mathf.Exp(-2.5f * Time.deltaTime)).normalized;
        followCam.forward = _camForward;
      }
    }
    if (raceText != null) {
      string head = Phase == RacePhase.Countdown ? Mathf.CeilToInt(Mathf.Max(0f, countdownSeconds - (float)(_sim - Math.Max(0, _t0)))).ToString()
                  : Phase == RacePhase.Racing ? "GO" : "FINISH";
      raceText.text = $"{head}\n{RaceTime:F2} s";
    }
    if (standingsText != null) {
      var sb = new System.Text.StringBuilder();
      for (int i = 0; i < order.Count; i++) {
        var r = order[i];
        string st = r.finishTime >= 0 ? r.finishTime.ToString("F2", CultureInfo.InvariantCulture) + " s"
                  : (map.closed ? $"L{Mathf.Min(r.lap + 1, Laps)}/{Laps}  " : "") + $"{r.distance:F0} m" + (r.state == RaceState.RACING ? "" : r.state == RaceState.STUMBLE ? "  stumble" : "  DOWN");
        sb.Append($"{i + 1}. <color=#{ColorUtility.ToHtmlStringRGB(r.color)}>{r.name}</color>  {st}");
        foreach (var extra in standingsExtras) sb.Append(extra(r));
        sb.Append('\n');
      }
      sb.Append($"<size=18>{map.displayName}   bumps {Bumps}</size>");
      standingsText.text = sb.ToString();
    }
  }
}

}
