using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 10, gait-driven sound and effects, all synthesised at start (no audio assets):
///   footstep   short filtered noise + thump on every foot strike (RaceSensors), louder with speed, 3D at the foot
///   dust       a puff of particles at the foot on strikes above 0.6 m/s, a burst on falls and cube hits
///   servo whine one looping 3D source per racer: pitch follows joint speed, volume follows joint effort
///   impact     thud + camera shake when a cube hits (speed lost from RaceSensors)
///   crowd      ambience bed, cheer swell on overtakes and the finish, countdown beeps
/// Headless runs skip all of it.</summary>
public class RaceAudio : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public RaceDirector director;
  public Material particleMaterial;
  [Range(0f, 1f)] public float master = 0.8f;

  AudioClip _step, _thud, _whine, _crowd, _cheer, _beep, _go;
  readonly List<AudioSource> _pool = new List<AudioSource>(); int _poolNext;
  readonly Dictionary<Racer, AudioSource> _whines = new Dictionary<Racer, AudioSource>();
  AudioSource _bed, _ui; ParticleSystem _dust; int _lastBeep = -1; float _cheerUntil;
  const int Rate = 44100;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    var cam = race.followCam != null ? race.followCam.gameObject : Camera.main?.gameObject;
    if (cam != null && FindAnyObjectByType<AudioListener>() == null) cam.AddComponent<AudioListener>();

    var rng = new System.Random(7);
    _step = Make("step", 0.07f, (t, i) => (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 70f) * 0.6f + Mathf.Sin(2 * Mathf.PI * 95f * t) * Mathf.Exp(-t * 45f) * 0.8f, lowpass: 0.35f);
    _thud = Make("thud", 0.3f, (t, i) => Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(120f, 45f, t / 0.3f) * t) * Mathf.Exp(-t * 14f) + (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 40f) * 0.4f, lowpass: 0.25f);
    _whine = Make("whine", 1f, (t, i) => 0.5f * Mathf.Sin(2 * Mathf.PI * 330f * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * 660f * t) + 0.12f * Mathf.Sin(2 * Mathf.PI * 990f * t));
    _crowd = Make("crowd", 3f, (t, i) => (float)(rng.NextDouble() * 2 - 1) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.33f * t + 1f)) * 0.5f, lowpass: 0.08f);
    _cheer = Make("cheer", 2.2f, (t, i) => (float)(rng.NextDouble() * 2 - 1) * Mathf.Sin(Mathf.PI * t / 2.2f) * (0.8f + 0.2f * Mathf.Sin(2 * Mathf.PI * 7f * t)), lowpass: 0.15f);
    _beep = Make("beep", 0.15f, (t, i) => Mathf.Sin(2 * Mathf.PI * 880f * t) * Mathf.Min(1f, (0.15f - t) * 40f) * 0.5f);
    _go = Make("go", 0.45f, (t, i) => Mathf.Sin(2 * Mathf.PI * 1320f * t) * Mathf.Min(1f, (0.45f - t) * 20f) * 0.5f);

    for (int i = 0; i < 16; i++) { var s = new GameObject("sfx" + i).AddComponent<AudioSource>(); s.transform.SetParent(transform); s.spatialBlend = 1f; s.minDistance = 1f; s.maxDistance = 25f; s.rolloffMode = AudioRolloffMode.Linear; s.playOnAwake = false; _pool.Add(s); }
    _ui = gameObject.AddComponent<AudioSource>(); _ui.spatialBlend = 0f; _ui.playOnAwake = false;
    _bed = gameObject.AddComponent<AudioSource>(); _bed.clip = _crowd; _bed.loop = true; _bed.volume = 0.12f * master; _bed.spatialBlend = 0f; _bed.Play();
    foreach (var r in race.Live) {
      var b = r.controller.BaseTransform; if (b == null) continue;
      var w = b.gameObject.AddComponent<AudioSource>(); w.clip = _whine; w.loop = true; w.spatialBlend = 1f; w.minDistance = 0.8f; w.maxDistance = 15f;
      w.rolloffMode = AudioRolloffMode.Linear; w.volume = 0f; w.time = Random.Range(0f, 0.9f); w.Play(); _whines[r] = w;
    }
    _dust = MakeDust();

    sensors.FootStrike += OnStrike;
    sensors.CubeImpact += (c, pos, lost, hit) => { Play(_thud, pos, Mathf.Clamp01(lost / 5f), Random.Range(0.85f, 1.1f)); Puff(pos, 25); if (hit != null && director != null && director.Subject == hit) director.Shake(0.08f, 0.35f); };
    race.Fell += r => { if (r.controller.BaseTransform != null) { Play(_thud, r.controller.BaseTransform.position, 0.9f, 0.8f); Puff(r.controller.BaseTransform.position, 40); } };
    race.Overtook += (a, b) => Cheer(0.35f);
    race.Finished += r => Cheer(r.place == 1 ? 0.9f : 0.4f);
  }

  /// <summary>Mono clip from a sample function f(time, index), with an optional one-pole low-pass (0..1, lower = darker).</summary>
  static AudioClip Make(string name, float seconds, System.Func<float, int, float> f, float lowpass = 1f) {
    int n = Mathf.CeilToInt(seconds * Rate); var data = new float[n]; float y = 0f;
    for (int i = 0; i < n; i++) { float x = f(i / (float)Rate, i); y += (x - y) * lowpass; data[i] = Mathf.Clamp(y, -1f, 1f); }
    var clip = AudioClip.Create(name, n, 1, Rate, false); clip.SetData(data, 0); return clip;
  }

  ParticleSystem MakeDust() {
    var go = new GameObject("Dust"); go.transform.SetParent(transform, false);
    var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
    var main = ps.main; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f); main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
    main.startColor = new Color(0.72f, 0.66f, 0.56f, 0.55f); main.gravityModifier = -0.02f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 1500;
    var em = ps.emission; em.rateOverTime = 0f;
    var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.05f; sh.rotation = new Vector3(-90f, 0f, 0f);
    var col = ps.colorOverLifetime; col.enabled = true;
    var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
    col.color = g;
    var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
    var rend = go.GetComponent<ParticleSystemRenderer>(); rend.sharedMaterial = particleMaterial; rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    ps.Play(); return ps;
  }

  void Puff(Vector3 pos, int count) {
    if (_dust == null) return;
    var ep = new ParticleSystem.EmitParams { position = pos + Vector3.up * 0.02f, applyShapeToPosition = true };
    _dust.Emit(ep, count);
  }

  void OnStrike(Racer r, int foot, Vector3 pos, float speed) {
    if (Camera.main != null && (Camera.main.transform.position - pos).sqrMagnitude > 30f * 30f) return;
    Play(_step, pos, 0.25f + 0.35f * Mathf.Clamp01(speed / 1.5f), Random.Range(0.85f, 1.2f));
    if (speed > 0.6f) Puff(pos, 3);
  }

  void Play(AudioClip clip, Vector3 pos, float volume, float pitch) {
    var s = _pool[_poolNext]; _poolNext = (_poolNext + 1) % _pool.Count;
    s.transform.position = pos; s.pitch = pitch; s.PlayOneShot(clip, volume * master);
  }

  void Cheer(float level) { _ui.PlayOneShot(_cheer, level * master * 0.6f); _cheerUntil = Time.unscaledTime + 2f; }

  void Update() {
    if (sensors == null) return;
    foreach (var kv in _whines) {
      var s = sensors.Of(kv.Key); if (s == null) continue;
      float effort = 0f; foreach (var e in s.effort) effort += e; effort /= s.effort.Length;
      float jointSpeed = s.power / Mathf.Max(1f, effort * RaceSensors.ForceRange * s.effort.Length);   // ~ mean |qdot|
      kv.Value.pitch = Mathf.Lerp(kv.Value.pitch, 0.7f + 0.06f * Mathf.Min(jointSpeed, 12f), 0.2f);
      kv.Value.volume = Mathf.Lerp(kv.Value.volume, master * Mathf.Clamp01(0.03f + 0.35f * effort), 0.2f);
    }
    _bed.volume = Mathf.Lerp(_bed.volume, master * (Time.unscaledTime < _cheerUntil ? 0.22f : 0.12f), 0.05f);
    if (race.Phase == RacePhase.Countdown) {
      int sec = Mathf.CeilToInt(race.CountdownLeft);
      if (sec != _lastBeep && sec <= 3 && sec > 0) _ui.PlayOneShot(_beep, 0.6f * master);
      _lastBeep = sec;
    } else if (_lastBeep > 0 && race.Phase == RacePhase.Racing) { _ui.PlayOneShot(_go, 0.7f * master); _lastBeep = 0; }
  }
}

}
