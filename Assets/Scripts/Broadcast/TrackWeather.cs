using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Mujoco;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature 7, live-weather track conditions. Current weather at a real circuit comes from Open-Meteo (free,
/// no key) and changes the race through MuJoCo only:
///   rain  -> world geoms (floor, bank, obstacles) lose grip: friction 0.6 x (1 - min(0.3, 0.1 + 0.08 mm/h)), never
///            below 0.42, inside the 0.4-1.0 floor friction the policies were randomized over (randomize.py)
///   wind  -> a steady push on every base through ExternalForces: min(10, 0.3 v^2) N toward where the wind blows,
///            with slow gusts of +-30 %. Track "north" is MuJoCo +y.
/// Rain also turns on a particle curtain around the camera and dims the sun. Menu picks the circuit (or Off);
/// -weather "rain=2,wind=7,dir=270,temp=12,city=Test" sets conditions offline (headless tests), -noWeather disables.</summary>
public unsafe class TrackWeather : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public Material rainMaterial;

  public static readonly (string name, float lat, float lon)[] Cities = {
    ("Monaco", 43.7347f, 7.4206f), ("Le Mans", 47.9560f, 0.2240f), ("Suzuka", 34.8431f, 136.5410f), ("Silverstone", 52.0786f, -1.0169f),
    ("Interlagos", -23.7036f, -46.6997f), ("Spa", 50.4372f, 5.9714f), ("Seattle", 47.6062f, -122.3321f) };

  [Serializable] class Current { public float temperature_2m, precipitation, wind_speed_10m, wind_direction_10m; public int weather_code; }
  [Serializable] class Reply { public Current current; }

  public bool Active { get; private set; }
  public string City { get; private set; } = "";
  public float RainMm { get; private set; }
  public float WindMs { get; private set; }
  public float WindFromDeg { get; private set; }
  public float TempC { get; private set; }
  public int Code { get; private set; }
  public float Grip { get; private set; } = 1f;

  readonly List<(int geom, double mu)> _world = new List<(int, double)>();
  Text _widget; ParticleSystem _rain; Light _sun; float _sunBase = -1f;

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    int city = RaceConfig.fromMenu ? RaceConfig.weatherCity : -1; string offline = null;
    var args = Environment.GetCommandLineArgs();
    if (Array.IndexOf(args, "-raceTest") >= 0) city = -1;   // headless tests: only explicit -weather / -weatherCity
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-noWeather") city = -1;
      if (i + 1 < args.Length && args[i] == "-weather") offline = args[i + 1];
      if (i + 1 < args.Length && args[i] == "-weatherCity") city = int.Parse(args[i + 1]);
    }
    if (!BroadcastUi.Headless) _widget = BroadcastUi.Label(BroadcastUi.Root, "Weather", Vector2.one, Vector2.one, new Vector2(-16, -222), new Vector2(330, 64), 19, TextAnchor.UpperRight);
    if (offline != null) Apply(Parse(offline), "offline");
    else if (city >= 0 && city < Cities.Length) StartCoroutine(Fetch(city));
    else if (_widget != null) _widget.text = "";
  }

  static (string city, Current c) Parse(string spec) {
    var c = new Current(); string city = "Test";
    foreach (var kv in spec.Split(',')) {
      var p = kv.Split('='); if (p.Length != 2) continue;
      float v = 0; float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
      switch (p[0]) { case "rain": c.precipitation = v; break; case "wind": c.wind_speed_10m = v; break; case "dir": c.wind_direction_10m = v; break; case "temp": c.temperature_2m = v; break; case "city": city = p[1]; break; }
    }
    c.weather_code = c.precipitation > 0 ? 61 : 1; return (city, c);
  }

  IEnumerator Fetch(int i) {
    var (name, lat, lon) = Cities[i];
    if (_widget != null) _widget.text = $"weather: {name}...";
    string url = string.Format(CultureInfo.InvariantCulture,
        "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,precipitation,wind_speed_10m,wind_direction_10m,weather_code&wind_speed_unit=ms", lat, lon);
    using (var req = UnityWebRequest.Get(url)) {
      req.timeout = 8;
      yield return req.SendWebRequest();
      if (req.result != UnityWebRequest.Result.Success) { if (_widget != null) _widget.text = $"weather {name}: offline"; Debug.LogWarning("[Weather] " + req.error); yield break; }
      var reply = JsonUtility.FromJson<Reply>(req.downloadHandler.text);
      if (reply?.current != null) Apply((name, reply.current), "live");
    }
  }

  void Apply((string city, Current c) w, string source) {
    City = w.city; RainMm = Mathf.Max(0f, w.c.precipitation); WindMs = Mathf.Max(0f, w.c.wind_speed_10m); WindFromDeg = w.c.wind_direction_10m;
    TempC = w.c.temperature_2m; Code = w.c.weather_code; Active = true;
    Grip = RainMm > 0.05f ? Mathf.Max(0.7f, 1f - Mathf.Min(0.3f, 0.1f + 0.08f * RainMm)) : 1f;
    _frictionSet = false; ApplyFriction();
    Debug.Log($"[Weather] {source} {City}: {TempC:F1} C, rain {RainMm:F2} mm/h, wind {WindMs:F1} m/s from {WindFromDeg:F0} deg -> grip {Grip * 100:F0} %, wind force {WindForce():F1} N");
    BroadcastTicker.Post($"{BroadcastUi.Paint("LIVE WEATHER", BroadcastUi.Live)} {City}: {Describe()}", 8f);
    if (!BroadcastUi.Headless) Visuals();
  }

  bool _frictionSet;
  /// <summary>Scale the sliding friction of every world geom (body 0) from its compiled value. Waits for the model.</summary>
  void ApplyFriction() {
    if (_frictionSet || !MjScene.InstanceExists || MjScene.Instance.Model == null) return;
    var m = MjScene.Instance.Model;
    if (_world.Count == 0) for (int g = 0; g < (int)m->ngeom; g++) if (m->geom_bodyid[g] == 0) _world.Add((g, m->geom_friction[3 * g]));
    foreach (var (g, mu) in _world) m->geom_friction[3 * g] = mu * Grip;
    _frictionSet = true;
    Debug.Log($"[Weather] friction x{Grip:F2} on {_world.Count} world geoms");
  }

  float WindForce() => Mathf.Min(10f, 0.3f * WindMs * WindMs);
  static readonly string[] Arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
  string Describe() {
    string sky = Code >= 95 ? "storm" : Code >= 71 && Code <= 77 ? "snow" : Code >= 51 ? "rain" : Code >= 45 ? "fog" : Code >= 3 ? "cloudy" : "clear";
    string to = Arrows[Mathf.RoundToInt(Mathf.Repeat(WindFromDeg + 180f, 360f) / 45f) % 8];
    return $"{TempC:F0}°C {sky}{(RainMm > 0 ? $" {RainMm:F1} mm/h" : "")}  wind {WindMs:F1} m/s {to}  grip {Grip * 100:F0}%";
  }

  void FixedUpdate() {
    if (!Active || race == null) return;
    ApplyFriction();
    if (WindMs < 0.5f) return;
    // Wind blows toward (from + 180); compass bearing -> MuJoCo (x east, y north). Gusts from slow noise.
    float to = (WindFromDeg + 180f) * Mathf.Deg2Rad;
    var dir = new Vector3(Mathf.Sin(to), Mathf.Cos(to), 0f);
    foreach (var r in race.Live) {
      if (r.controller.Model == null) continue;
      float gust = 0.7f + 0.6f * Mathf.PerlinNoise(Time.time * 0.3f, r.lane * 3.1f);
      ExternalForces.Instance.SetSteady(r.controller.Model.BaseBody, r.finishTime < 0 ? dir * WindForce() * gust : Vector3.zero);
    }
  }

  void Update() { if (_widget != null && Active) _widget.text = $"{BroadcastUi.Paint("● LIVE", BroadcastUi.Live)} {City}\n{Describe()}"; }

  void Visuals() {
    if (_sun == null) foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) { _sun = l; break; }
    if (_sun != null) { if (_sunBase < 0) _sunBase = _sun.intensity; _sun.intensity = _sunBase * (Code >= 51 ? 0.55f : Code >= 3 ? 0.8f : 1f); }
    if (RainMm <= 0.05f || rainMaterial == null) return;
    var cam = race.followCam != null ? race.followCam.transform : Camera.main?.transform; if (cam == null) return;
    var go = new GameObject("Rain"); go.transform.SetParent(cam, false); go.transform.localPosition = new Vector3(0, 4f, 3f);
    _rain = go.AddComponent<ParticleSystem>(); _rain.Stop();
    var main = _rain.main; main.startLifetime = 1.2f; main.startSpeed = 9f; main.startSize = 0.025f; main.maxParticles = 3000;
    main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = new Color(0.75f, 0.82f, 0.95f, 0.5f);
    var em = _rain.emission; em.rateOverTime = Mathf.Clamp(300f * RainMm, 150f, 2500f);
    var sh = _rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(12f, 0.1f, 12f); sh.rotation = new Vector3(90f, 0f, 0f);
    var rend = go.GetComponent<ParticleSystemRenderer>(); rend.sharedMaterial = rainMaterial; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.04f; rend.lengthScale = 1f;
    _rain.Play();
  }
}

}
