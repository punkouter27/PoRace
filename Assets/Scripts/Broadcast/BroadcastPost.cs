using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PoRace {

/// <summary>H.4 post-processing, three global volumes built at start (no profile assets to keep in sync):
///   base      one look per map (exposure, contrast, saturation, colour filter, bloom) on a neutral tonemapper; rain
///             (TrackWeather) pulls saturation down a little
///   incident  weight follows the director's INCIDENT slow motion: desaturated, plus vignette and chromatic aberration
///   finish    a bloom and exposure kick when the winner crosses the line, decaying over 1.2 s
/// Mobile (GfxTier) keeps bloom and colour grading, which share URP's single uber pass, and drops vignette and
/// chromatic aberration. The HUD canvas is screen-space overlay, so none of this touches text.
/// Headless runs skip it.</summary>
public class BroadcastPost : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceDirector director;
  public TrackWeather weather;

  struct Look { public float exposure, contrast, saturation, bloom; public Color filter; }

  /// <summary>Per-map grade, keyed by scene name.</summary>
  static Look For(string scene) {
    switch (scene) {
      case "Track02": return new Look { exposure = 0.05f, contrast = 12f, saturation = 8f, bloom = 0.45f, filter = new Color(1f, 0.95f, 0.88f) };    // late afternoon
      case "Track03": return new Look { exposure = 0f, contrast = 18f, saturation = -6f, bloom = 0.35f, filter = new Color(0.94f, 0.98f, 1.04f) };    // overcast, hard
      case "Track04": return new Look { exposure = 0.1f, contrast = 10f, saturation = 10f, bloom = 0.55f, filter = new Color(1f, 0.9f, 0.78f) };      // golden hour
      default: return new Look { exposure = 0f, contrast = 8f, saturation = 6f, bloom = 0.35f, filter = new Color(1f, 0.98f, 0.95f) };              // clear noon
    }
  }

  Volume _base, _incident, _finish;
  ColorAdjustments _grade; float _baseSaturation, _finishKick;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (weather == null) weather = FindAnyObjectByType<TrackWeather>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    var cam = race.followCam != null ? race.followCam.GetComponent<Camera>() : Camera.main;
    if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    bool full = GfxTier.FullPost;

    var look = For(gameObject.scene.name);
    _base = MakeVolume("PostBase", 0f, out var p);
    p.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
    _grade = p.Add<ColorAdjustments>();
    _grade.postExposure.Override(look.exposure); _grade.contrast.Override(look.contrast); _grade.saturation.Override(look.saturation); _grade.colorFilter.Override(look.filter);
    _baseSaturation = look.saturation;
    var bloom = p.Add<Bloom>();
    bloom.threshold.Override(0.95f); bloom.intensity.Override(look.bloom); bloom.scatter.Override(0.6f); bloom.highQualityFiltering.Override(full);
    if (!full) bloom.downscale.Override(BloomDownscaleMode.Quarter);
    if (full) { var v = p.Add<Vignette>(); v.intensity.Override(0.2f); v.smoothness.Override(0.45f); }
    _base.weight = 1f;

    _incident = MakeVolume("PostIncident", 10f, out p);
    p.Add<ColorAdjustments>().saturation.Override(-45f);
    if (full) { p.Add<Vignette>().intensity.Override(0.42f); p.Add<ChromaticAberration>().intensity.Override(0.45f); }

    _finish = MakeVolume("PostFinish", 20f, out p);
    p.Add<Bloom>().intensity.Override(look.bloom + 1.6f);
    p.Add<ColorAdjustments>().postExposure.Override(look.exposure + 0.35f);
    race.Finished += r => { if (r.place == 1) _finishKick = 1f; };
    race.Restarted += () => _finishKick = 0f;
  }

  Volume MakeVolume(string name, float priority, out VolumeProfile profile) {
    var go = new GameObject(name); go.transform.SetParent(transform, false);
    var v = go.AddComponent<Volume>(); v.isGlobal = true; v.priority = priority; v.weight = 0f;
    profile = ScriptableObject.CreateInstance<VolumeProfile>(); profile.name = name; v.sharedProfile = profile;
    return v;
  }

  void Update() {
    bool slowIncident = director != null && director.Current == Shot.Incident && Time.timeScale < 0.99f;
    _incident.weight = Mathf.MoveTowards(_incident.weight, slowIncident ? 1f : 0f, Time.unscaledDeltaTime * 3f);
    _finishKick = Mathf.Max(0f, _finishKick - Time.unscaledDeltaTime / 1.2f);
    _finish.weight = _finishKick * _finishKick;
    float rain = weather != null && weather.Active ? Mathf.Clamp01(weather.RainMm / 2f) : 0f;
    _grade.saturation.value = _baseSaturation - 12f * rain;
  }
}

}
