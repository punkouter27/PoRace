using UnityEngine;
using UnityEngine.Rendering;

namespace PoRace {

/// <summary>B1 speed feel. When the director's subject (the leader on PACK) runs faster than lineSpeed, or 20 % over its
/// own cruise for slower creatures, the view camera's FOV kicks out by up to fovKick degrees and a radial streak overlay
/// fades in, re-rolled every few frames so it flickers. The overlay is one square PoRace/Decal quad just inside the near
/// plane (a generated streak texture, clear centre), on both tiers: one draw, no renderer feature asset to keep in
/// sync. The FOV kick is added only for this camera's render (beginCameraRendering) and taken off after it, so
/// whoever owns the FOV keeps owning it. Off outside RACING, in slow motion and replays, and on the fixed trackside and
/// overhead shots. Headless runs skip it.</summary>
public class SpeedLines : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceDirector director;
  public RaceSensors sensors;
  public Material decalMaterial;
  public float lineSpeed = 2f, fovKick = 4f, maxAlpha = 0.28f, rampSpeed = 0.8f;

  Camera _cam; Transform _quad; MeshRenderer _r; MaterialPropertyBlock _mpb; float _k, _applied; int _flick;
  static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (!BroadcastUi.Wired(this, sensors, nameof(sensors))) return;
    _cam = race.ViewCamera;
    if (BroadcastUi.Headless || _cam == null || decalMaterial == null) { enabled = false; return; }
    var mat = new Material(decalMaterial) { name = "SpeedLines", renderQueue = 3100 };   // after the other transparents
    mat.SetTexture("_BaseMap", Streaks()); mat.SetColor(BaseColor, Color.white);
    var go = new GameObject("SpeedLines"); go.transform.SetParent(_cam.transform, false);
    go.AddComponent<MeshFilter>().sharedMesh = ImpactFx.Quad();
    _r = go.AddComponent<MeshRenderer>(); _r.sharedMaterial = mat; _r.shadowCastingMode = ShadowCastingMode.Off; _r.receiveShadows = false; _r.enabled = false;
    _quad = go.transform; _mpb = new MaterialPropertyBlock();
    RenderPipelineManager.beginCameraRendering += Begin; RenderPipelineManager.endCameraRendering += End;
  }

  void OnDestroy() { RenderPipelineManager.beginCameraRendering -= Begin; RenderPipelineManager.endCameraRendering -= End; }

  void Begin(ScriptableRenderContext c, Camera cam) { if (cam != _cam || _k <= 0.001f) return; _applied = fovKick * _k; cam.fieldOfView += _applied; }
  void End(ScriptableRenderContext c, Camera cam) { if (cam != _cam || _applied == 0f) return; cam.fieldOfView -= _applied; _applied = 0f; }

  void LateUpdate() {
    if (race.Live.Count == 0) return;
    var subject = director != null && director.Subject != null ? director.Subject : race.Leader();
    var s = subject != null ? sensors.Of(subject) : null;
    var shot = director != null ? director.Current : Shot.Pack;
    bool on = s != null && race.Phase == RacePhase.Racing && Time.timeScale > 0.99f && subject.finishTime < 0
      && shot != Shot.Trackside && shot != Shot.Overhead && shot != Shot.Replay;
    float thr = subject != null && subject.speed > 0.1f ? Mathf.Min(lineSpeed, 1.2f * subject.speed) : lineSpeed;
    float want = on ? Mathf.Clamp01((s.speed - thr) / rampSpeed) : 0f;
    _k = Mathf.MoveTowards(_k, want, Time.unscaledDeltaTime * (want > _k ? 2f : 1.2f));
    _r.enabled = _k > 0.01f;
    if (!_r.enabled) return;
    // A square that covers the (kicked) frustum at this depth, so any roll still fills the screen.
    float z = _cam.nearClipPlane + 0.02f, h = 2f * z * Mathf.Tan(0.5f * Mathf.Deg2Rad * (_cam.fieldOfView + fovKick * _k));
    float side = 1.05f * h * Mathf.Sqrt(1f + _cam.aspect * _cam.aspect);
    if (++_flick % 3 == 0) _quad.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    _quad.localPosition = new Vector3(0f, 0f, z); _quad.localScale = new Vector3(side, side, 1f);
    _mpb.SetColor(BaseColor, new Color(1f, 1f, 1f, maxAlpha * _k * Random.Range(0.85f, 1f))); _r.SetPropertyBlock(_mpb);
  }

  /// <summary>Thin radial streaks in random directions, clear in the middle third of the screen.</summary>
  static Texture2D Streaks() {
    var rng = new System.Random(5); const int Bins = 160;
    var bin = new float[Bins]; for (int i = 0; i < Bins; i++) bin[i] = rng.NextDouble() < 0.3 ? 0.4f + 0.6f * (float)rng.NextDouble() : 0f;
    return ImpactFx.Tex(256, 256, (u, v) => {
      float r = Mathf.Sqrt(u * u + v * v), a = (Mathf.Atan2(v, u) / (2f * Mathf.PI) + 0.5f) * Bins;
      return bin[Mathf.Clamp((int)a, 0, Bins - 1)] * Mathf.SmoothStep(0.38f, 0.95f, r);
    }, "SpeedLines");
  }
}

}
