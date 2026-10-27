using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Mujoco;
using UnityEngine;

namespace PoRace {

public enum Surface { Track, Metal, Wood, Robot, Cube }
public enum ContactKind { Foot, Body, RobotRobot, Cube }

/// <summary>H.1, contact impacts for the effects and sound layers, read from mjData every physics step (4 ms):
///   impact  a geom pair that starts touching. Impulse is the estimate needed to stop the normal approach:
///           J = m_eff * v_n, v_n from mj_objectVelocity at the contact point, m_eff the reduced mass of the two sides
///           (foot 1.5 kg, any other robot geom the mass of its body and everything below it, at most half the robot,
///           so a grazing calf is light and a landing trunk is heavy; cube its body mass, world infinite). Summing
///           mj_contactForce over a window was rejected: standing load dominates it, so a still foot reads as a hit.
///   slip    per control step, tangential speed of each foot touching a world geom (scrape and wet-track sounds,
///           scuff marks).
///   wear    running sum of non-foot impulses per racer (robot grime, telemetry), reset on restart.
/// World geoms get a surface from their name: rail -> Metal, obs (steeplechase boxes) -> Wood, anything else -> Track.
/// Read only: nothing here writes MuJoCo state.</summary>
public unsafe class ContactImpulse : MonoBehaviour {
  public MultiRaceOrchestrator race;
  [Tooltip("Smallest impulse reported (N·s); feet use a tenth of it.")]
  public float minImpulse = 0.4f;
  public float footMass = 1.5f;

  public struct Hit {
    public ContactKind kind; public Surface surface; public float impulse, approach;   // N·s, m/s
    public Vector3 pos, normal;                                                           // Unity frame, normal points up / away from the struck surface
    public Racer racer, other; public int foot;                                           // foot -1 unless kind == Foot
  }
  /// <summary>A new contact, at most once per geom pair per contact episode.</summary>
  public event Action<Hit> Impact;
  /// <summary>(racer, foot 0..3, tangential slip m/s, surface, Unity position, Unity normal), per control step.</summary>
  public event Action<Racer, int, float, Surface, Vector3, Vector3> Slip;

  public float Wear(Racer r) => r != null && _wear.TryGetValue(r, out var w) ? w : 0f;

  Racer[] _geomRacer; int[] _geomFoot; Surface[] _surface; float[] _mass; bool[] _cube;
  Dictionary<long, int> _seen = new Dictionary<long, int>(), _next = new Dictionary<long, int>();
  readonly Dictionary<Racer, float> _wear = new Dictionary<Racer, float>();
  readonly Dictionary<Racer, int> _slot = new Dictionary<Racer, int>();
  // Per racer x 4 feet (a Go2 uses all four, Matt two), sized in Init: four dogs plus Matt is five racers.
  float[] _slip = new float[0]; Vector3[] _slipPos, _slipN; Surface[] _slipSurf;
  int _step;
  readonly int[] _count = new int[4]; readonly float[] _sum = new float[4], _max = new float[4];

  void Awake() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    MjScene.Instance.postUpdateEvent += OnPostStep;
  }
  void Start() { race.Restarted += () => _wear.Clear(); }
  void OnDestroy() {
    if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= OnPostStep;
    var sb = new System.Text.StringBuilder("[Contacts] impacts");
    for (int k = 0; k < 4; k++) sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "  {0} {1} (mean {2:F2}, max {3:F2} N·s)", (ContactKind)k, _count[k], _count[k] > 0 ? _sum[k] / _count[k] : 0f, _max[k]);
    Debug.Log(sb.ToString());
  }

  void Init(MujocoLib.mjModel_* m) {
    int n = (int)m->ngeom;
    _geomRacer = new Racer[n]; _geomFoot = new int[n]; _surface = new Surface[n]; _mass = new float[n]; _cube = new bool[n];
    var root = new Dictionary<int, Racer>(); var robotMass = new Dictionary<Racer, float>();
    for (int i = 0; i < race.Live.Count; i++) _slot[race.Live[i]] = i;
    int slots = 4 * race.Live.Count;
    _slip = new float[slots]; _slipPos = new Vector3[slots]; _slipN = new Vector3[slots]; _slipSurf = new Surface[slots];
    foreach (var r in race.Live) {
      var B = r.Body; if (B == null || !B.Ready) { _geomRacer = null; return; }
      root[B.BaseBody] = r; float mass = 0f;
      for (int b = 0; b < (int)m->nbody; b++) if (m->body_rootid[b] == B.BaseBody) mass += (float)m->body_mass[b];
      robotMass[r] = mass;
    }
    for (int g = 0; g < n; g++) {
      _geomFoot[g] = -1; int b = m->geom_bodyid[g];
      if (b == 0) {
        string name = Marshal.PtrToStringAnsi(MujocoLib.mj_id2name(m, (int)MujocoLib.mjtObj.mjOBJ_GEOM, g)) ?? "";
        _surface[g] = name.Contains("rail") ? Surface.Metal : name.Contains("obs") ? Surface.Wood : Surface.Track;
        _mass[g] = float.PositiveInfinity; continue;
      }
      if (root.TryGetValue(m->body_rootid[b], out var r)) { _geomRacer[g] = r; _surface[g] = Surface.Robot; _mass[g] = Mathf.Min((float)m->body_subtreemass[b], 0.5f * robotMass[r]); }
      else { _cube[g] = true; _surface[g] = Surface.Cube; _mass[g] = (float)m->body_mass[b]; }
    }
    foreach (var r in race.Live) {
      var feet = r.matt != null ? new[] { "left_foot", "right_foot" } : Array.ConvertAll(Go2Model.Legs, l => r.controller.prefix + l);
      for (int f = 0; f < feet.Length; f++) {
        int g = MujocoLib.mj_name2id(m, (int)MujocoLib.mjtObj.mjOBJ_GEOM, feet[f]);
        if (g >= 0) { _geomFoot[g] = f; _mass[g] = r.matt != null ? footMass * 4f : footMass; }   // effective striking mass: a human foot brings the shank and foot (~5.4 kg for Matt)
      }
    }
  }

  static Vector3 U(double* v) => new Vector3((float)v[0], (float)v[2], (float)v[1]);

  /// <summary>World velocity of geom g at point p (MuJoCo frame).</summary>
  static Vector3 PointVel(MujocoLib.mjModel_* m, MujocoLib.mjData_* d, int g, Vector3 p) {
    if (m->geom_bodyid[g] == 0) return Vector3.zero;
    double* res = stackalloc double[6];
    MujocoLib.mj_objectVelocity(m, d, (int)MujocoLib.mjtObj.mjOBJ_GEOM, g, res, 0);
    var w = new Vector3((float)res[0], (float)res[1], (float)res[2]); var v = new Vector3((float)res[3], (float)res[4], (float)res[5]);
    var c = new Vector3((float)d->geom_xpos[3 * g], (float)d->geom_xpos[3 * g + 1], (float)d->geom_xpos[3 * g + 2]);
    return v + Vector3.Cross(w, p - c);
  }

  void OnPostStep(object sender, MjStepArgs a) {
    if (race == null || race.Live.Count == 0) return;
    var m = a.model; var d = a.data;
    if (_geomRacer == null) { Init(m); if (_geomRacer == null) return; }
    bool control = _step++ % Go2Controller.Decimation == 0;
    if (control) Array.Clear(_slip, 0, _slip.Length);
    long ng = _geomRacer.Length; _next.Clear();

    for (int i = 0; i < d->ncon; i++) {
      var con = d->contact + i; int g1 = con->geom1, g2 = con->geom2;
      long key = g1 * ng + g2;
      var p = new Vector3((float)con->pos[0], (float)con->pos[1], (float)con->pos[2]);
      var nrm = new Vector3((float)con->frame[0], (float)con->frame[1], (float)con->frame[2]);   // geom1 -> geom2
      bool fresh = !_seen.ContainsKey(key) && !_next.ContainsKey(key);
      _next[key] = 1;

      // Foot slip on world geoms (sampled once per control step).
      int fg = _geomFoot[g1] >= 0 && m->geom_bodyid[g2] == 0 ? g1 : _geomFoot[g2] >= 0 && m->geom_bodyid[g1] == 0 ? g2 : -1;
      if (control && fg >= 0) {
        var v = PointVel(m, d, fg, p); var t = v - Vector3.Dot(v, nrm) * nrm;
        int slot = _slot[_geomRacer[fg]] * 4 + _geomFoot[fg];
        if (slot >= 0 && slot < _slip.Length && t.magnitude > _slip[slot]) {
          _slip[slot] = t.magnitude; _slipPos[slot] = new Vector3(p.x, p.z, p.y);
          var up = fg == g1 ? -nrm : nrm; _slipN[slot] = new Vector3(up.x, up.z, up.y);   // out of the ground
          _slipSurf[slot] = _surface[fg == g1 ? g2 : g1];
        }
      }
      if (!fresh) continue;

      var rel = PointVel(m, d, g2, p) - PointVel(m, d, g1, p);
      float vn = -Vector3.Dot(rel, nrm);   // closing speed
      if (vn <= 0f) continue;
      float m1 = _mass[g1], m2 = _mass[g2];
      float mu = float.IsInfinity(m1) ? m2 : float.IsInfinity(m2) ? m1 : m1 * m2 / (m1 + m2);
      float J = mu * vn;
      var r1 = _geomRacer[g1]; var r2 = _geomRacer[g2];
      bool foot = _geomFoot[g1] >= 0 || _geomFoot[g2] >= 0;
      var kind = r1 != null && r2 != null ? ContactKind.RobotRobot : _cube[g1] || _cube[g2] ? ContactKind.Cube : foot ? ContactKind.Foot : ContactKind.Body;
      if (J < (kind == ContactKind.Foot ? minImpulse * 0.1f : minImpulse)) continue;
      // The struck surface is the side that is not the moving robot (or either robot / the cube for those kinds).
      int struck = r1 != null && r2 == null ? g2 : g1;
      var n = struck == g2 ? -nrm : nrm;   // out of the struck geom, toward the other
      var hit = new Hit {
        kind = kind, surface = _surface[struck], impulse = J, approach = vn, pos = new Vector3(p.x, p.z, p.y), normal = new Vector3(n.x, n.z, n.y),
        racer = r1 ?? r2, other = r1 != null ? r2 : null, foot = foot ? Mathf.Max(_geomFoot[g1], _geomFoot[g2]) : -1 };
      if (kind != ContactKind.Foot) {
        if (r1 != null) _wear[r1] = Wear(r1) + J;
        if (r2 != null) _wear[r2] = Wear(r2) + J;
      }
      _count[(int)kind]++; _sum[(int)kind] += J; _max[(int)kind] = Mathf.Max(_max[(int)kind], J);
      Impact?.Invoke(hit);
    }
    (_seen, _next) = (_next, _seen);

    if (!control || Slip == null) return;
    for (int i = 0; i < race.Live.Count; i++)
      for (int f = 0; f < 4; f++) { int s = i * 4 + f; if (_slip[s] > 0f) Slip(race.Live[i], f, _slip[s], _slipSurf[s], _slipPos[s], _slipN[s]); }
  }
}

}
