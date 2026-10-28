using UnityEngine;

namespace PoRace {

/// <summary>What a race needs from any racing body sharing the race's MuJoCo model (Go2Controller, MattController):
/// its base addresses, a steering command, spawn / reset at a checkpoint, the policy state and the facts about its
/// policy's training envelope and feet that the race and broadcast layers need, so they don't branch on the creature.
/// Leg strain and effort sensors still read the concrete controller.</summary>
public unsafe interface IRacer {
  GameObject gameObject { get; }
  string name { get; }
  bool Ready { get; }
  /// <summary>Joystick command (vx m/s, vy m/s, yaw rate rad/s), body frame.</summary>
  Vector3 Command { get; set; }
  /// <summary>Slew-limited command actually fed to the policy.</summary>
  Vector3 FilteredCommand { get; }
  Vector3 SpawnMj { get; set; }
  float SpawnYaw { get; set; }
  /// <summary>Base height above the surface at the home pose (spawn height).</summary>
  float HomeHeight { get; }
  int BaseBody { get; }
  int BaseQpos { get; }
  int BaseDof { get; }
  /// <summary>sensordata address of the body's up vector (z component at +2): fallen / stumble state.</summary>
  int UpAdr { get; }
  Transform BaseTransform { get; }
  bool Upright { get; }
  /// <summary>True while the locomotion policy (not a getup policy or a hold) drives the joints.</summary>
  bool Locomoting { get; }
  string ModeName { get; }
  float SimDt { get; }
  /// <summary>Largest yaw-rate command inside the policy's trained (and graduated) range, rad/s.</summary>
  float MaxYawRate { get; }
  /// <summary>The race's un-stick (restart the command ramp when stalled at speed) helps this policy.</summary>
  bool UsesUnstick { get; }
  /// <summary>Steer to hold the track heading during the countdown (a policy that drifts on a zero command).</summary>
  bool HoldsHeadingAtRest { get; }
  /// <summary>The policy was trained on flat ground only: no banks, no obstacles.</summary>
  bool FlatGroundOnly { get; }
  /// <summary>Foot geom names in the shared MuJoCo model, in foot-index order.</summary>
  string[] FootGeomNames { get; }
  /// <summary>Effective striking mass of a foot as a multiple of the per-foot base mass (shank + foot for a human).</summary>
  float FootStrikeMassScale { get; }
  void ResetToHome();
  void KnockOver();
  void RestartCommandRamp();
  /// <summary>Back to the locomotion policy with a zero command (race restart).</summary>
  void StartLocomotion();
}

/// <summary>Pieces shared by the creature controllers (Go2, Matt, G1) and the race loop. MuJoCo frame (x, y, z up);
/// a free joint's qpos is position 3 + quaternion (w, x, y, z).</summary>
public static unsafe class RacerMath {
  /// <summary>Heading about z of the free joint whose qpos starts at `adr`.</summary>
  public static double Yaw(double* qpos, int adr) {
    double qw = qpos[adr + 3], qx = qpos[adr + 4], qy = qpos[adr + 5], qz = qpos[adr + 6];
    return System.Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
  }

  /// <summary>Put the free joint whose qpos starts at `adr` at `pos`, turned `yaw` rad about z.</summary>
  public static void Place(double* qpos, int adr, Vector3 pos, float yaw) {
    qpos[adr] = pos.x; qpos[adr + 1] = pos.y; qpos[adr + 2] = pos.z;
    qpos[adr + 3] = System.Math.Cos(yaw * 0.5); qpos[adr + 4] = 0; qpos[adr + 5] = 0; qpos[adr + 6] = System.Math.Sin(yaw * 0.5);
  }

  /// <summary>Sensor part of an observation, snapshotted before mj_step1 (trainer sensor age) into
  /// sens = local linvel 3, gyro 3, orientation quaternion 4, up-vector z.</summary>
  public static void Snapshot(double* sensordata, int linvel, int gyro, int quat, int up, double[] sens) {
    for (int i = 0; i < 3; i++) { sens[i] = sensordata[linvel + i]; sens[3 + i] = sensordata[gyro + i]; }
    for (int i = 0; i < 4; i++) sens[6 + i] = sensordata[quat + i];
    sens[10] = sensordata[up + 2];
  }

  /// <summary>Projected gravity R^T * (0, 0, -1) from the snapshot quaternion (sens[6..9]), appended to obs at k.</summary>
  public static void Gravity(double[] sens, float[] obs, ref int k) {
    double w = sens[6], x = sens[7], y = sens[8], z = sens[9];
    obs[k++] = (float)(-2 * (x * z - w * y));
    obs[k++] = (float)(-2 * (y * z + w * x));
    obs[k++] = (float)(-(1 - 2 * (x * x + y * y)));
  }

  /// <summary>Total mass of the bodies whose root is `root` (one racer in a shared model).</summary>
  public static double RootMass(Mujoco.MujocoLib.mjModel_* m, int root) {
    double mass = 0; for (int b = 0; b < (int)m->nbody; b++) if (m->body_rootid[b] == root) mass += m->body_mass[b];
    return mass;
  }

  /// <summary>Slew-limited joystick command: each component moves at most `step` toward the target.</summary>
  public static Vector3 Slew(Vector3 from, Vector3 to, float step) =>
    new Vector3(Mathf.MoveTowards(from.x, to.x, step), Mathf.MoveTowards(from.y, to.y, step), Mathf.MoveTowards(from.z, to.z, step));

  /// <summary>Two-foot gait clock of mujoco_playground's G1 env at control step n: cos L, cos R, sin L, sin R.</summary>
  public static void GaitPhase(float[] obs, ref int k, int n, double dphi) {
    double pl = Wrap(n * dphi), pr = Wrap(System.Math.PI + n * dphi);
    obs[k++] = (float)System.Math.Cos(pl); obs[k++] = (float)System.Math.Cos(pr); obs[k++] = (float)System.Math.Sin(pl); obs[k++] = (float)System.Math.Sin(pr);
  }
  static double Wrap(double p) { p = (p + System.Math.PI) % (2 * System.Math.PI); if (p < 0) p += 2 * System.Math.PI; return p - System.Math.PI; }

  /// <summary>Headless harness flag "-command vx vy yaw" (the last one wins); false when absent.</summary>
  public static bool TryCommandArg(out Vector3 cmd) {
    cmd = Vector3.zero; if (!CliArgs.TryFloats("-command", 3, out var v)) return false;
    cmd = new Vector3(v[0], v[1], v[2]); return true;
  }
}

}
