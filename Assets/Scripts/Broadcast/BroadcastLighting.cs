using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>H.4 lighting for the broadcast:
///   spotlight  PC: one soft-shadowed spot follows the director's subject (the leader on PACK), hung 5 m up on the
///              camera's side so the pool of light and its shadow face the viewer
///   shadows    Mobile: only the subject casts a real-time shadow; every other robot gets a soft blob on the ground
///              under it (PoRace/Decal quad, tilted to the ground slope on banks), so the pack stays grounded for the
///              price of four quads instead of four shadow casters
/// Visual only. Headless runs skip it.</summary>
public class BroadcastLighting : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceDirector director;
  public Material decalMaterial;
  public float spotHeight = 5f, spotIntensity = 8f, spotAngle = 30f;
  public Color spotColor = new Color(1f, 0.95f, 0.85f);

  Light _spot; Camera _cam; Racer _shadowed;
  class Blob { public Transform t; public MeshRenderer r; public Renderer[] body; }
  readonly Dictionary<Racer, Blob> _blobs = new Dictionary<Racer, Blob>();

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    _cam = race.ViewCamera;
    if (GfxTier.Spotlight) {
      var go = new GameObject("SubjectSpot"); go.transform.SetParent(transform, false);
      _spot = go.AddComponent<Light>(); _spot.type = LightType.Spot; _spot.spotAngle = spotAngle; _spot.innerSpotAngle = spotAngle * 0.45f;
      _spot.range = spotHeight * 2.5f; _spot.intensity = spotIntensity; _spot.color = spotColor;
      _spot.shadows = LightShadows.Soft; _spot.shadowStrength = 0.65f;
    }
    if (GfxTier.BlobShadows && decalMaterial != null) {
      var mat = new Material(decalMaterial) { name = "Blob" };
      mat.SetTexture("_BaseMap", ImpactFx.Radial(64, 0.15f)); mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0.55f));
      foreach (var r in race.Live) {
        var b = r.Body.BaseTransform; if (b == null) continue;
        var go = new GameObject("blob_" + r.name); go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = ImpactFx.Quad();
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        go.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
        _blobs[r] = new Blob { t = go.transform, r = mr, body = b.GetComponentsInChildren<Renderer>() };
      }
    }
  }

  Racer Subject() => director != null && director.Subject != null ? director.Subject : race.Leader();

  float Ground(Vector3 p) => race.followCam != null && race.followCam.groundHeight != null ? race.followCam.groundHeight(p) : 0f;

  void LateUpdate() {
    var subject = Subject();
    if (_spot != null && subject != null && subject.Body.BaseTransform != null) {
      var p = subject.Body.BaseTransform.position;
      var side = _cam != null ? Vector3.ProjectOnPlane(_cam.transform.position - p, Vector3.up).normalized : Vector3.back;
      var want = p + Vector3.up * spotHeight + side * 1.5f;
      _spot.transform.position = Vector3.Lerp(_spot.transform.position, want, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
      _spot.transform.rotation = Quaternion.LookRotation(p - _spot.transform.position);
    }
    if (_blobs.Count == 0) return;
    if (subject != _shadowed) {   // only the subject casts a real shadow
      _shadowed = subject;
      foreach (var kv in _blobs) {
        var mode = kv.Key == subject ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var r in kv.Value.body) if (r != null) r.shadowCastingMode = mode;
        kv.Value.r.enabled = kv.Key != subject;
      }
    }
    foreach (var kv in _blobs) {
      var b = kv.Key.Body.BaseTransform; if (b == null || !kv.Value.r.enabled) continue;
      var p = b.position; float h = Ground(p);
      float hx = Ground(p + Vector3.right * 0.3f) - Ground(p - Vector3.right * 0.3f), hz = Ground(p + Vector3.forward * 0.3f) - Ground(p - Vector3.forward * 0.3f);
      var n = new Vector3(-hx / 0.6f, 1f, -hz / 0.6f).normalized;
      float lift = Mathf.Clamp01(1f - (p.y - h - 0.3f) / 0.8f);   // fades as the robot leaves the ground
      kv.Value.t.SetPositionAndRotation(new Vector3(p.x, h + 0.005f, p.z), Quaternion.FromToRotation(Vector3.back, n));
      kv.Value.t.localScale = new Vector3(0.75f, 0.75f, 1f) * (0.7f + 0.3f * lift);
    }
  }
}

}
