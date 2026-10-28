using Mujoco;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PoRace {

/// <summary>B2 motor heat shimmer: you can see a racer working hard. Every joint whose RaceSensors effort
/// (|actuator_force| / forcerange, smoothed) is over `threshold` gets a small camera-facing refraction quad
/// (PoRace/HeatShimmer) at its anchor (mjData xanchor, read only), stronger and larger the closer it runs to its limit.
/// A fixed pool of maxSprites quads, filled racer by racer, nothing beyond cullDistance from the camera. PC only
/// (GfxTier.HeatShimmer: it needs the camera opaque texture, turned on here for the view camera); Mobile keeps
/// StrainMonitor's leg tint and redline pulse. Headless runs skip it.</summary>
public unsafe class HeatShimmer : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public Material material;
  [Range(0f, 1f)] public float threshold = 0.8f;
  public float size = 0.14f, cullDistance = 12f;
  public int maxSprites = 8;

  Transform[] _t; MeshRenderer[] _r; MaterialPropertyBlock _mpb; Camera _cam;
  static readonly int Strength = Shader.PropertyToID("_Strength");

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race)) || !BroadcastUi.Wired(this, sensors, nameof(sensors))) return;
    _cam = race.ViewCamera;
    if (BroadcastUi.Headless || !GfxTier.HeatShimmer || material == null || _cam == null) { enabled = false; return; }
    _cam.GetUniversalAdditionalCameraData().requiresColorTexture = true;
    _mpb = new MaterialPropertyBlock();
    _t = new Transform[maxSprites]; _r = new MeshRenderer[maxSprites];
    for (int i = 0; i < maxSprites; i++) {
      var go = new GameObject("shimmer" + i); go.transform.SetParent(transform, false);
      go.AddComponent<MeshFilter>().sharedMesh = ImpactFx.Quad();
      var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
      _t[i] = go.transform; _r[i] = r;
    }
  }

  void LateUpdate() {
    int used = 0;
    if (MjScene.InstanceExists && race.Phase == RacePhase.Racing) {
      var m = MjScene.Instance.Model; var d = MjScene.Instance.Data;
      if (m != null && d != null) {
        var cp = _cam.transform.position; var rot = _cam.transform.rotation; float cull = cullDistance * cullDistance;
        for (int ri = 0; ri < race.Live.Count && used < maxSprites; ri++) {
          var s = sensors.Of(race.Live[ri]); if (s == null) continue;
          for (int i = 0; i < s.effort.Length && used < maxSprites; i++) {
            float e = s.effort[i]; if (e < threshold) continue;
            int a = s.act[i]; if (a < 0 || m->actuator_trntype[a] != 0) continue;   // mjTRN_JOINT
            int j = m->actuator_trnid[2 * a];
            var p = new Vector3((float)d->xanchor[3 * j], (float)d->xanchor[3 * j + 2], (float)d->xanchor[3 * j + 1]);
            if ((p - cp).sqrMagnitude > cull) continue;
            float k = Mathf.Clamp01((e - threshold) / Mathf.Max(0.01f, 1f - threshold));
            _t[used].SetPositionAndRotation(p, rot); _t[used].localScale = Vector3.one * (size * (0.7f + 0.6f * k));
            _mpb.SetFloat(Strength, 0.35f + 0.65f * k); _r[used].SetPropertyBlock(_mpb); _r[used].enabled = true;
            used++;
          }
        }
      }
    }
    for (int i = used; i < maxSprites; i++) if (_r[i].enabled) _r[i].enabled = false;
  }
}

}
