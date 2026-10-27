using System;
using Mujoco;
using Unity.InferenceEngine;
using UnityEngine;

namespace PoRace {

/// <summary>
/// Matt (photoscanned human, tools/blender/matt_mjcf.py) inside the race's single MjScene: 250 Hz physics, policy at
/// 50 Hz (every 5th step) from MjScene.ctrlCallback. Observation and action are mujoco_playground's G1 joystick env
/// (training/matt/env.py: Matt has the G1 joint layout) exactly as G1Controller builds them: local linear velocity,
/// gyro, projected gravity (pelvis IMU), command, joint pos/vel, last action and a two-foot gait phase clock.
/// Unlike G1Controller it never touches the solver options (the race scene owns them, and they are Matt's training
/// contract), and it resets only its own state, at a spawn point, so it can share the model with the dogs.
/// There is no getup policy: a fall is handled by AutoReset (back to the last checkpoint).
/// </summary>
public unsafe class MattController : MonoBehaviour, IParitySource, IRacer {
  public enum Mode { HoldPose, Locomotion }
  public Mode mode = Mode.HoldPose;
  public ModelAsset locomotionModel;
  [Tooltip("vx (m/s), vy (m/s), yaw rate (rad/s)")] public Vector3 command = Vector3.zero;
  [Tooltip("Where ResetToHome puts Matt, MuJoCo frame (x, y, z up) and heading about z. A race moves this to the last checkpoint.")]
  public Vector3 spawnMj = new Vector3(0f, 0f, MattSpec.HomeHeight);
  public float spawnYaw = 0f;
  [Tooltip("True when Matt is the only thing in the model (parity harness): resets then also zero the sim time.")]
  public bool alone = false;

  public bool Ready => _ready;
  public int Decimation => MattSpec.Decimation;
  public int Nq => 7 + MattSpec.Nu; public int Nv => 6 + MattSpec.Nu; public int Nu => MattSpec.Nu;
  public float[] CurrentObs => _obs;
  public float[] LastAction => _lastAction;
  public bool Upright { get; private set; } = true;
  public float LastInferenceMs { get; private set; }
  public int BaseBody { get; private set; }
  public int BaseQpos { get; private set; }
  public int BaseDof { get; private set; }
  public int UpAdr => _up;

  // IRacer
  Vector3 IRacer.Command { get => command; set => command = value; }
  public Vector3 FilteredCommand => _cmd;
  Vector3 IRacer.SpawnMj { get => spawnMj; set => spawnMj = value; }
  float IRacer.SpawnYaw { get => spawnYaw; set => spawnYaw = value; }
  public float HomeHeight => MattSpec.HomeHeight;
  public bool Locomoting => mode == Mode.Locomotion && _policy != null;
  public string ModeName => mode.ToString();
  public float SimDt => MattSpec.SimDt;
  public void StartLocomotion() { mode = Mode.Locomotion; command = Vector3.zero; }

  const int N = MattSpec.Nu;
  readonly int[] _jq = new int[N], _jd = new int[N], _act = new int[N];
  readonly MjActuator[] _actComp = new MjActuator[N];
  readonly float[] _obs = new float[MattSpec.ObsDim], _lastAction = new float[N];
  readonly double[] _sens = new double[11];   // linvel 3, gyro 3, quat 4, torso up z
  int _linvel, _gyro, _quat, _up, _step, _ctrlStep; bool _ready;
  PolicyRunner _policy; Vector3 _cmd;

  void Awake() {
    Time.fixedDeltaTime = MattSpec.SimDt;
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-command" && i + 3 < args.Length && alone)
        command = new Vector3(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture),
                              float.Parse(args[i + 2], System.Globalization.CultureInfo.InvariantCulture),
                              float.Parse(args[i + 3], System.Globalization.CultureInfo.InvariantCulture));
      if (args[i] == "-mattMode" && i + 1 < args.Length && Enum.TryParse(args[i + 1], out Mode m)) mode = m;
    }
    MjScene.Instance.postInitEvent += OnInit;
    MjScene.Instance.preUpdateEvent += OnPre;
    MjScene.Instance.ctrlCallback += OnCtrl;
  }

  void OnDestroy() {
    if (!MjScene.InstanceExists) return;
    MjScene.Instance.postInitEvent -= OnInit;
    MjScene.Instance.preUpdateEvent -= OnPre;
    MjScene.Instance.ctrlCallback -= OnCtrl;
  }

  void Start() { if (locomotionModel != null) _policy = new PolicyRunner(locomotionModel, MattSpec.ObsDim, N); }
  void OnApplicationQuit() { _policy?.Dispose(); }

  static int Id(MujocoLib.mjModel_* m, MujocoLib.mjtObj t, string name) {
    int id = MujocoLib.mj_name2id(m, (int)t, name);
    if (id < 0) throw new InvalidOperationException($"Matt: {t} '{name}' missing from model");
    return id;
  }

  void OnInit(object sender, MjStepArgs a) {
    var m = a.model;
    // The plugin does not parse these two (Go2Controller sets the same values in a race scene; the testbed has no dog).
    m->opt.ls_iterations = 5;
    m->opt.disableflags |= (int)MujocoLib.mjtDisableBit.mjDSBL_EULERDAMP;
    BaseBody = Id(m, MujocoLib.mjtObj.mjOBJ_BODY, "pelvis");
    for (int j = 0; j < (int)m->njnt; j++) if (m->jnt_bodyid[j] == BaseBody && m->jnt_type[j] == (int)MujocoLib.mjtJoint.mjJNT_FREE) { BaseQpos = m->jnt_qposadr[j]; BaseDof = m->jnt_dofadr[j]; }
    for (int i = 0; i < N; i++) {
      int j = Id(m, MujocoLib.mjtObj.mjOBJ_JOINT, MattSpec.Joints[i]);
      _jq[i] = m->jnt_qposadr[j]; _jd[i] = m->jnt_dofadr[j];
      _act[i] = Id(m, MujocoLib.mjtObj.mjOBJ_ACTUATOR, MattSpec.Joints[i]);   // actuators are named after their joints
    }
    foreach (var c in FindObjectsByType<MjActuator>(FindObjectsSortMode.None)) for (int i = 0; i < N; i++) if (c.MujocoId == _act[i]) _actComp[i] = c;
    _linvel = m->sensor_adr[Id(m, MujocoLib.mjtObj.mjOBJ_SENSOR, "local_linvel_pelvis")];
    _gyro = m->sensor_adr[Id(m, MujocoLib.mjtObj.mjOBJ_SENSOR, "gyro_pelvis")];
    _quat = m->sensor_adr[Id(m, MujocoLib.mjtObj.mjOBJ_SENSOR, "orientation_pelvis")];
    _up = m->sensor_adr[Id(m, MujocoLib.mjtObj.mjOBJ_SENSOR, "upvector_torso")];
    Check(m);
    _ready = true;
    ResetToHome(m, a.data);
  }

  /// <summary>The model Unity regenerated must equal the trainer's (docs/matt/model_summary.json) and run under the
  /// physics contract Matt was trained with (the race's).</summary>
  void Check(MujocoLib.mjModel_* m) {
    void Must(bool ok, string msg) { if (!ok) throw new InvalidOperationException("[MattCheck] " + msg); }
    Must(Math.Abs(m->opt.timestep - MattSpec.SimDt) < 1e-9, $"timestep {m->opt.timestep}");
    Must(m->opt.integrator == (int)MujocoLib.mjtIntegrator.mjINT_EULER && m->opt.cone == (int)MujocoLib.mjtCone.mjCONE_PYRAMIDAL, "integrator/cone");
    Must(m->opt.iterations == 1 && m->opt.ls_iterations == 5, $"iterations {m->opt.iterations}/{m->opt.ls_iterations}");
    Must(Math.Abs(m->opt.impratio - MattSpec.ImpRatio) < 1e-6, $"impratio {m->opt.impratio} (a race with Matt compiles at {MattSpec.ImpRatio})");
    double mass = 0; for (int b = 0; b < (int)m->nbody; b++) if (m->body_rootid[b] == BaseBody) mass += m->body_mass[b];
    Must(Math.Abs(mass - MattSpec.Mass) < 1e-3, $"mass {mass}");
    for (int i = 0; i < N; i++) {
      int a = _act[i];
      Must(Math.Abs(m->actuator_gainprm[a * 10] - MattSpec.Kp[i]) < 1e-6 && Math.Abs(m->actuator_biasprm[a * 10 + 1] + MattSpec.Kp[i]) < 1e-6, $"kp[{i}] {m->actuator_gainprm[a * 10]}");
      Must(Math.Abs(m->actuator_forcerange[a * 2 + 1] - MattSpec.ForceMax[i]) < 1e-6 && m->actuator_forcelimited[a] != 0, $"forcerange[{i}] {m->actuator_forcerange[a * 2 + 1]}");
      Must(_actComp[i] != null, $"MjActuator component for {MattSpec.Joints[i]}");
    }
    Debug.Log($"[MattCheck] OK mass={mass:F4} dt={m->opt.timestep} baseQpos={BaseQpos}");
  }

  public void ResetToHome() {
    var s = MjScene.Instance;
    ResetToHome(s.Model, s.Data);
    s.SyncUnityToMjState();
  }

  void ResetToHome(MujocoLib.mjModel_* m, MujocoLib.mjData_* d) {
    if (alone) MujocoLib.mj_resetData(m, d);
    else { for (int i = 0; i < 6; i++) d->qvel[BaseDof + i] = 0; for (int i = 0; i < N; i++) d->qvel[_jd[i]] = 0; }
    d->qpos[BaseQpos] = spawnMj.x; d->qpos[BaseQpos + 1] = spawnMj.y; d->qpos[BaseQpos + 2] = spawnMj.z;
    d->qpos[BaseQpos + 3] = Math.Cos(spawnYaw * 0.5); d->qpos[BaseQpos + 4] = 0; d->qpos[BaseQpos + 5] = 0; d->qpos[BaseQpos + 6] = Math.Sin(spawnYaw * 0.5);
    for (int i = 0; i < N; i++) { d->qpos[_jq[i]] = MattSpec.DefaultPose[i]; SetCtrl(d, i, MattSpec.DefaultPose[i]); }
    Array.Clear(_lastAction, 0, N); _cmd = Vector3.zero; _step = 0; _ctrlStep = 0;
    MujocoLib.mj_forward(m, d);
  }

  void SetCtrl(MujocoLib.mjData_* d, int i, float v) { d->ctrl[_act[i]] = v; _actComp[i].Control = v; }

  void OnPre(object sender, MjStepArgs a) {
    if (!_ready) return; var d = a.data;
    for (int i = 0; i < 3; i++) { _sens[i] = d->sensordata[_linvel + i]; _sens[3 + i] = d->sensordata[_gyro + i]; }
    for (int i = 0; i < 4; i++) _sens[6 + i] = d->sensordata[_quat + i];
    _sens[10] = d->sensordata[_up + 2];
  }

  void OnCtrl(object sender, MjStepArgs a) {
    if (!_ready || _step++ % MattSpec.Decimation != 0) return;
    var d = a.data;
    Upright = _sens[10] > 0.0;
    _cmd = new Vector3(Mathf.MoveTowards(_cmd.x, command.x, Go2Controller.CmdSlewStep), Mathf.MoveTowards(_cmd.y, command.y, Go2Controller.CmdSlewStep),
                       Mathf.MoveTowards(_cmd.z, command.z, Go2Controller.CmdSlewStep));
    BuildObs(d);
    var t0 = Time.realtimeSinceStartupAsDouble;
    if (mode == Mode.Locomotion && _policy != null) {
      var act = _policy.Run(_obs, MattSpec.ObsDim);
      for (int i = 0; i < N; i++) { _lastAction[i] = act[i]; SetCtrl(d, i, MattSpec.DefaultPose[i] + MattSpec.ActionScale * act[i]); }   // trainer does not clip actions
    } else {
      for (int i = 0; i < N; i++) { _lastAction[i] = 0f; SetCtrl(d, i, MattSpec.DefaultPose[i]); }
    }
    LastInferenceMs = (float)((Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
    _ctrlStep++;
  }

  /// <summary>linvel 3, gyro 3, gravity 3, command 3, q - default 29, qvel 29, last action 29, cos(phase) 2, sin(phase) 2.
  /// The trainer builds the observation before advancing the phase, so the reset obs and the first step obs share phase 0.</summary>
  void BuildObs(MujocoLib.mjData_* d) {
    int k = 0;
    for (int i = 0; i < 3; i++) _obs[k++] = (float)_sens[i];
    for (int i = 0; i < 3; i++) _obs[k++] = (float)_sens[3 + i];
    double w = _sens[6], x = _sens[7], y = _sens[8], z = _sens[9];
    _obs[k++] = (float)(-2 * (x * z - w * y)); _obs[k++] = (float)(-2 * (y * z + w * x)); _obs[k++] = (float)(-(1 - 2 * (x * x + y * y)));
    _obs[k++] = _cmd.x; _obs[k++] = _cmd.y; _obs[k++] = _cmd.z;
    for (int i = 0; i < N; i++) _obs[k++] = (float)(d->qpos[_jq[i]] - MattSpec.DefaultPose[i]);
    for (int i = 0; i < N; i++) _obs[k++] = (float)d->qvel[_jd[i]];
    for (int i = 0; i < N; i++) _obs[k++] = _lastAction[i];
    double dphi = 2 * Math.PI * MattSpec.SimDt * MattSpec.Decimation * MattSpec.GaitHz;
    int n = Math.Max(0, _ctrlStep - 1);
    double pl = Wrap(n * dphi), pr = Wrap(Math.PI + n * dphi);
    _obs[k++] = (float)Math.Cos(pl); _obs[k++] = (float)Math.Cos(pr); _obs[k++] = (float)Math.Sin(pl); _obs[k++] = (float)Math.Sin(pr);
  }
  static double Wrap(double p) { p = (p + Math.PI) % (2 * Math.PI); if (p < 0) p += 2 * Math.PI; return p - Math.PI; }

  public void Canonical(MujocoLib.mjData_* d, double[] qpos, double[] qvel, double[] ctrl) {
    for (int i = 0; i < 7; i++) qpos[i] = d->qpos[BaseQpos + i];
    for (int i = 0; i < 6; i++) qvel[i] = d->qvel[BaseDof + i];
    for (int i = 0; i < N; i++) { qpos[7 + i] = d->qpos[_jq[i]]; qvel[6 + i] = d->qvel[_jd[i]]; ctrl[i] = d->ctrl[_act[i]]; }
  }

  public void RestartCommandRamp() { _cmd = Vector3.zero; }

  Transform _baseTr;
  /// <summary>Unity transform of the pelvis body (kept in sync by the MuJoCo plugin); for cameras and UI.</summary>
  public Transform BaseTransform { get { if (_baseTr == null) { var go = GameObject.Find("pelvis"); if (go != null) _baseTr = go.transform; } return _baseTr; } }

  /// <summary>Gameplay: lay Matt on his back in place (state write only) - AutoReset brings him back to the checkpoint.</summary>
  public void KnockOver() {
    var d = MjScene.Instance.Data; if (!_ready) return;
    d->qpos[BaseQpos + 2] = 0.35;
    d->qpos[BaseQpos + 3] = Math.Cos(Math.PI / 4); d->qpos[BaseQpos + 4] = 0; d->qpos[BaseQpos + 5] = -Math.Sin(Math.PI / 4); d->qpos[BaseQpos + 6] = 0;   // pitched 90 deg back
    for (int i = 0; i < 6; i++) d->qvel[BaseDof + i] = 0;
    MujocoLib.mj_forward(MjScene.Instance.Model, d);
  }
}

}
