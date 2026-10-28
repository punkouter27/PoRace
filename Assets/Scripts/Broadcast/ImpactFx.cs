using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>H.1 / H.5, what contacts look like, driven by ContactImpulse (k = impulse / 8 N·s, clamped):
///   dust    a puff at feet that land hard and a burst on body, cube and robot hits; count, speed and size scale with k,
///           the cone follows the contact normal (banks, obstacle tops)
///   sparks  short stretched streaks when metal is involved: rails, robot against robot, cubes
///   ring    an expanding dust ring on the ground for hits over rippleImpulse (radial stress ripple)
///   scuffs  dark marks left where feet slide and bodies or cubes land, laid on the contact plane; a fixed pool
///           (GfxTier.Scuffs), oldest recycled, faded out over their last 30 %
///   shake   camera trauma for hits on or near the director's subject
///   dust colour (B8) by surface: the map's track dust (tan dirt on the steeplechase, light concrete on the flat maps,
///           cool grey concrete on the banked oval), pale sawdust off obstacles, grey grit robot on robot; none off
///           metal (sparks only) and none on a wet track (TrackWeather)
///   footfall rings (B3) a faint ring in the racer's colour at each foot strike (RaceSensors.FootStrike), wider the
///           faster the racer runs, laid on the ground slope: the gait rhythm at a glance (GfxTier.FootRings alive)
///   confetti (B4) one pooled burst when a racer finishes: the winner's is the big one (GfxTier.Confetti), later
///           finishers a fifth of it, in the racer's colour plus gold and white
/// Bursts are capped per frame (GfxTier.BurstsPerFrame) and nothing is emitted beyond cullDistance from the camera.
/// Headless runs skip all of it.</summary>
public class ImpactFx : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ContactImpulse contacts;
  public RaceDirector director;
  public RaceSensors sensors;
  public TrackWeather weather;
  public Material particleMaterial;
  public Material decalMaterial;
  public float rippleImpulse = 3f;
  public float scuffSlip = 0.6f;
  public float scuffLife = 25f;
  public float cullDistance = 30f;

  ParticleSystem _dust, _sparks, _rings, _footRings, _confetti;
  Color _trackDust;
  int _frame, _bursts, _puffs;
  class Scuff { public Transform t; public MeshRenderer r; public float born = -1f, shown = -1f; }
  readonly List<Scuff> _scuffs = new List<Scuff>(); int _scuffNext;
  readonly Dictionary<(Racer, int), float> _lastScuff = new Dictionary<(Racer, int), float>();
  MaterialPropertyBlock _mpb;
  Camera _cam;
  static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseMap");
  static readonly Color ScuffColor = new Color(0.12f, 0.1f, 0.08f, 0.55f), SparkColor = new Color(1f, 0.92f, 0.6f, 1f);

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race)) || !BroadcastUi.Wired(this, contacts, nameof(contacts))) return;
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    if (weather == null) weather = FindAnyObjectByType<TrackWeather>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    _cam = race.ViewCamera;
    _mpb = new MaterialPropertyBlock();
    _trackDust = TrackDust(race.map != null ? race.map.terrain : "");
    _dust = MakeDust(); _sparks = MakeSparks(); _rings = MakeRings(); _footRings = MakeFootRings(); _confetti = MakeConfetti();
    MakeScuffPool();
    contacts.Impact += OnImpact;
    contacts.Slip += OnSlip;
    if (sensors != null) sensors.FootStrike += OnFootStrike;
    race.Finished += Confetti;
    race.Restarted += () => { foreach (var s in _scuffs) { s.born = -1f; s.r.enabled = false; } _confetti.Clear(); };
  }

  bool Near(Vector3 p) => _cam == null || (_cam.transform.position - p).sqrMagnitude < cullDistance * cullDistance;

  // ---- B8 surface dust palette ------------------------------------------------------------------------------------

  static Color TrackDust(string terrain) {
    switch (terrain) {
      case "obstacles": return new Color(0.7f, 0.56f, 0.38f, 0.6f);     // tan dirt
      case "banked": return new Color(0.6f, 0.62f, 0.66f, 0.5f);        // cool grey concrete
      default: return new Color(0.74f, 0.7f, 0.62f, 0.55f);             // light concrete
    }
  }
  static readonly Color Sawdust = new Color(0.82f, 0.7f, 0.5f, 0.55f), Grit = new Color(0.45f, 0.46f, 0.48f, 0.4f);

  bool Wet => weather != null && weather.Active && weather.Grip < 0.97f;

  /// <summary>Dust colour off a struck surface; alpha 0 = no dust (metal, or a wet track).</summary>
  Color Dust(Surface s) {
    switch (s) {
      case Surface.Track: return Wet ? Color.clear : _trackDust;
      case Surface.Wood: return Wet ? Color.clear : Sawdust;
      case Surface.Metal: return Color.clear;
      default: return Grit;
    }
  }

  void OnImpact(ContactImpulse.Hit h) {
    if (Time.frameCount != _frame) { _frame = Time.frameCount; _bursts = 0; _puffs = 0; }
    if (!Near(h.pos)) return;
    if (h.kind == ContactKind.Foot) {
      float kf = Mathf.Clamp01(h.impulse / 3f);
      var fc = Dust(h.surface);
      if (h.impulse > 2f && fc.a > 0f && _puffs++ < 3) Emit(_dust, h.pos, h.normal, 1 + (int)(2 * kf), 0.4f + 0.5f * kf, 0.5f + 0.3f * kf, fc);
      if (h.surface == Surface.Track && h.impulse > 2.5f) AddScuff(h.pos, h.normal, 0.12f, 0.08f);
      return;
    }
    float k = Mathf.Clamp01(h.impulse / 8f);
    if (director != null) {
      bool subject = director.Subject != null && (h.racer == director.Subject || h.other == director.Subject);
      float near = _cam == null ? 1f : Mathf.Clamp01(1.5f - Vector3.Distance(_cam.transform.position, h.pos) / 6f);
      director.AddTrauma(k * (subject ? 0.55f : 0.3f * near));
    }
    if (_bursts++ >= GfxTier.BurstsPerFrame) return;
    var dc = Dust(h.surface);
    if (dc.a > 0f) Emit(_dust, h.pos, h.normal, (h.surface == Surface.Robot ? 4 : 8) + (int)(40 * k), 0.6f + 1.4f * k, 1f + 0.8f * k, dc);
    bool metal = h.surface == Surface.Metal || h.kind == ContactKind.RobotRobot || h.kind == ContactKind.Cube;
    if (metal && h.impulse > 1f) Emit(_sparks, h.pos, h.normal, 4 + (int)(24 * k), 1f + 1.5f * k, 1f, SparkColor);
    if (h.impulse >= rippleImpulse && (h.surface == Surface.Track || h.surface == Surface.Wood)) Ring(h.pos, h.normal, 0.7f + 1.3f * k);
    if (h.surface == Surface.Track && h.impulse > 1.5f) AddScuff(h.pos, h.normal, 0.2f + 0.35f * k, 0.15f + 0.25f * k);
  }

  void OnSlip(Racer r, int foot, float slip, Surface surface, Vector3 pos, Vector3 normal) {
    if (slip < scuffSlip || surface != Surface.Track || !Near(pos)) return;
    var key = (r, foot);
    if (_lastScuff.TryGetValue(key, out float t) && Time.time - t < 0.25f) return;
    _lastScuff[key] = Time.time;
    float k = Mathf.Clamp01((slip - scuffSlip) / 1.2f);
    AddScuff(pos, normal, 0.12f + 0.25f * k, 0.05f + 0.04f * k);
    var c = Dust(surface);
    if (slip > 0.9f && c.a > 0f && _puffs++ < 4) Emit(_dust, pos, normal, 1 + (int)(2 * k), 0.4f + 0.4f * k, 0.6f, c);
  }

  // ---- B3 footfall rings, B4 confetti -----------------------------------------------------------------------------

  float Ground(Vector3 p) => race.followCam != null && race.followCam.groundHeight != null ? race.followCam.groundHeight(p) : p.y;

  /// <summary>RaceSensors.FootStrike (physics step, main thread): one ring at the foot, on the ground's slope.</summary>
  void OnFootStrike(Racer r, int foot, Vector3 pos, float speed) {
    if (!Near(pos)) return;
    float h = Ground(pos);
    float hx = Ground(pos + Vector3.right * 0.2f) - Ground(pos - Vector3.right * 0.2f), hz = Ground(pos + Vector3.forward * 0.2f) - Ground(pos - Vector3.forward * 0.2f);
    var n = new Vector3(-hx / 0.4f, 1f, -hz / 0.4f).normalized;
    float k = Mathf.Clamp01(speed / 2.5f);
    var c = r.color; c.a = 0.35f + 0.25f * k;
    _footRings.Emit(new ParticleSystem.EmitParams { position = new Vector3(pos.x, h + 0.006f, pos.z), rotation3D = Quaternion.FromToRotation(Vector3.back, n).eulerAngles,
      startSize = 0.22f + 0.28f * k, startColor = c, applyShapeToPosition = false }, 1);
  }

  void Confetti(Racer r) {
    var b = r.Body.BaseTransform; if (b == null || !Near(b.position)) return;
    var g = new Gradient();   // once per finish
    g.SetKeys(new[] { new GradientColorKey(r.color, 0f), new GradientColorKey(new Color(1f, 0.82f, 0.2f), 0.45f), new GradientColorKey(Color.white, 0.7f), new GradientColorKey(r.color, 1f) },
      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
    var main = _confetti.main; main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    bool first = r.place == 1; main.startSpeedMultiplier = first ? 1f : 0.7f;
    _confetti.Emit(new ParticleSystem.EmitParams { position = b.position + Vector3.up * 0.5f, applyShapeToPosition = true }, first ? GfxTier.Confetti : GfxTier.Confetti / 5);
  }

  // ---- particles --------------------------------------------------------------------------------------------------

  static Quaternion Up(Vector3 n) => Quaternion.FromToRotation(Vector3.up, n.sqrMagnitude > 0.01f ? n.normalized : Vector3.up);

  /// <summary>Emit with the shape cone along the contact normal and speed / size scaled for this burst only.</summary>
  static void Emit(ParticleSystem ps, Vector3 pos, Vector3 normal, int count, float speed, float size, Color color) {
    if (ps == null || count <= 0) return;
    var main = ps.main; main.startSpeedMultiplier = speed; main.startSizeMultiplier = size;
    var sh = ps.shape; sh.rotation = (Up(normal) * Quaternion.Euler(-90f, 0f, 0f)).eulerAngles;
    ps.Emit(new ParticleSystem.EmitParams { position = pos + normal * 0.02f, startColor = color, applyShapeToPosition = true }, count);
  }

  void Ring(Vector3 pos, Vector3 normal, float size) {
    var rot = Quaternion.FromToRotation(Vector3.back, normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    _rings.Emit(new ParticleSystem.EmitParams { position = pos + normal * 0.01f, rotation3D = rot.eulerAngles, startSize = size, applyShapeToPosition = false }, 1);
  }

  ParticleSystem NewSystem(string name, Material mat) {
    var go = new GameObject(name); go.transform.SetParent(transform, false);
    var ps = go.AddComponent<ParticleSystem>(); ps.Stop();
    var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = GfxTier.MaxParticles;
    var em = ps.emission; em.rateOverTime = 0f;
    var rend = go.GetComponent<ParticleSystemRenderer>(); rend.sharedMaterial = mat; rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
    return ps;
  }

  static Gradient Fade(Color a, Color b) {
    var g = new Gradient();
    g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
    return g;
  }

  ParticleSystem MakeDust() {
    var ps = NewSystem("Dust", particleMaterial);
    var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f); main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
    main.startColor = new Color(0.72f, 0.66f, 0.56f, 0.55f); main.gravityModifier = -0.02f;
    var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 60f; sh.radius = 0.05f;
    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white);
    var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
    var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 2.5f;
    ps.Play(); return ps;
  }

  ParticleSystem MakeSparks() {
    var mat = new Material(particleMaterial) { name = "Sparks" };
    var ps = NewSystem("Sparks", mat);
    var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f); main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
    main.startColor = new Color(1f, 0.92f, 0.6f, 1f); main.gravityModifier = 1f;
    var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = 0.02f;
    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.35f, 0.05f));
    var rend = ps.GetComponent<ParticleSystemRenderer>(); rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.06f; rend.lengthScale = 1.5f;
    ps.Play(); return ps;
  }

  ParticleSystem MakeRings() {
    var mat = new Material(decalMaterial) { name = "Ring" }; mat.SetTexture(BaseMap, RingTexture()); mat.SetColor(BaseColor, new Color(0.85f, 0.8f, 0.7f, 0.85f));
    var ps = NewSystem("Rings", mat);
    var main = ps.main; main.startLifetime = 0.55f; main.startSpeed = 0f; main.startRotation3D = true; main.maxParticles = 16;
    var sh = ps.shape; sh.enabled = false;
    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white);
    var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.15f, 0f, 3f), new Keyframe(1f, 1f, 0.3f, 0f)));
    var rend = ps.GetComponent<ParticleSystemRenderer>(); rend.renderMode = ParticleSystemRenderMode.Mesh; rend.mesh = Quad(); rend.alignment = ParticleSystemRenderSpace.World;
    ps.Play(); return ps;
  }

  ParticleSystem MakeFootRings() {
    var mat = new Material(decalMaterial) { name = "FootRing" }; mat.SetTexture(BaseMap, RingTexture()); mat.SetColor(BaseColor, Color.white);
    var ps = NewSystem("FootRings", mat);
    var main = ps.main; main.startLifetime = 0.6f; main.startSpeed = 0f; main.startRotation3D = true; main.maxParticles = GfxTier.FootRings;
    var sh = ps.shape; sh.enabled = false;
    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, Color.white);
    var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f, 0f, 2f), new Keyframe(1f, 1f, 0.2f, 0f)));
    var rend = ps.GetComponent<ParticleSystemRenderer>(); rend.renderMode = ParticleSystemRenderMode.Mesh; rend.mesh = Quad(); rend.alignment = ParticleSystemRenderSpace.World;
    ps.Play(); return ps;
  }

  /// <summary>Two-sided paper squares (decal shader, Cull Off) tumbling in 3D as they drift down.</summary>
  ParticleSystem MakeConfetti() {
    var mat = new Material(decalMaterial) { name = "Confetti" }; mat.SetTexture(BaseMap, Texture2D.whiteTexture); mat.SetColor(BaseColor, Color.white);
    var ps = NewSystem("Confetti", mat);
    var main = ps.main; main.maxParticles = GfxTier.Confetti * 3 / 2;
    main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f); main.startRotation3D = true;
    main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
    main.gravityModifier = 0.25f;
    var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.15f; sh.rotation = new Vector3(-90f, 0f, 0f);
    var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
    rot.x = new ParticleSystem.MinMaxCurve(-9f, 9f); rot.y = new ParticleSystem.MinMaxCurve(-9f, 9f); rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
    var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.6f;
    var col = ps.colorOverLifetime; col.enabled = true;
    var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
    col.color = g;
    var rend = ps.GetComponent<ParticleSystemRenderer>(); rend.renderMode = ParticleSystemRenderMode.Mesh; rend.mesh = Quad(); rend.alignment = ParticleSystemRenderSpace.World;
    ps.Play(); return ps;
  }

  // ---- scuffs -----------------------------------------------------------------------------------------------------

  void MakeScuffPool() {
    var mat = new Material(decalMaterial) { name = "Scuff" }; mat.SetTexture(BaseMap, ScuffTexture()); mat.SetColor(BaseColor, ScuffColor);
    var root = new GameObject("Scuffs").transform; root.SetParent(transform, false);
    var mesh = Quad();
    for (int i = 0; i < GfxTier.Scuffs; i++) {
      var go = new GameObject("scuff" + i); go.transform.SetParent(root, false);
      go.AddComponent<MeshFilter>().sharedMesh = mesh;
      var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
      _scuffs.Add(new Scuff { t = go.transform, r = r });
    }
  }

  void AddScuff(Vector3 pos, Vector3 normal, float length, float width) {
    if (_scuffs.Count == 0) return;
    var s = _scuffs[_scuffNext]; _scuffNext = (_scuffNext + 1) % _scuffs.Count;
    var n = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
    s.t.SetPositionAndRotation(pos + n * 0.004f, Quaternion.AngleAxis(Random.Range(0f, 360f), n) * Quaternion.FromToRotation(Vector3.back, n));
    s.t.localScale = new Vector3(length, width, 1f);
    s.born = Time.time; s.shown = -1f; s.r.enabled = true;
  }

  void Update() {
    float now = Time.time;
    foreach (var s in _scuffs) {
      if (s.born < 0f) continue;
      float age = (now - s.born) / scuffLife;
      if (age >= 1f) { s.born = -1f; s.r.enabled = false; continue; }
      float a = age < 0.7f ? 1f : 1f - (age - 0.7f) / 0.3f;
      if (Mathf.Abs(a - s.shown) < 0.03f) continue;
      s.shown = a; var c = ScuffColor; c.a *= a; _mpb.SetColor(BaseColor, c); s.r.SetPropertyBlock(_mpb);
    }
  }

  // ---- generated meshes and textures (no texture assets) -----------------------------------------------------------

  static Mesh _quad;
  /// <summary>Unit quad in the XY plane facing -Z, white vertex colours (the decal shader multiplies them in).</summary>
  public static Mesh Quad() {
    if (_quad != null) return _quad;
    _quad = new Mesh { name = "DecalQuad" };
    _quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
    _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
    _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
    _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
    _quad.RecalculateNormals(); _quad.RecalculateBounds();
    return _quad;
  }

  /// <summary>White texture with alpha(u, v), u and v in -1..1 (also SpeedLines' streaks).</summary>
  internal static Texture2D Tex(int w, int h, System.Func<float, float, float> alpha, string name) {
    var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
    var px = new Color32[w * h];
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) {
      float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
      px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(alpha(u, v))));
    }
    t.SetPixels32(px); t.Apply(true, true); return t;
  }

  /// <summary>Soft radial falloff (blob shadows): alpha 1 at the centre to 0 at the edge.</summary>
  public static Texture2D Radial(int size, float hardness) =>
    Tex(size, size, (u, v) => { float r = Mathf.Sqrt(u * u + v * v); return 1f - Mathf.SmoothStep(hardness, 1f, r); }, "Radial");

  static Texture2D RingTexture() =>
    Tex(128, 128, (u, v) => { float r = Mathf.Sqrt(u * u + v * v); return Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.09f, 2f)) + 0.15f * (1f - Mathf.SmoothStep(0.2f, 0.8f, r)); }, "Ring");

  static Texture2D ScuffTexture() {
    var rng = new System.Random(3);
    var streak = new float[64]; for (int i = 0; i < streak.Length; i++) streak[i] = 0.4f + 0.6f * (float)rng.NextDouble();
    return Tex(128, 64, (u, v) => {
      float edge = (1f - Mathf.SmoothStep(0.6f, 1f, Mathf.Abs(u))) * (1f - Mathf.SmoothStep(0.3f, 1f, Mathf.Abs(v)));
      return edge * streak[Mathf.Clamp((int)((v * 0.5f + 0.5f) * 63f), 0, 63)];
    }, "Scuff");
  }
}

}
