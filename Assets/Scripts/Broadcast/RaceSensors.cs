using System;
using System.Collections.Generic;
using System.Linq;
using Mujoco;
using UnityEngine;

namespace PoRace {

/// <summary>Per-racer measurements shared by the broadcast layer, computed once per 50 Hz control step from mjData:
/// joint effort (|actuator_force| / forcerange, smoothed), mechanical power sum |tau * qdot|, base speed, foot strikes
/// (rising edge of a foot geom touching a world geom: floor, bank, rails, obstacles) and cube impacts. Read only:
/// nothing here writes MuJoCo state.</summary>
public unsafe class RaceSensors : MonoBehaviour {
  public MultiRaceOrchestrator race;
  [Tooltip("Smoothing time constant for joint effort (s).")]
  public float effortTau = 0.15f;

  public class State {
    public readonly float[] effort;                            // per actuator, 0..1 of its forcerange, smoothed
    public float power, speed, energy, mass;                   // W, m/s, J, kg
    public Vector3 velocity;                                   // MuJoCo frame
    public readonly bool[] down;
    public int strikes;
    internal readonly int[] footGeom, act, dof;
    internal readonly float[] forceMax;
    internal readonly bool[] now;
    internal int baseDof;
    public State(int actuators, int feet) {
      effort = new float[actuators]; act = new int[actuators]; dof = new int[actuators]; forceMax = new float[actuators];
      down = new bool[feet]; now = new bool[feet]; footGeom = new int[feet];
    }
  }

  /// <summary>(racer, foot index (Go2: 0..3 FL FR RL RR; Matt: 0..1 L R), Unity position, base speed m/s).</summary>
  public event Action<Racer, int, Vector3, float> FootStrike;
  /// <summary>(cube index, Unity position, speed lost m/s, racer hit or null).</summary>
  public event Action<int, Vector3, float, Racer> CubeImpact;

  readonly Dictionary<Racer, State> _s = new Dictionary<Racer, State>();
  readonly Vector3[] _cubeVel = new Vector3[Go2Model.NumCubes];
  int _step;
  public const float ForceRange = 24f;

  public State Of(Racer r) => r != null && _s.TryGetValue(r, out var s) ? s : null;

  void Awake() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    MjScene.Instance.postUpdateEvent += OnPostStep;
  }
  void OnDestroy() { if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= OnPostStep; }

  void Init(MujocoLib.mjModel_* m) {
    int Id(MujocoLib.mjtObj t, string n) => MujocoLib.mj_name2id(m, (int)t, n);
    foreach (var r in race.Live) {
      var B = r.Body; if (B == null || !B.Ready) { _s.Clear(); return; }
      State s;
      if (r.matt == null) {
        var M = r.controller.Model;
        s = new State(Go2Model.Nu, 4);
        for (int i = 0; i < Go2Model.Nu; i++) { s.act[i] = M.ActId[i]; s.dof[i] = M.JointDof[i]; s.forceMax[i] = ForceRange; }
        for (int f = 0; f < 4; f++) s.footGeom[f] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, M.Prefix + Go2Model.Legs[f]);
      } else {
        s = new State(MattSpec.Nu, 2);
        for (int i = 0; i < MattSpec.Nu; i++) {
          s.act[i] = Id(MujocoLib.mjtObj.mjOBJ_ACTUATOR, MattSpec.Joints[i]);
          s.dof[i] = m->jnt_dofadr[Id(MujocoLib.mjtObj.mjOBJ_JOINT, MattSpec.Joints[i])];
          s.forceMax[i] = MattSpec.ForceMax[i];
        }
        s.footGeom[0] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "left_foot"); s.footGeom[1] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "right_foot");
      }
      s.baseDof = B.BaseDof;
      for (int b = 0; b < (int)m->nbody; b++) if (m->body_rootid[b] == B.BaseBody) s.mass += (float)m->body_mass[b];
      _s[r] = s;
    }
  }

  void OnPostStep(object sender, MjStepArgs a) {
    if (race == null || race.Live.Count == 0) return;
    if (_s.Count == 0) Init(a.model);
    if (_s.Count == 0 || _step++ % Go2Controller.Decimation != 0) return;
    var m = a.model; var d = a.data; float dt = Go2Controller.SimDt * Go2Controller.Decimation;
    float k = 1f - Mathf.Exp(-dt / effortTau);

    foreach (var kv in _s) {
      var s = kv.Value;
      float p = 0f;
      for (int i = 0; i < s.effort.Length; i++) {
        float tau = (float)d->actuator_force[s.act[i]];
        s.effort[i] += (Mathf.Min(1f, Mathf.Abs(tau) / s.forceMax[i]) - s.effort[i]) * k;
        p += Mathf.Abs(tau * (float)d->qvel[s.dof[i]]);
      }
      s.power = p; s.energy += p * dt;
      s.velocity = new Vector3((float)d->qvel[s.baseDof], (float)d->qvel[s.baseDof + 1], (float)d->qvel[s.baseDof + 2]);
      s.speed = new Vector2(s.velocity.x, s.velocity.y).magnitude;
    }

    // Foot contacts with world geoms (body 0), one pass over the contact list.
    foreach (var s in _s.Values) for (int f = 0; f < s.now.Length; f++) s.now[f] = false;
    for (int i = 0; i < d->ncon; i++) {
      int g1 = d->contact[i].geom1, g2 = d->contact[i].geom2;
      bool w1 = m->geom_bodyid[g1] == 0, w2 = m->geom_bodyid[g2] == 0;
      if (!(w1 ^ w2)) continue;
      int foot = w1 ? g2 : g1;
      foreach (var s in _s.Values) for (int f = 0; f < s.footGeom.Length; f++) if (s.footGeom[f] == foot) s.now[f] = true;
    }
    foreach (var kv in _s) {
      var s = kv.Value;
      for (int f = 0; f < s.now.Length; f++) {
        bool now = s.now[f];
        if (now && !s.down[f]) {
          s.strikes++;
          int g = s.footGeom[f];
          FootStrike?.Invoke(kv.Key, f, new Vector3((float)d->geom_xpos[3 * g], (float)d->geom_xpos[3 * g + 2], (float)d->geom_xpos[3 * g + 1]), s.speed);
        }
        s.down[f] = now;
      }
    }

    // Cube impacts: a cube that lost more than 1.5 m/s since the last control step hit something.
    var dog = race.Live.FirstOrDefault(x => x.matt == null); if (dog == null) return;   // the cube pool lives in the dogs' model part
    var any = dog.controller.Model;
    for (int c = 0; c < Go2Model.NumCubes; c++) {
      int v = any.CubeDof[c], q = any.CubeQpos[c];
      var vel = new Vector3((float)d->qvel[v], (float)d->qvel[v + 1], (float)d->qvel[v + 2]);
      float lost = (_cubeVel[c] - vel).magnitude; _cubeVel[c] = vel;
      if (lost < 1.5f || d->qpos[q] > 15.0) continue;   // parked cubes live at x >= 20
      var mj = new Vector3((float)d->qpos[q], (float)d->qpos[q + 1], (float)d->qpos[q + 2]);
      Racer hit = null; float best = 0.6f;
      foreach (var r in race.Live) {
        var B = r.Body; float dx = (float)d->qpos[B.BaseQpos] - mj.x, dy = (float)d->qpos[B.BaseQpos + 1] - mj.y;
        float dist = Mathf.Sqrt(dx * dx + dy * dy); if (dist < best) { best = dist; hit = r; }
      }
      CubeImpact?.Invoke(c, new Vector3(mj.x, mj.z, mj.y), lost, hit);
    }
  }

}

}
