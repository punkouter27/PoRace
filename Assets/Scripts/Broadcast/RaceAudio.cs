using System.Collections.Generic;
using Mujoco;
using UnityEngine;

namespace PoRace {

/// <summary>Feature 10 + H.2, gait and contact sound, all synthesised at start (no audio assets):
///   footstep   per foot landing (ContactImpulse), louder with impulse, timbre by surface: track thump, rail ping,
///              obstacle knock; a squelch layer on top when TrackWeather has made the track wet
///   impact     body, cube and robot hits crossfade three layers by impulse (tap / thud / slam, pitch falls as the hit
///              gets harder) plus a surface layer: rail clang, robot-on-robot clank, cube / obstacle knock
///   scrape     one looping 3D source per racer: volume and low-pass cutoff follow its fastest foot slip, darker and
///              louder on a wet track (Mobile: folded into the servo voice, one voice per racer)
///   servo      B6: one procedural 3D voice per racer (ServoSynth): a partial per limb whose pitch follows that limb's
///              mean |q̇| (read from mjData, never written) and level its torque, plus hiss with mechanical power
///   crowd      ambience bed, cheer swell on overtakes and the finish, countdown beeps; B7 stingers synthesised from
///              formant-filtered noise, three variants each with cooldowns: a gasp on a fall, cheers on an overtake
///              and a finish, and a roar that rises over the leader's last 10 m
/// B10 voice manager: one-shots share GfxTier.AudioVoices sources (8 Mobile / 24 PC); a new sound takes a free voice,
/// else steals the one with the lowest remaining priority (volume x proximity, fading as it plays), else is dropped.
/// Every 3D voice rolls off logarithmically and loses treble with distance (AudioLowPassFilter). Particles and camera
/// shake live in ImpactFx. Headless runs skip all of it.</summary>
public unsafe class RaceAudio : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public ContactImpulse contacts;
  public RaceDirector director;
  public TrackWeather weather;
  [Range(0f, 1f)] public float master = 0.8f;
  public float hearDistance = 40f;
  public float gaspCooldown = 3f, cheerCooldown = 2.5f, roarDistance = 10f;

  AudioClip _step, _stepMetal, _stepWood, _squelch, _tap, _thud, _slam, _clang, _clank, _knock, _scrape, _ones, _crowd, _roarClip, _beep, _go;
  AudioClip[] _gasps, _cheers;
  class Voice { public AudioSource s; public AudioLowPassFilter lp; public float start, until, score; }
  readonly List<Voice> _pool = new List<Voice>();
  class Loops { public AudioSource whine, scrape; public AudioLowPassFilter whineLp, scrapeLp; public ServoSynth synth; public float slip, f0; }
  readonly Dictionary<Racer, Loops> _loops = new Dictionary<Racer, Loops>();
  AudioSource _bed, _ui, _roar; int _lastBeep = -1, _lastGasp = -1, _lastCheer = -1; float _cheerUntil, _gaspAt = -100f, _cheerAt = -100f; bool _finishedAny;
  Transform _listener;
  const int Rate = 44100;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
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
    _ones = Make("ones", 1f, (t, i) => 1f);   // carrier for ServoSynth (it multiplies its signal in)
    _crowd = Make("crowd", 3f, (t, i) => N() * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.33f * t + 1f)) * 0.5f, lowpass: 0.08f);
    _beep = Make("beep", 0.15f, (t, i) => S(880f, t) * Mathf.Min(1f, (0.15f - t) * 40f) * 0.5f);
    _go = Make("go", 0.45f, (t, i) => S(1320f, t) * Mathf.Min(1f, (0.45f - t) * 20f) * 0.5f);
    // B7 crowd stingers: band-passed noise through vowel formants, many voices' level wander, an envelope per kind.
    _gasps = new AudioClip[3]; _cheers = new AudioClip[3];
    float[] gaspLen = { 0.8f, 0.95f, 0.7f }, gaspShift = { 0.9f, 1f, 1.12f }, cheerLen = { 2f, 2.4f, 1.8f }, cheerRate = { 5f, 6.5f, 4f }, whistle = { 0f, 0.12f, 0.06f };
    for (int v = 0; v < 3; v++) {
      float gl = gaspLen[v], cl = cheerLen[v], cr = cheerRate[v];
      _gasps[v] = Crowd("gasp" + v, gl, 11 + v, new[] { 700f * gaspShift[v], 1150f * gaspShift[v], 2500f * gaspShift[v] },
        t => t < 0.05f ? t / 0.05f : Mathf.Exp(-(t - 0.05f) * 4.5f), 0f);
      _cheers[v] = Crowd("cheer" + v, cl, 21 + v, new[] { 650f * gaspShift[v], 1750f * gaspShift[v], 2600f * gaspShift[v] },
        t => Mathf.Pow(Mathf.Sin(Mathf.PI * t / cl), 0.7f) * (0.75f + 0.25f * Mathf.Sin(2 * Mathf.PI * cr * t)), whistle[v]);
    }
    _roarClip = Crowd("roar", 4f, 31, new[] { 450f, 950f, 1900f }, t => 1f, 0f);

    for (int i = 0; i < GfxTier.AudioVoices; i++) {
      var go = new GameObject("sfx" + i); go.transform.SetParent(transform);
      var s = Spatial(go.AddComponent<AudioSource>(), 1.5f); s.dopplerLevel = 0f;
      _pool.Add(new Voice { s = s, lp = go.AddComponent<AudioLowPassFilter>() });
    }
    _ui = gameObject.AddComponent<AudioSource>(); _ui.spatialBlend = 0f; _ui.playOnAwake = false; _ui.priority = 16;
    _bed = gameObject.AddComponent<AudioSource>(); _bed.clip = _crowd; _bed.loop = true; _bed.volume = 0.12f * master; _bed.spatialBlend = 0f; _bed.priority = 32; _bed.Play();
    _roar = gameObject.AddComponent<AudioSource>(); _roar.clip = _roarClip; _roar.loop = true; _roar.volume = 0f; _roar.spatialBlend = 0f; _roar.priority = 32; _roar.Play();
    bool split = !GfxTier.Mobile;   // PC: scrape on its own voice; Mobile: inside the servo voice
    foreach (var r in race.Live) {
      var b = r.Body.BaseTransform; if (b == null) continue;
      var l = new Loops();
      // Each loop on its own child: one AudioLowPassFilter per GameObject filters every source on it. ServoSynth sits
      // between the source and the filter (component order is filter order).
      var wg = new GameObject("servo"); wg.transform.SetParent(b, false);
      l.whine = Loop(wg.AddComponent<AudioSource>(), _ones, 0.8f); l.whine.dopplerLevel = 0f; l.whine.spread = 40f; l.whine.priority = 64;
      l.synth = wg.AddComponent<ServoSynth>(); l.synth.partials = GfxTier.ServoPartials;
      l.whineLp = wg.AddComponent<AudioLowPassFilter>();
      if (split) {
        var sg = new GameObject("scrape"); sg.transform.SetParent(b, false);
        l.scrape = Loop(sg.AddComponent<AudioSource>(), _scrape, 1f); l.scrape.priority = 80; l.scrapeLp = sg.AddComponent<AudioLowPassFilter>();
      }
      _loops[r] = l;
    }

    if (contacts != null) { contacts.Impact += OnImpact; contacts.Slip += (r, f, slip, surf, pos, n) => { if (_loops.TryGetValue(r, out var l)) l.slip = Mathf.Max(l.slip, slip); }; }
    race.Overtook += (a, b) => Cheer(0.35f, false);
    race.Finished += r => { Cheer(r.place == 1 ? 0.9f : 0.4f, r.place == 1); _finishedAny = true; };
    race.Fell += Gasp;
    race.Restarted += () => { _finishedAny = false; _roar.volume = 0f; };
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

  /// <summary>B7 crowd voice: white noise through parallel band-passes at vowel formants, a slow random level wander
  /// (many people, never in step), an envelope env(t) and an optional gliding whistle; normalised to 0.9 peak.</summary>
  static AudioClip Crowd(string name, float seconds, int seed, float[] formants, System.Func<float, float> env, float whistle) {
    int n = Mathf.CeilToInt(seconds * Rate), nf = formants.Length; var data = new float[n]; var rng = new System.Random(seed);
    var low = new float[nf]; var band = new float[nf]; var F = new float[nf];
    for (int k = 0; k < nf; k++) F[k] = 2f * Mathf.Sin(Mathf.PI * formants[k] / Rate);
    float wander = 0f, peak = 1e-4f, ph = 0f;
    for (int i = 0; i < n; i++) {
      float t = i / (float)Rate, x = (float)(rng.NextDouble() * 2 - 1), y = 0f;
      wander += ((float)(rng.NextDouble() * 2 - 1) - wander) * 0.0004f;
      for (int k = 0; k < nf; k++) { low[k] += F[k] * band[k]; float hi = x - low[k] - 0.5f * band[k]; band[k] += F[k] * hi; y += band[k] / (k + 1); }
      if (whistle > 0f) { ph += (1900f + 500f * Mathf.Sin(2 * Mathf.PI * 0.6f * t)) / Rate; y += whistle * Mathf.Sin(2 * Mathf.PI * ph) * Mathf.Clamp01(Mathf.Sin(Mathf.PI * t / seconds) * 2f - 0.4f); }
      data[i] = y * env(t) * (1f + 4f * wander); peak = Mathf.Max(peak, Mathf.Abs(data[i]));
    }
    for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
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

  /// <summary>B10: a free voice, else steal the lowest remaining priority (score x fraction left), else drop.</summary>
  void Play(AudioClip clip, Vector3 pos, float volume, float pitch) {
    float now = Time.unscaledTime;
    float prox = _listener == null ? 1f : 1f - Mathf.Clamp01(Vector3.Distance(_listener.position, pos) / hearDistance);
    float score = volume * (0.25f + 0.75f * prox);
    Voice pick = null; float worst = float.MaxValue;
    for (int i = 0; i < _pool.Count; i++) {
      var v = _pool[i];
      if (now >= v.until) { pick = v; worst = -1f; break; }
      float left = v.score * (v.until - now) / Mathf.Max(0.01f, v.until - v.start);
      if (left < worst) { worst = left; pick = v; }
    }
    if (pick == null || (worst >= 0f && score <= worst)) return;
    if (worst >= 0f) pick.s.Stop();
    pick.s.transform.position = pos; pick.s.pitch = pitch; pick.lp.cutoffFrequency = Cutoff(pos);
    pick.s.priority = 255 - (int)(200f * Mathf.Clamp01(score));
    pick.s.PlayOneShot(clip, volume * master);
    pick.start = now; pick.until = now + clip.length / Mathf.Max(0.1f, pitch); pick.score = score;
  }

  int Variant(ref int last) { int v = Random.Range(0, 3); if (v == last) v = (v + 1) % 3; last = v; return v; }

  /// <summary>Bed swell plus a cheer stinger (cooldown, unless forced: the winner's finish).</summary>
  void Cheer(float level, bool force) {
    _cheerUntil = Time.unscaledTime + 2f;
    if (!force && Time.unscaledTime - _cheerAt < cheerCooldown) return;
    _cheerAt = Time.unscaledTime;
    _ui.PlayOneShot(_cheers[Variant(ref _lastCheer)], level * master * 0.6f);
  }

  void Gasp(Racer r) {
    if (race.Phase != RacePhase.Racing || Time.unscaledTime - _gaspAt < gaspCooldown) return;
    _gaspAt = Time.unscaledTime;
    bool subject = director != null && director.Subject == r;
    _ui.PlayOneShot(_gasps[Variant(ref _lastGasp)], (subject ? 0.6f : 0.4f) * master);
  }

  void Update() {
    if (sensors == null) return;
    float wet = Wetness, decay = 1f - Mathf.Exp(-Time.deltaTime / 0.12f);
    var d = MjScene.InstanceExists ? MjScene.Instance.Data : null;
    foreach (var kv in _loops) {
      var l = kv.Value; var s = sensors.Of(kv.Key); if (s == null) continue;
      if (l.f0 <= 0f) l.f0 = Mathf.Clamp(330f * Mathf.Sqrt(15f / Mathf.Max(1f, s.mass)), 150f, 400f);   // a 15 kg dog ~330 Hz, a heavier body lower
      // B6 per limb: mean |qdot| (rad/s) -> partial pitch, mean effort and joint speed -> partial level.
      var limbs = StrainMonitor.Limbs(s.effort.Length); int np = l.synth.partials;
      float effortAll = 0f, qdAll = 0f;
      for (int li = 0; li < limbs.Length; li++) {
        int from = limbs[li].from, to = Mathf.Min(limbs[li].to, s.effort.Length), c = Mathf.Max(1, to - from);
        float qd = 0f, eff = 0f;
        for (int i = from; i < to; i++) { eff += s.effort[i]; if (d != null) qd += Mathf.Abs((float)d->qvel[s.dof[i]]); }
        qd /= c; eff /= c; effortAll += eff; qdAll += qd;
        if (np > 1 && li < np) {
          l.synth.freq[li] = l.f0 * (0.8f + 0.06f * Mathf.Min(qd, 15f)) * (1f + 0.03f * li);   // slight detune per limb
          l.synth.gain[li] = Mathf.Clamp01(0.08f + 0.9f * eff) * (0.25f + 0.75f * Mathf.Clamp01(qd / 6f)) / np;
        }
      }
      effortAll /= Mathf.Max(1, limbs.Length); qdAll /= Mathf.Max(1, limbs.Length);
      if (np == 1) {
        l.synth.freq[0] = l.f0 * (0.8f + 0.06f * Mathf.Min(qdAll, 15f));
        l.synth.gain[0] = Mathf.Clamp01(0.08f + 0.9f * effortAll) * (0.25f + 0.75f * Mathf.Clamp01(qdAll / 6f));
      }
      l.synth.hiss = 0.35f * Mathf.Clamp01(s.power / Mathf.Max(1f, s.mass) / 25f);
      l.synth.hissFreq = 2.5f * l.synth.freq[0];
      l.whine.volume = Mathf.Lerp(l.whine.volume, master * 0.55f, 0.2f);
      float k = Mathf.Clamp01((l.slip - 0.15f) / 1.2f);
      float c0 = Cutoff(l.whine.transform.position);
      if (l.scrape != null) {
        l.scrape.volume = Mathf.Lerp(l.scrape.volume, master * 0.45f * k * (1f + 0.3f * wet), 0.25f);
        l.scrape.pitch = Mathf.Lerp(1f, 0.8f, wet) * (0.9f + 0.3f * k);
        l.scrapeLp.cutoffFrequency = Mathf.Min(c0, Mathf.Lerp(900f, 6000f, k) * Mathf.Lerp(1f, 0.45f, wet));
      } else {
        l.synth.scrape = 0.45f * k * (1f + 0.3f * wet); l.synth.scrapeTone = Mathf.Lerp(0.05f, 0.35f, k) * Mathf.Lerp(1f, 0.45f, wet);
      }
      l.whineLp.cutoffFrequency = c0;
      l.slip -= l.slip * decay;
    }
    _bed.volume = Mathf.Lerp(_bed.volume, master * (Time.unscaledTime < _cheerUntil ? 0.22f : 0.12f), 0.05f);
    // B7 roar over the leader's last roarDistance metres; half as loud for the chasers once someone has finished.
    float roar = 0f;
    if (race.Phase == RacePhase.Racing && race.Live.Count > 0) {
      var lead = race.Leader();
      float left = race.TotalLength - lead.distance;
      if (lead.finishTime < 0 && left < roarDistance) roar = 0.45f * Mathf.Clamp01(1f - left / roarDistance) * (_finishedAny ? 0.5f : 1f);
    }
    _roar.volume = Mathf.MoveTowards(_roar.volume, master * roar, Time.unscaledDeltaTime * (roar > 0f ? 0.6f : 0.25f));
    if (race.Phase == RacePhase.Countdown) {
      int sec = Mathf.CeilToInt(race.CountdownLeft);
      if (sec != _lastBeep && sec <= 3 && sec > 0) _ui.PlayOneShot(_beep, 0.6f * master);
      _lastBeep = sec;
    } else if (_lastBeep > 0 && race.Phase == RacePhase.Racing) { _ui.PlayOneShot(_go, 0.7f * master); _lastBeep = 0; }
  }
}

}
