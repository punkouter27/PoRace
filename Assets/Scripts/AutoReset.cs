using Mujoco;
using UnityEngine;

namespace PoRace {

public enum RaceState { RACING, STUMBLE, FALLEN_RECOVERING }

/// <summary>D.2: race state from MuJoCo state only, and automatic reset to the home keyframe when the racer stays down,
/// stalls, or leaves the arena. Reset = IRacer.ResetToHome (qpos/qvel/ctrl buffers), no scene reload.</summary>
public unsafe class AutoReset : MonoBehaviour {
  public Go2Controller controller;
  [Tooltip("Set instead of `controller` for Matt (no getup policy: a fall ends in a reset to the last checkpoint).")]
  public MattController matt;
  public IRacer Body => matt != null ? matt : controller;
  public float fallenSeconds = 5f;
  public float stallSeconds = 10f;
  public float stallSpeed = 0.05f;   // m/s; only counts while a non-zero command is active
  public float arenaRadius = 15f;
  public RaceState State { get; private set; } = RaceState.RACING;
  public int Resets { get; private set; }

  float _fallenFor, _stalledFor;

  void Awake() { MjScene.Instance.postUpdateEvent += OnPostStep; }
  void OnDestroy() { if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= OnPostStep; }

  void OnPostStep(object sender, MjStepArgs a) {
    var B = Body; if (B == null || !B.Ready || !enabled) return;
    var d = a.data;
    float dt = B.SimDt;
    double upZ = d->sensordata[B.UpAdr + 2];
    float x = (float)d->qpos[B.BaseQpos], y = (float)d->qpos[B.BaseQpos + 1];
    float speed = Mathf.Sqrt((float)(d->qvel[B.BaseDof] * d->qvel[B.BaseDof] + d->qvel[B.BaseDof + 1] * d->qvel[B.BaseDof + 1]));

    State = upZ < 0.0 ? RaceState.FALLEN_RECOVERING : upZ < 0.7 ? RaceState.STUMBLE : RaceState.RACING;
    _fallenFor = State == RaceState.FALLEN_RECOVERING ? _fallenFor + dt : 0f;
    // Dogs: the requested mode (a stall also counts while the getup policy runs), as before Matt joined.
    bool wantsToMove = (controller != null && matt == null ? controller.mode == Go2Mode.Locomotion : B.Locomoting) && B.Command.sqrMagnitude > 0.01f;
    _stalledFor = wantsToMove && speed < stallSpeed ? _stalledFor + dt : 0f;

    bool nan = double.IsNaN(d->qpos[B.BaseQpos + 2]) || double.IsNaN(d->qvel[B.BaseDof]);  // solver blow-up guard, same as the trainer
    if (nan || _fallenFor > fallenSeconds || _stalledFor > stallSeconds || x * x + y * y > arenaRadius * arenaRadius) {
      Debug.Log($"[AutoReset] {B.name} {(nan ? "NaN state " : "")}{State} fallen={_fallenFor:F1}s stalled={_stalledFor:F1}s pos=({x:F1},{y:F1}) -> home");
      B.ResetToHome();
      _fallenFor = _stalledFor = 0f; Resets++;
    }
  }
}

}
