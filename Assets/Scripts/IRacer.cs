using UnityEngine;

namespace PoRace {

/// <summary>What a race needs from any racing body sharing the race's MuJoCo model (Go2Controller, MattController):
/// its base addresses, a steering command, spawn / reset at a checkpoint and the policy state. Creature-specific
/// systems (leg strain, foot impacts, effort sensors) keep using the concrete controller.</summary>
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
  void ResetToHome();
  void KnockOver();
  void RestartCommandRamp();
  /// <summary>Back to the locomotion policy with a zero command (race restart).</summary>
  void StartLocomotion();
}

}
