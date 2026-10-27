using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 10 + H.2, gait and contact sound, all synthesised at start (no audio assets):
///   footstep   per foot landing (ContactImpulse), louder with impulse, timbre by surface: track thump, rail ping,
///              obstacle knock; a squelch layer on top when TrackWeather has made the track wet
///   impact     body, cube and robot hits crossfade three layers by impulse (tap / thud / slam, pitch falls as the hit
///              gets harder) plus a surface layer: rail clang, robot-on-robot clank, cube / obstacle knock
///   scrape     one looping 3D source per racer: volume and low-pass cutoff follow its fastest foot slip, darker and
///              louder on a wet track
///   servo whine one looping 3D source per racer: pitch follows joint speed, volume follows joint effort, Doppler on
///   crowd      ambience bed, cheer swell on overtakes and the finish, countdown beeps
/// Every 3D voice rolls off logarithmically and loses treble with distance (AudioLowPassFilter). Particles and camera
/// shake live in ImpactFx. Headless runs skip all of it.</summary>
public class RaceAudio : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public ContactImpulse contacts;
  public RaceDirector director;
  public TrackWeather weather;
  [Range(0f, 1f)] public float master = 0.8f;
  public float hearDistance = 40f;

  AudioClip _step, _stepMetal, _stepWood, _squelch, _tap, _thud, _slam, _clang, _clank, _knock, _scrape, _whine, _crowd, _cheer, _beep, _go;
  class Voice { public AudioSource s; public AudioLowPassFilter lp; }
  readonly List<Voice> _pool = new List<Voice>(); int _poolNext;
  class Loops { public AudioSource whine, scrape; public AudioLowPassFilter whineLp, scrapeLp; public float slip; }
  readonly Dictionary<Racer, Loops> _loops = new Dictionary<Racer, Loops>();
  AudioSource _bed, _ui; int _lastBeep = -1; float _cheerUntil;
  Transform _listener;
  const int Rate = 44100;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    if (contacts == null) contacts = FindAnyObjectByType<ContactImpulse>();
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (weather == null) weather = FindAnyObjectByType<TrackWeather>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    var cam = race.followCam != null ? race.followCam.gameObject : Camera.main?.gameObject;
    var listener = FindAnyObjectByType<AudioListener>();
    if (listener == null && cam != null) listener = cam.AddComponent<AudioListener>();
    _listener = listener != null ? listener.transform : null;

    var rng = new System.Random(7);
    float N() => (float)(rng.NextDouble() * 2 - 1);
    float S(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);
    _step = Make("step", 0.07f, (t, i) => N() * Mathf.Exp(-t * 70f) * 0.6f + S(95f, t) * Mathf.Exp(-t * 45f) * 0.8f, lowpass: 0.35f);
    _stepMetal = Make("stepMetal", 0.18f, (t, i) => (S(1830f, t) * 0.5f + S(2710f, t) * 0.3f) * Mathf.Exp(-t * 28f) + N() * Mathf.Exp(-t * 300f) * 0.4f);
    _stepWood = Make("stepWood", 0.1f, (t, i) => (S(410f, t) + 0.6f * S(660f, t)) * Mathf.Exp(-t * 55f) * 0.6f + N() * Mathf.Exp(-t * 200f) * 0.3f, lowpass: 0.5f);
    _squelch = Make("squelch", 0.16f, (t, i) => (N() * 0.5f + S(Mathf.Lerp(260f, 90f, t / 0.16f), t)) * Mathf.Sin(Mathf.PI * t / 0.16f) * 0.5f, lowpass: 0.12f);
    _tap = Make("tap", 0.12f, (t, i) => S(Mathf.Lerp(240f, 140f, t / 0.12f), t) * Mathf.Exp(-t * 40f) * 0.7f + N() * Mathf.Exp(-t * 120f) * 0.4f, lowpass: 0.4f);
    _thud = Make("thud", 0.3f, (t, i) => S(Mathf.Lerp(120f, 45f, t / 0.3f), t) * Mathf.Exp(-t * 14f) + N() * Mathf.Exp(-t * 40f) * 0.4f, lowpass: 0.25f);
    _slam = Make("slam", 0.65f, (t, i) => S(Mathf.Lerp(85f, 28f, t / 0.65f), t) * Mathf.Exp(-t * 6f) + N() * Mathf.Exp(-t * 18f) * 0.6f, lowpass: 0.15f);
    _clang = Make("clang", 0.9f, (t, i) => S(523f, t) * Mathf.Exp(-t * 4f) * 0.4f + S(1410f, t) * Mathf.Exp(-t * 6f) * 0.3f + S(2210f, t) * Mathf.Exp(-t * 9f) * 0.2f + S(3170f, t) * Mathf.Exp(-t * 14f) * 0.15f + N() * Mathf.Exp(-t * 150f) * 0.3f);
    _clank = Make("clank", 0.35f, (t, i) => (S(300f, t) * 0.5f + S(820f, t) * 0.35f + S(1530f, t) * 0.2f) * Mathf.Exp(-t * 12f) + N() * Mathf.Exp(-t * 60f) * 0.4f, lowpass: 0.6f);
    _knock = Make("knock", 0.25f, (t, i) => (S(280f, t) * 0.6f + S(510f, t) * 0.4f + S(900f, t) * 0.2f) * Mathf.Exp(-t * 22f) + N() * Mathf.Exp(-t * 90f) * 0.3f, lowpass: 0.45f);
    _scrape = Make("scrape", 1f, (t, i) => N() * (0.8f + 0.2f * S(13f, t)), lowpass: 0.3f);
    _whine = Make("whine", 1f, (t, i) => 0.5f * S(330f, t) + 0.25f * S(660f, t) + 0.12f * S(990f, t));
    _crowd = Make("crowd", 3f, (t, i) => N() * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.33f * t + 1f)) * 0.5f, lowpass: 0.08f);
    _cheer = Make("cheer", 2.2f, (t, i) => N() * Mathf.Sin(Mathf.PI * t / 2.2f) * (0.8f + 0.2f * S(7f, t)), lowpass: 0.15f);
    _beep = Make("beep", 0.15f, (t, i) => S(880f, t) * Mathf.Min(1f, (0.15f - t) * 40f) * 0.5f);
    _go = Make("go", 0.45f, (t, i) => S(1320f, t) * Mathf.Min(1f, (0.45f - t) * 20f) * 0.5f);

    for (int i = 0; i < 16; i++) {
      var go = new GameObject("sfx" + i); go.transform.SetParent(transform);
      var s = Spatial(go.AddComponent<AudioSource>(), 1.5f); s.dopplerLevel = 0f;
      _pool.Add(new Voice { s = s, lp = go.AddComponent<AudioLowPassFilter>() });
    }
    _ui = gameObject.AddComponent<AudioSource>(); _ui.spatialBlend = 0f; _ui.playOnAwake = false;
    _bed = gameObject.AddComponent<AudioSource>(); _bed.clip = _crowd; _bed.loop = true; _bed.volume = 0.12f * master; _bed.spatialBlend = 0f; _bed.Play();
    foreach (var r in race.Live) {
      var b = r.Body.BaseTransform; if (b == null) continue;
      var l = new Loops();
      // Each loop on its own child: one AudioLowPassFilter per GameObject filters every source on it.
      var wg = new GameObject("whine"); wg.transform.SetParent(b, false);
      l.whine = Loop(wg.AddComponent<AudioSource>(), _whine, 0.8f); l.whine.dopplerLevel = 0.6f; l.whine.spread = 40f; l.whineLp = wg.AddComponent<AudioLowPassFilter>();
      var sg = new GameObject("scrape"); sg.transform.SetParent(b, false);
      l.scrape = Loop(sg.AddComponent<AudioSource>(), _scrape, 1f); l.scrapeLp = sg.AddComponent<AudioLowPassFilter>();
      _loops[r] = l;
    }

    if (contacts != null) { contacts.Impact += OnImpact; contacts.Slip += (r, f, slip, surf, pos, n) => { if (_loops.TryGetValue(r, out var l)) l.slip = Mathf.Max(l.slip, slip); }; }
    race.Overtook += (a, b) => Cheer(0.35f);
    race.Finished += r => Cheer(r.place == 1 ? 0.9f : 0.4f);
  }

  AudioSource Spatial(AudioSource s, float minDistance) {
    s.spatialBlend = 1f; s.minDistance = minDistance; s.maxDistance = hearDistance; s.rolloffMode = AudioRolloffMode.Logarithmic; s.playOnAwake = false;
    return s;
  }

  AudioSource Loop(AudioSource s, AudioClip clip, float minDistance) {
    Spatial(s, minDistance); s.clip = clip; s.loop = true; s.volume = 0f; s.time = Random.Range(0f, clip.length * 0.9f); s.Play();
    return s;
  }

  /// <summary>Mono clip from a sample function f(time, index), with an optional one-pole low-pass (0..1, lower = darker).</summary>
  static AudioClip Make(string name, float seconds, System.Func<float, int, float> f, float lowpass = 1f) {
    int n = Mathf.CeilToInt(seconds * Rate); var data = new float[n]; float y = 0f;
    for (int i = 0; i < n; i++) { float x = f(i / (float)Rate, i); y += (x - y) * lowpass; data[i] = Mathf.Clamp(y, -1f, 1f); }
    var clip = AudioClip.Create(name, n, 1, Rate, false); clip.SetData(data, 0); return clip;
  }

  float Wetness => weather != null && weather.Active ? Mathf.Clamp01((1f - weather.Grip) / 0.3f) : 0f;

  /// <summary>Treble lost with distance: 22 kHz up close to 2.5 kHz at 30 m.</summary>
  float Cutoff(Vector3 pos) => _listener == null ? 22000f : Mathf.Lerp(22000f, 2500f, Mathf.Sqrt(Mathf.Clamp01(Vector3.Distance(_listener.position, pos) / 30f)));

  void OnImpact(ContactImpulse.Hit h) {
    if (_listener != null && (_listener.position - h.pos).sqrMagnitude > hearDistance * hearDistance) return;
    float pv = Random.Range(0.94f, 1.06f);
    if (h.kind == ContactKind.Foot) {
      if (h.impulse < 0.15f) return;   // feet sliding across bank pieces start new contacts with almost no approach speed
      float v = 0.2f + 0.4f * Mathf.Clamp01(h.impulse / 3f);
      var clip = h.surface == Surface.Metal ? _stepMetal : h.surface == Surface.Wood ? _stepWood : _step;
      Play(clip, h.pos, v, Random.Range(0.85f, 1.2f));
      float wet = Wetness; if (wet > 0f && h.surface == Surface.Track) Play(_squelch, h.pos, v * 0.7f * wet, Random.Range(0.85f, 1.15f));
      return;
    }
    float J = h.impulse, k = Mathf.Clamp01(J / 8f);
    float vol = 0.35f + 0.65f * k, pitch = Mathf.Lerp(1.15f, 0.8f, k) * pv;
    float tapW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 2f, J)), slamW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.5f, 7f, J));
    float thudW = Mathf.Clamp01(1f - tapW - slamW);
    if (tapW > 0.05f) Play(_tap, h.pos, vol * tapW, pitch);
    if (thudW > 0.05f) Play(_thud, h.pos, vol * thudW, pitch);
    if (slamW > 0.05f) Play(_slam, h.pos, vol * slamW, pitch);
    if (h.kind == ContactKind.RobotRobot) Play(_clank, h.pos, 0.3f + 0.6f * k, pitch);
    else if (h.surface == Surface.Metal) Play(_clang, h.pos, 0.25f + 0.65f * k, pv);
    else if (h.kind == ContactKind.Cube || h.surface == Surface.Wood) Play(_knock, h.pos, 0.3f + 0.6f * k, pitch);
  }

  void Play(AudioClip clip, Vector3 pos, float volume, float pitch) {
    var v = _pool[_poolNext]; _poolNext = (_poolNext + 1) % _pool.Count;
    v.s.transform.position = pos; v.s.pitch = pitch; v.lp.cutoffFrequency = Cutoff(pos); v.s.PlayOneShot(clip, volume * master);
  }

  void Cheer(float level) { _ui.PlayOneShot(_cheer, level * master * 0.6f); _cheerUntil = Time.unscaledTime + 2f; }

  void Update() {
    if (sensors == null) return;
    float wet = Wetness, decay = 1f - Mathf.Exp(-Time.deltaTime / 0.12f);
    foreach (var kv in _loops) {
      var l = kv.Value; var s = sensors.Of(kv.Key); if (s == null) continue;
      // sum |tau| from each joint's own forcerange (Matt's reach 250 N·m; the dogs' are all RaceSensors.ForceRange)
      float effort = 0f, torque = 0f; for (int i = 0; i < s.effort.Length; i++) { effort += s.effort[i]; torque += s.effort[i] * s.forceMax[i]; }
      effort /= s.effort.Length;
      float jointSpeed = s.power / Mathf.Max(1f, torque);   // ~ mean |qdot|
      l.whine.pitch = Mathf.Lerp(l.whine.pitch, 0.7f + 0.06f * Mathf.Min(jointSpeed, 12f), 0.2f);
      l.whine.volume = Mathf.Lerp(l.whine.volume, master * Mathf.Clamp01(0.03f + 0.35f * effort), 0.2f);
      float k = Mathf.Clamp01((l.slip - 0.15f) / 1.2f);
      l.scrape.volume = Mathf.Lerp(l.scrape.volume, master * 0.45f * k * (1f + 0.3f * wet), 0.25f);
      l.scrape.pitch = Mathf.Lerp(1f, 0.8f, wet) * (0.9f + 0.3f * k);
      float c = Cutoff(l.whine.transform.position);
      l.scrapeLp.cutoffFrequency = Mathf.Min(c, Mathf.Lerp(900f, 6000f, k) * Mathf.Lerp(1f, 0.45f, wet));
      l.whineLp.cutoffFrequency = c;
      l.slip -= l.slip * decay;
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
