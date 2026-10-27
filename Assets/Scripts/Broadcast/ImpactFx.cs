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
/// Bursts are capped per frame (GfxTier.BurstsPerFrame) and nothing is emitted beyond cullDistance from the camera.
/// Headless runs skip all of it.</summary>
public class ImpactFx : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ContactImpulse contacts;
  public RaceDirector director;
  public Material particleMaterial;
  public Material decalMaterial;
  public float rippleImpulse = 3f;
  public float scuffSlip = 0.35f;
  public float scuffLife = 25f;
  public float cullDistance = 30f;

  ParticleSystem _dust, _sparks, _rings;
  int _frame, _bursts, _puffs;
  class Scuff { public Transform t; public MeshRenderer r; public float born = -1f, shown = -1f; }
  readonly List<Scuff> _scuffs = new List<Scuff>(); int _scuffNext;
  readonly Dictionary<(Racer, int), float> _lastScuff = new Dictionary<(Racer, int), float>();
  MaterialPropertyBlock _mpb;
  Camera _cam;
  static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseMap");
  static readonly Color ScuffColor = new Color(0.12f, 0.1f, 0.08f, 0.55f);

  void Start() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (contacts == null) contacts = FindAnyObjectByType<ContactImpulse>();
    if (director == null) director = FindAnyObjectByType<RaceDirector>();
    if (BroadcastUi.Headless || contacts == null) { enabled = false; return; }
    _cam = race.followCam != null ? race.followCam.GetComponent<Camera>() : Camera.main;
    _mpb = new MaterialPropertyBlock();
    _dust = MakeDust(); _sparks = MakeSparks(); _rings = MakeRings();
    MakeScuffPool();
    contacts.Impact += OnImpact;
    contacts.Slip += OnSlip;
    race.Restarted += () => { foreach (var s in _scuffs) { s.born = -1f; s.r.enabled = false; } };
  }

  bool Near(Vector3 p) => _cam == null || (_cam.transform.position - p).sqrMagnitude < cullDistance * cullDistance;

  void OnImpact(ContactImpulse.Hit h) {
    if (Time.frameCount != _frame) { _frame = Time.frameCount; _bursts = 0; _puffs = 0; }
    if (!Near(h.pos)) return;
    if (h.kind == ContactKind.Foot) {
      float kf = Mathf.Clamp01(h.impulse / 3f);
      if (h.impulse > 0.5f && _puffs++ < 8) Emit(_dust, h.pos, h.normal, 1 + (int)(6 * kf), 0.6f + 0.8f * kf, 0.8f + 0.5f * kf);
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
    Emit(_dust, h.pos, h.normal, 8 + (int)(40 * k), 0.6f + 1.4f * k, 1f + 0.8f * k);
    bool metal = h.surface == Surface.Metal || h.kind == ContactKind.RobotRobot || h.kind == ContactKind.Cube;
    if (metal && h.impulse > 1f) Emit(_sparks, h.pos, h.normal, 4 + (int)(24 * k), 1f + 1.5f * k, 1f);
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
    if (_puffs++ < 8) Emit(_dust, pos, normal, 2 + (int)(5 * k), 0.4f + 0.6f * k, 0.8f);
  }

  // ---- particles --------------------------------------------------------------------------------------------------

  static Quaternion Up(Vector3 n) => Quaternion.FromToRotation(Vector3.up, n.sqrMagnitude > 0.01f ? n.normalized : Vector3.up);

  /// <summary>Emit with the shape cone along the contact normal and speed / size scaled for this burst only.</summary>
  static void Emit(ParticleSystem ps, Vector3 pos, Vector3 normal, int count, float speed, float size) {
    if (ps == null || count <= 0) return;
    var main = ps.main; main.startSpeedMultiplier = speed; main.startSizeMultiplier = size;
    var sh = ps.shape; sh.rotation = (Up(normal) * Quaternion.Euler(-90f, 0f, 0f)).eulerAngles;
    ps.Emit(new ParticleSystem.EmitParams { position = pos + normal * 0.02f, applyShapeToPosition = true }, count);
  }

  void Ring(Vector3 pos, Vector3 normal, float size) {
    var rot = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
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
    s.t.SetPositionAndRotation(pos + n * 0.004f, Quaternion.AngleAxis(Random.Range(0f, 360f), n) * Quaternion.LookRotation(-n));
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

  static Texture2D Tex(int w, int h, System.Func<float, float, float> alpha, string name) {
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
