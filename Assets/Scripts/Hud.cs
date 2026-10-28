using Mujoco;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Portrait HUD. Anchors: TL title | TC fps + sim tick | TR behaviour / race state | BL reset / shove / cube | BR version.
/// TAB toggles the telemetry block (D.3). Buttons drive the creature only through MuJoCo state.</summary>
public class Hud : MonoBehaviour {
  public Go2Controller controller;
  public CubePool cubes;
  public Shove shove;
  [OptionalRef] public AutoReset autoReset;   // resolved from the controller in Awake
  public Text title, telemetry, behaviour, version;
  public bool telemetryVisible = true;
  int _ticks, _lastTicks; float _fpsTimer; int _frames; float _fps, _tickRate;
  Go2Mode _shownMode = (Go2Mode)(-1); RaceState _shownState = (RaceState)(-1);

  void Awake() {
    if (!Application.isBatchMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }  // do not burn the GPU at 500+ fps
    MjScene.Instance.postUpdateEvent += (s, a) => _ticks++;
    if (autoReset == null) autoReset = controller.GetComponent<AutoReset>();
  }

  void Start() {
    if (title) title.text = "PoRace Go2";
    if (version) version.text = BuildInfo.Watermark;   // YYMMDD build watermark, bottom-right
  }

  // Text is rebuilt only when it changes: behaviour on a mode / state change, telemetry twice a second.
  void Update() {
    if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) telemetryVisible = !telemetryVisible;
    _frames++; _fpsTimer += Time.unscaledDeltaTime;
    bool tick = _fpsTimer >= 0.5f;
    if (tick) {
      _fps = _frames / _fpsTimer; _tickRate = (_ticks - _lastTicks) / _fpsTimer;
      _frames = 0; _fpsTimer = 0f; _lastTicks = _ticks;
    }
    var state = autoReset != null ? autoReset.State : (controller.Upright ? RaceState.RACING : RaceState.FALLEN_RECOVERING);
    if (behaviour && (state != _shownState || controller.mode != _shownMode)) {
      _shownState = state; _shownMode = controller.mode; behaviour.text = $"{controller.mode}\n{state}";
    }
    if (telemetry) {
      if (telemetry.enabled != telemetryVisible) telemetry.enabled = telemetryVisible;
      if (tick && telemetryVisible)
        telemetry.text = $"{_fps:F0} fps | sim {_tickRate:F0} Hz | ctrl {controller.ControlStep} | inf {controller.LastInferenceMs:F2} ms\n{SpeedText()} | cmd {controller.command}";
    }
  }

  unsafe string SpeedText() {
    if (!MjScene.InstanceExists || controller.Model == null || MjScene.Instance.Data == null) return "v -";
    var d = MjScene.Instance.Data; var M = controller.Model;
    return $"v ({d->qvel[M.BaseDof]:F2}, {d->qvel[M.BaseDof + 1]:F2}) m/s";
  }

  [OptionalRef] public MultiRaceOrchestrator multiRace;   // null on the testbeds (single dog)
  public void OnReset() { if (multiRace != null) multiRace.Restart(); else controller.ResetToHome(); }
  public void OnShove() => Shove.PushRobot(multiRace != null ? multiRace.Leader().Body : controller, shove.impulse, shove.durationSteps);
  public void OnCube() => cubes.ThrowAtRobot(3f, multiRace != null ? multiRace.Leader().Body : null);
  public void OnDrop() => cubes.DropOnRobot(multiRace != null ? multiRace.Leader().Body : null);
  public void OnMode(int m) => controller.mode = (Go2Mode)m;
  public void OnMenu() { RaceConfig.autoplay = false; if (Application.CanStreamedLevelBeLoaded("Menu")) UnityEngine.SceneManagement.SceneManager.LoadScene("Menu"); }

  // Behaviour selector (TR). All presets only change the command / mode or write MuJoCo state.
  void Go(Vector3 cmd) { controller.mode = Go2Mode.Locomotion; controller.command = cmd; }
  public void OnStand() => Go(Vector3.zero);
  public void OnWalk() => Go(new Vector3(1.0f, 0f, 0f));
  public void OnRun() => Go(new Vector3(1.5f, 0f, 0f));
  public void OnTurn() => Go(new Vector3(0.6f, 0f, 0.8f));
  public void OnKnockOver() { if (multiRace != null) multiRace.Leader().Body.KnockOver(); else controller.KnockOver(); }
}

}
