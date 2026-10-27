using System.Collections.Generic;
using UnityEngine;

namespace PoRace {

/// <summary>
/// A track as data (PRD FR-02). The centerline is a polyline in the MuJoCo frame (x forward, y left, metres);
/// everything else (rails, visuals, start grid, checkpoints, steering targets) is derived from it.
/// Assets live in Assets/Resources/Maps so MapRegistry can discover them at runtime (FR-03).
/// </summary>
[CreateAssetMenu(menuName = "PoRace/Map Definition", fileName = "Map")]
public class MapDefinition : ScriptableObject {
  public string displayName = "Track";
  [Tooltip("Scene that contains this map's geometry (must be in the build).")]
  public string sceneName = "";
  public string terrain = "flat";
  [Range(1, 5)] public int difficulty = 1;
  public bool closed = false;
  [Range(1, 5)] public int defaultLaps = 1;
  public float halfWidth = 1.65f;
  [Range(1, 4)] public int gridSlots = 4;
  public float laneSpacing = 0.8f;
  public float checkpointSpacing = 25f;
  public float lookAhead = 4f;
  public List<Vector2> centerline = new List<Vector2>();
  [Tooltip("Lane spacing multiplier over one lap (x = 0..1 of lap length). Below 1 squeezes the racers together.")]
  public AnimationCurve laneScale = AnimationCurve.Constant(0f, 1f, 1f);
  [Tooltip("Cross-slope in degrees over one lap (x = 0..1 of lap length). Positive rises to the right of travel " +
           "(a left-hand turn), pivoting on the left edge so that edge stays flush with the floor. 0 = flat.")]
  public AnimationCurve bankDeg = AnimationCurve.Constant(0f, 1f, 0f);

  [Tooltip("Static obstacle boxes on the track (steeplechase). Heights must stay inside the trained bands of " +
           "training/make_steeple.py: hurdles and steps 3-8 cm, rubble 2-5 cm.")]
  public List<Obstacle> obstacles = new List<Obstacle>();

  /// <summary>Height of the tallest obstacle within `radius` of a MuJoCo-plane point (0 when clear). Used to lift
  /// respawns above blocks.</summary>
  public float ObstacleHeightAt(Vector2 p, float radius) {
    float h = 0f;
    foreach (var o in obstacles) {
      var c = o.Center(this, out var along);
      var d = p - c; var across = Left(along);
      if (Mathf.Abs(Vector2.Dot(d, along)) <= o.along * 0.5f + radius && Mathf.Abs(Vector2.Dot(d, across)) <= o.across * 0.5f + radius) h = Mathf.Max(h, o.height);
    }
    return h;
  }

  /// <summary>Half-width of the banked surface: a little wider than the track so the rails stand on it.</summary>
  public float BankHalfWidth => halfWidth + 0.15f;

  public float BankAt(float s) => bankDeg.Evaluate(closed ? Mathf.Repeat(s, Length) / Length : Mathf.Clamp01(s / Length));

  /// <summary>Track surface height (MuJoCo z) at arc length s, lateral offset from the centreline (positive = left).
  /// The pivot edge (left for positive bank, right for negative) is at z = 0, flush with the floor.</summary>
  public float SurfaceHeight(float s, float lateral) {
    float b = BankAt(s);
    if (Mathf.Abs(b) < 1e-3f) return 0f;
    return Mathf.Max(0f, BankHalfWidth - Mathf.Sign(b) * lateral) * Mathf.Tan(Mathf.Abs(b) * Mathf.Deg2Rad);
  }

  float[] _cum; float _length; int _n;

  void EnsureBuilt() {
    int segs = closed ? centerline.Count : centerline.Count - 1;
    if (_cum != null && _n == segs && _cum.Length == segs + 1) return;
    _n = segs; _cum = new float[segs + 1];
    for (int i = 0; i < segs; i++) _cum[i + 1] = _cum[i] + Vector2.Distance(P(i), P(i + 1));
    _length = _cum[segs];
  }

  public int SegmentCount { get { EnsureBuilt(); return _n; } }
  /// <summary>Length of one lap (closed) or of the whole track (open).</summary>
  public float Length { get { EnsureBuilt(); return _length; } }
  public Vector2 P(int i) => centerline[closed ? ((i % centerline.Count) + centerline.Count) % centerline.Count : Mathf.Clamp(i, 0, centerline.Count - 1)];
  public float SegmentStart(int i) { EnsureBuilt(); return _cum[i]; }
  public float SegmentLength(int i) { EnsureBuilt(); return _cum[i + 1] - _cum[i]; }

  /// <summary>Point and unit tangent at arc length s. Closed tracks wrap; open tracks extrapolate past the ends.</summary>
  public Vector2 PointAt(float s, out Vector2 tangent) {
    EnsureBuilt();
    if (closed) s = Mathf.Repeat(s, _length);
    int i = 0;
    if (s >= _length) i = _n - 1;
    else if (s > 0) { while (i < _n - 1 && _cum[i + 1] <= s) i++; }
    Vector2 a = P(i), b = P(i + 1);
    tangent = (b - a).normalized;
    return a + tangent * (s - _cum[i]);
  }

  public static Vector2 Left(Vector2 tangent) => new Vector2(-tangent.y, tangent.x);

  /// <summary>Lane centre at arc length s for a lane offset (positive = left of travel), with the lap's squeeze applied.</summary>
  public Vector2 LanePoint(float s, float laneOffset, out Vector2 tangent) {
    var p = PointAt(s, out tangent);
    float u = closed ? Mathf.Repeat(s, Length) / Length : Mathf.Clamp01(s / Length);
    return p + Left(tangent) * laneOffset * laneScale.Evaluate(u);
  }

  /// <summary>Surface height under a point given in the MuJoCo plane: nearest centreline point, then SurfaceHeight.
  /// Brute force over the segments (a few dozen), fine once per frame for the camera.</summary>
  public float SurfaceHeightAt(Vector2 p) {
    EnsureBuilt();
    if (_n == 0) return 0f;
    float best = float.MaxValue, bs = 0f, bl = 0f;
    for (int i = 0; i < _n; i++) {
      Vector2 a = P(i), d = P(i + 1) - a; float len = d.magnitude; if (len < 1e-6f) continue; d /= len;
      float t = Mathf.Clamp(Vector2.Dot(p - a, d), 0f, len); var q = a + d * t; float dist = (p - q).sqrMagnitude;
      if (dist < best) { best = dist; bs = _cum[i] + t; bl = Vector2.Dot(p - q, Left(d)); }
    }
    return SurfaceHeight(bs, bl);
  }

  /// <summary>Lane offsets for n racers, centred on the track, leftmost first.</summary>
  public float[] LaneOffsets(int n) {
    var r = new float[n];
    for (int i = 0; i < n; i++) r[i] = ((n - 1) * 0.5f - i) * laneSpacing;
    return r;
  }
}

/// <summary>An obstacle box placed in the track frame: centre at arc length s and lateral offset (+ left), sized
/// along x across x height (m), rotated by yaw degrees from the track direction. Sits on the floor.</summary>
[System.Serializable]
public struct Obstacle {
  public string kind;          // hurdle | step | rubble
  public float s, lateral, along, across, height, yaw;

  /// <summary>Centre in the MuJoCo plane and the obstacle's own "along" axis.</summary>
  public Vector2 Center(MapDefinition map, out Vector2 alongAxis) {
    var p = map.PointAt(s, out var t);
    float a = yaw * Mathf.Deg2Rad;
    alongAxis = new Vector2(t.x * Mathf.Cos(a) - t.y * Mathf.Sin(a), t.x * Mathf.Sin(a) + t.y * Mathf.Cos(a));
    return p + MapDefinition.Left(t) * lateral;
  }
}

/// <summary>Runtime discovery of every map asset under Resources/Maps (PRD FR-03).</summary>
public static class MapRegistry {
  public static MapDefinition[] All() {
    var maps = Resources.LoadAll<MapDefinition>("Maps");
    System.Array.Sort(maps, (a, b) => string.CompareOrdinal(a.name, b.name));
    return maps;
  }
}

/// <summary>Choices made on the pre-race screen, carried across the scene load. Defaults give a full grid.</summary>
public static class RaceConfig {
  public static int racers = 4;
  public static bool matt = false;          // Matt (the human racer) on the grid; flat maps only
  // Self-running app: launch straight into a race, then race after race (MultiRaceOrchestrator). Off after the player
  // presses Menu, or when started with -menu.
  public static bool autoplay = !System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-menu");
  public static int autoplayMap = -1;       // index into the flat maps of MapRegistry; -1 = not started yet

  /// <summary>Next race of the self-running app: the flat maps in turn (Matt races on flat ground only), four dogs and
  /// Matt, a fresh seed. Light on the GPU: Mobile graphics tier (GfxTier) and 30 fps.</summary>
  public static void LaunchNextAutoplay() {
    var flat = System.Array.FindAll(MapRegistry.All(), m => m.terrain == "flat");
    if (flat.Length == 0) return;
    autoplayMap = (autoplayMap + 1) % flat.Length;
    racers = 4; laps = 0; seed = -1; fromMenu = true; matt = true;
    QualitySettings.vSyncCount = 0; Application.targetFrameRate = 30;
    UnityEngine.SceneManagement.SceneManager.LoadScene(flat[autoplayMap].sceneName);
  }
  public static int laps = 0;        // 0 = the map's default
  public static int seed = -1;       // -1 = different every race
  public static bool fromMenu = false;
  public static string twitchChannel = "";   // live chat hazards / bets (anonymous read-only IRC); "" = off
  public static int weatherCity = 0;         // index into TrackWeather.Cities; -1 = off
}

}
