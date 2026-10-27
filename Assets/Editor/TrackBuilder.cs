using System.Collections.Generic;
using System.Linq;
using Mujoco;
using PoRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-time track authoring from a MapDefinition. Physics = side rails as native MuJoCo world geoms (MjGeom boxes
/// under the MuJoCo scene root). Visuals = plain meshes without colliders. Nothing here runs in the player.
/// MuJoCo (x, y, z up) maps to Unity (x, z, y); a MuJoCo yaw of theta is a Unity rotation of -theta about Y.
/// </summary>
public static class TrackBuilder {
  const string MapDir = "Assets/Resources/Maps";

  static Material Mat(string name, Color col) {
    if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
    var path = "Assets/Materials/" + name + ".mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { m = new Material(MjcfImporter.GetLitShader()); AssetDatabase.CreateAsset(m, path); }
    m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
    EditorUtility.SetDirty(m); return m;
  }

  static MapDefinition MapAsset(string file) {
    if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
    if (!AssetDatabase.IsValidFolder(MapDir)) AssetDatabase.CreateFolder("Assets/Resources", "Maps");
    var path = MapDir + "/" + file + ".asset";
    var m = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
    if (m == null) { m = ScriptableObject.CreateInstance<MapDefinition>(); AssetDatabase.CreateAsset(m, path); }
    return m;
  }

  /// <summary>Creates / refreshes the launch maps: 01 Planar Sprint and 02 Flat Oval.</summary>
  [MenuItem("PoRace/Create Launch Maps")]
  public static void CreateMaps() {
    var s = MapAsset("Map01_PlanarSprint");
    s.displayName = "Planar Sprint"; s.sceneName = "Track01x4"; s.terrain = "flat"; s.difficulty = 1; s.closed = false; s.defaultLaps = 1;
    s.halfWidth = 1.65f; s.gridSlots = 4; s.laneSpacing = 0.8f; s.checkpointSpacing = 25f; s.lookAhead = 4f;
    s.centerline = new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0) };
    // All four lanes merge to one line between 40 % and 60 % of the track, then spread again.
    s.laneScale = new AnimationCurve(new Keyframe(0f, 1f, 0, 0), new Keyframe(0.15f, 1f, 0, 0), new Keyframe(0.4f, 0f, 0, 0), new Keyframe(0.6f, 0f, 0, 0), new Keyframe(0.85f, 1f, 0, 0), new Keyframe(1f, 1f, 0, 0));
    EditorUtility.SetDirty(s);

    var o = MapAsset("Map02_FlatOval");
    o.displayName = "Flat Oval"; o.sceneName = "Track02"; o.terrain = "flat"; o.difficulty = 2;
    Oval(o);
    EditorUtility.SetDirty(o);

    // Map 03: the same oval with 15 degree banked turns (PRD Map 02 spec). The bank ramps in over the last 4 m of each
    // straight and out over the first 4 m of the next, and pivots on the inside edge so that edge stays at floor level.
    var b = MapAsset("Map03_BankedOval");
    b.displayName = "Banked Oval"; b.sceneName = "Track03"; b.terrain = "banked"; b.difficulty = 3;
    Oval(b);
    float lap = b.Length, turn = (lap - 25f - 30f - 5f) / 2f, t1 = 25f, t2 = 25f + turn + 30f;
    b.bankDeg = Linear(lap, (0, 0), (t1 - 4, 0), (t1, Bank), (t1 + turn, Bank), (t1 + turn + 4, 0),
                            (t2 - 4, 0), (t2, Bank), (t2 + turn, Bank), (t2 + turn + 4, 0), (lap, 0));
    EditorUtility.SetDirty(b);

    var st = MapAsset("Map04_Steeplechase");
    st.displayName = "Steeplechase"; st.sceneName = "Track04"; st.terrain = "obstacles"; st.difficulty = 4; st.closed = false; st.defaultLaps = 1;
    st.halfWidth = 1.65f; st.gridSlots = 4; st.laneSpacing = 0.8f; st.checkpointSpacing = 25f; st.lookAhead = 4f;
    st.centerline = new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0) };
    st.laneScale = AnimationCurve.Constant(0f, 1f, 1f);
    st.obstacles = SteepleCourse(st.halfWidth);
    EditorUtility.SetDirty(st);
    AssetDatabase.SaveAssets();
    Debug.Log($"[TrackBuilder] maps: {s.displayName} {s.Length:F1} m, {o.displayName} {o.Length:F1} m per lap ({o.centerline.Count} points), {b.displayName} turns {turn:F2} m at {Bank} deg");
  }

  const float Bank = 15f;

  /// <summary>Map 04 course. Heights stay inside the trained bands (training/make_steeple.py): hurdles and steps
  /// 3-8 cm, rubble 2-5 cm. Hurdles and steps span the track between the rails, so every lane meets the same course.
  ///   14-26 m  three hurdles, 3 / 4.5 / 6 cm      34-38 m  a 5 cm step up and down
  ///   44 m     7 cm hurdle                        50-62 m  rubble field (24 blocks, seeded)
  ///   67-71 m  two-stage staircase, 4 + 4 cm      80-90 m  three hurdles, 5 / 6.5 / 8 cm</summary>
  static List<Obstacle> SteepleCourse(float halfWidth) {
    float across = 2f * halfWidth - 0.12f;
    var o = new List<Obstacle>();
    void Add(string kind, float s, float lat, float along, float acr, float h, float yaw = 0f) => o.Add(new Obstacle { kind = kind, s = s, lateral = lat, along = along, across = acr, height = h, yaw = yaw });
    Add("hurdle", 14f, 0f, 0.12f, across, 0.03f); Add("hurdle", 20f, 0f, 0.12f, across, 0.045f); Add("hurdle", 26f, 0f, 0.12f, across, 0.06f);
    Add("step", 36f, 0f, 4f, across, 0.05f);
    Add("hurdle", 44f, 0f, 0.14f, across, 0.07f);
    var rng = new System.Random(4);
    for (int i = 0; i < 24; i++) {
      float sz = 0.12f + 0.16f * (float)rng.NextDouble();
      Add("rubble", 50f + 12f * (float)rng.NextDouble(), (float)(rng.NextDouble() * 2 - 1) * (halfWidth - 0.25f), sz, sz * (0.6f + 0.8f * (float)rng.NextDouble()),
          0.02f + 0.03f * (float)rng.NextDouble(), (float)rng.NextDouble() * 180f);
    }
    Add("step", 69f, 0f, 4f, across, 0.04f); Add("step", 69.5f, 0f, 2f, across, 0.08f);
    Add("hurdle", 80f, 0f, 0.12f, across, 0.05f); Add("hurdle", 85f, 0f, 0.12f, across, 0.065f); Add("hurdle", 90f, 0f, 0.15f, across, 0.08f);
    return o;
  }

  /// <summary>30 m straights joined by two 180 degree left turns of radius 6 m; the lap starts 5 m into the south straight.</summary>
  static void Oval(MapDefinition o) {
    o.closed = true; o.defaultLaps = 2;
    o.halfWidth = 2.0f; o.gridSlots = 4; o.laneSpacing = 0.8f; o.checkpointSpacing = 12f; o.lookAhead = 2.5f;
    const float L = 30f, R = 6f; const int N = 14;
    var pts = new List<Vector2> { new Vector2(5, 0), new Vector2(L, 0) };
    for (int i = 1; i <= N; i++) { float a = Mathf.PI * i / N; pts.Add(new Vector2(L + R * Mathf.Sin(a), R - R * Mathf.Cos(a))); }       // left turn, east end
    pts.Add(new Vector2(0, 2 * R));
    for (int i = 1; i <= N; i++) { float a = Mathf.PI * i / N; pts.Add(new Vector2(-R * Mathf.Sin(a), R + R * Mathf.Cos(a))); }          // left turn, west end
    o.centerline = pts;   // closes back to (5, 0)
    // Full grid spacing on the start straight, then the lanes squeeze into a racing line (35 %) for the rest of the lap.
    o.laneScale = new AnimationCurve(new Keyframe(0f, 1f, 0, 0), new Keyframe(0.1f, 1f, 0, 0), new Keyframe(0.24f, 0.35f, 0, 0), new Keyframe(0.9f, 0.35f, 0, 0), new Keyframe(1f, 1f, 0, 0));
  }

  /// <summary>Piecewise-linear curve over lap fraction from (arc length m, value) points.</summary>
  static AnimationCurve Linear(float lap, params (float s, float v)[] p) {
    var keys = new Keyframe[p.Length];
    for (int i = 0; i < p.Length; i++) {
      float x = p[i].s / lap;
      float inS = i > 0 ? (p[i].v - p[i - 1].v) / (x - p[i - 1].s / lap) : 0f, outS = i < p.Length - 1 ? (p[i + 1].v - p[i].v) / (p[i + 1].s / lap - x) : 0f;
      keys[i] = new Keyframe(x, p[i].v, inS, outS);
    }
    return new AnimationCurve(keys);
  }

  static Quaternion Yaw(Vector2 dir) => Quaternion.Euler(0f, -Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, 0f);
  static Vector3 U(Vector2 mj, float height) => new Vector3(mj.x, height, mj.y);

  /// <summary>Copies `sourceScene` (a scene that already holds the racers) to `targetScene` and rebuilds its track
  /// geometry from the map: MuJoCo rails, visual surface, start/finish line, checkpoint marks.</summary>
  public static void BuildScene(string mapAssetPath, string sourceScene, string targetScene, bool extendOpenEnds = true) {
    var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(mapAssetPath);
    var src = EditorSceneManager.OpenScene(sourceScene, OpenSceneMode.Single);
    if (sourceScene != targetScene) { EditorSceneManager.SaveScene(src, targetScene, true); }
    var scene = EditorSceneManager.OpenScene(targetScene, OpenSceneMode.Single);
    var mjRoot = GameObject.Find("go2_scene");
    foreach (var oldName in new[] { "Track01", "TrackVisuals" }) { var g = GameObject.Find(oldName); if (g != null) Object.DestroyImmediate(g); }
    foreach (var oldName in new[] { "rails", "bank", "obstacles" }) { var old = mjRoot.transform.Find(oldName); if (old != null) Object.DestroyImmediate(old.gameObject); }

    Material mA = Mat("TrackA", new Color(0.30f, 0.32f, 0.36f)), mB = Mat("TrackB", new Color(0.36f, 0.38f, 0.42f)),
             mRail = Mat("Rail", new Color(0.85f, 0.25f, 0.15f)), mLine = Mat("Line", Color.white), mMark = Mat("Marker", new Color(0.95f, 0.8f, 0.1f));
    var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    var rails = new GameObject("rails"); rails.transform.SetParent(mjRoot.transform, false);
    var vis = new GameObject("TrackVisuals");
    System.Action<string, Vector3, Quaternion, Vector3, Material> V = (name, pos, rot, scale, m) => {
      var go = new GameObject(name); go.transform.SetParent(vis.transform, false); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scale;
      go.AddComponent<MeshFilter>().sharedMesh = cube; go.AddComponent<MeshRenderer>().sharedMaterial = m;
    };
    System.Action<string, Vector2, Vector2> Rail = (name, a, b) => {
      var dir = (b - a); float len = dir.magnitude; dir /= len;
      var go = new GameObject(name); go.transform.SetParent(rails.transform, false);
      go.transform.SetPositionAndRotation(U((a + b) * 0.5f, 0.15f), Yaw(dir));
      var g = go.AddComponent<MjGeom>(); g.ShapeType = MjShapeComponent.ShapeTypes.Box; g.Box.Extents = new Vector3(len * 0.5f + 0.06f, 0.15f, 0.05f);
      go.AddComponent<MeshFilter>(); go.AddComponent<MjMeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = mRail;
    };
    // Rail between two surface points given in MuJoCo (x, y, z); local x runs along the rail, local y stays upright-ish.
    System.Action<string, Vector3, Vector3> Rail3 = (name, a, b) => {
      Vector3 ua = new Vector3(a.x, a.z, a.y), ub = new Vector3(b.x, b.z, b.y), d = ub - ua; float len = d.magnitude;
      var go = new GameObject(name); go.transform.SetParent(rails.transform, false);
      go.transform.SetPositionAndRotation((ua + ub) * 0.5f + Vector3.up * 0.15f, Quaternion.LookRotation(d, Vector3.up) * Quaternion.Euler(0f, -90f, 0f));
      var g = go.AddComponent<MjGeom>(); g.ShapeType = MjShapeComponent.ShapeTypes.Box; g.Box.Extents = new Vector3(len * 0.5f + 0.06f, 0.15f, 0.05f);
      go.AddComponent<MeshFilter>(); go.AddComponent<MjMeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = mRail;
    };

    int n = map.SegmentCount; float w = map.halfWidth;
    // Offset polylines with vertex normals (average of the adjoining segment normals) so corner rails join up.
    var left = new List<Vector2>(); var right = new List<Vector2>(); var centre = new List<Vector2>(); var miter = new List<Vector2>();
    int verts = map.closed ? n : n + 1;
    for (int i = 0; i < verts; i++) {
      Vector2 p = map.P(i), dPrev = (map.closed || i > 0) ? (p - map.P(i - 1)).normalized : (map.P(i + 1) - p).normalized, dNext = (map.closed || i < n) ? (map.P(i + 1) - p).normalized : dPrev;
      var nrm = (MapDefinition.Left(dPrev) + MapDefinition.Left(dNext)).normalized;
      float k = 1f / Mathf.Max(0.5f, Vector2.Dot(nrm, MapDefinition.Left(dNext)));   // miter length
      if (!map.closed && extendOpenEnds) p += (i == 0 ? -dNext * 6f : i == n ? dPrev * 6f : Vector2.zero);
      centre.Add(p); left.Add(p + nrm * w * k); right.Add(p - nrm * w * k); miter.Add(nrm * k);
    }
    bool Banked(float s0, float s1) => Mathf.Abs(map.BankAt(s0)) > 1e-3f || Mathf.Abs(map.BankAt(s1)) > 1e-3f || Mathf.Abs(map.BankAt(0.5f * (s0 + s1))) > 1e-3f;
    var bank = new BankSurface(map);
    for (int i = 0; i < n; i++) {
      int j = (i + 1) % verts;
      float s0 = map.SegmentStart(i), segLen = map.SegmentLength(i);
      Vector2 a = centre[i], b = centre[j], dir = (b - a); float len = dir.magnitude; dir /= len;
      if (!Banked(s0, s0 + segLen)) {
        Rail("rail_L" + i, left[i], left[j]); Rail("rail_R" + i, right[i], right[j]);
      } else {
        // Banked: rails stand on the surface, cut into the same pieces so they follow the ramp.
        int na = Mathf.Max(1, Mathf.CeilToInt(segLen / BankSurface.PieceLength));
        for (int t = 0; t < na; t++) {
          float u0 = (float)t / na, u1 = (float)(t + 1) / na, sa = s0 + u0 * segLen, sb = s0 + u1 * segLen;
          foreach (var side in new[] { 1f, -1f }) {
            Vector2 pa = Vector2.Lerp(centre[i] + miter[i] * side * w, centre[j] + miter[j] * side * w, u0);
            Vector2 pb = Vector2.Lerp(centre[i] + miter[i] * side * w, centre[j] + miter[j] * side * w, u1);
            Rail3((side > 0 ? "rail_L" : "rail_R") + i + "_" + t, new Vector3(pa.x, pa.y, map.SurfaceHeight(sa, side * w)), new Vector3(pb.x, pb.y, map.SurfaceHeight(sb, side * w)));
          }
        }
        bank.AddSegment(i, s0, segLen, centre[i], centre[j], miter[i], miter[j]);
      }
      // Surface: long segments are cut into ~5 m tiles so alternating shades show speed. Banked stretches are drawn by
      // the bank mesh instead.
      int tiles = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
      for (int t = 0; t < tiles; t++) {
        if (Banked(s0 + segLen * t / tiles, s0 + segLen * (t + 1) / tiles)) continue;
        Vector2 c = a + dir * (len * (t + 0.5f) / tiles);
        V($"tile_{i}_{t}", U(c, -0.012f), Yaw(dir), new Vector3(len / tiles + 0.12f, 0.02f, 2f * w + 0.3f), ((i + t) % 2 == 0) ? mA : mB);
      }
    }
    bank.Build(mapAssetPath, mjRoot.transform, vis.transform, mA);
    Obstacles(map, mjRoot.transform, vis.transform);
    // Start / finish line and checkpoint marks, across the track at their arc position (lying on the bank where there is one).
    System.Func<float, Vector2, Quaternion> OnTrack = (s, tan) => Yaw(tan) * Quaternion.Euler(map.BankAt(s), 0f, 0f);
    var p0 = map.PointAt(0f, out var t0);
    V("start_line", U(p0 + t0 * 0.6f, map.SurfaceHeight(0.6f, 0f) - 0.0015f), OnTrack(0.6f, t0), new Vector3(0.15f, 0.003f, 2f * w), mLine);
    for (float s = map.checkpointSpacing; s < map.Length - 0.5f; s += map.checkpointSpacing) {
      var p = map.PointAt(s, out var tt); float up = Mathf.Abs(map.BankAt(s)) > 1e-3f ? 0.004f : -0.0015f;
      V("mark_" + Mathf.RoundToInt(s), U(p, map.SurfaceHeight(s, 0f) + up), OnTrack(s, tt), new Vector3(0.1f, 0.003f, 2f * w), mMark);
    }
    var pf = map.closed ? p0 : map.PointAt(map.Length, out t0); var nf = MapDefinition.Left(t0);
    V("arch_l", U(pf + nf * (w + 0.2f), 0.9f), Yaw(t0), new Vector3(0.1f, 1.8f, 0.1f), mLine);
    V("arch_r", U(pf - nf * (w + 0.2f), 0.9f), Yaw(t0), new Vector3(0.1f, 1.8f, 0.1f), mLine);
    V("arch_top", U(pf, 1.8f), Yaw(t0), new Vector3(0.12f, 0.15f, 2f * w + 0.5f), mMark);

    // Race wiring: the orchestrator reads the map; racer start transforms follow the grid at arc length 0.
    var race = Object.FindFirstObjectByType<MultiRaceOrchestrator>(FindObjectsInactive.Include);
    race.map = map; EditorUtility.SetDirty(race);
    var lanes = map.LaneOffsets(race.racers.Count);
    for (int k = 0; k < race.racers.Count; k++) {
      var r = race.racers[k]; var gp = map.LanePoint(0f, lanes[k], out var gt);
      var baseGo = GameObject.Find(r.controller.prefix + "base");
      if (baseGo != null) { baseGo.transform.SetPositionAndRotation(new Vector3(gp.x, baseGo.transform.position.y, gp.y), Yaw(gt)); }
      if (k > 0 && r.mjRoot == null) r.mjRoot = GameObject.Find("racer" + k);
    }
    int phys = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length + Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
    Debug.Log($"[TrackBuilder] {targetScene}: map '{map.displayName}' {map.Length:F1} m, {n} segments, {rails.transform.childCount} MuJoCo rail geoms, {vis.transform.childCount} visuals, PhysX components={phys}");
  }

  /// <summary>Steeplechase obstacles: MuJoCo box geoms with the floor's contact settings (what the policy collides
  /// with, same as the training scene) plus Blender-made visuals scaled onto each box (Assets/Art/Exports/GLB,
  /// tools/blender/steeple_props.py). The visual meshes are unit cubes centred on the origin.</summary>
  static void Obstacles(MapDefinition map, Transform mjRoot, Transform visRoot) {
    if (map.obstacles == null || map.obstacles.Count == 0) return;
    var root = new GameObject("obstacles"); root.transform.SetParent(mjRoot, false);
    for (int i = 0; i < map.obstacles.Count; i++) {
      var o = map.obstacles[i]; var c = o.Center(map, out var along);
      var pos = U(c, o.height * 0.5f); var rot = Yaw(along);
      var go = new GameObject($"obs{i}_{o.kind}"); go.transform.SetParent(root.transform, false); go.transform.SetPositionAndRotation(pos, rot);
      var g = go.AddComponent<MjGeom>(); g.ShapeType = MjShapeComponent.ShapeTypes.Box; g.Box.Extents = new Vector3(o.along * 0.5f, o.height * 0.5f, o.across * 0.5f);
      var st = g.Settings; st.Priority = 1; st.Friction.Sliding = 0.6f; st.Solver.ConDim = 3; st.Filtering.Contype = 1; st.Filtering.Conaffinity = 0; g.Settings = st;

      var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Exports/GLB/{o.kind}.fbx");
      var mf = src != null ? src.GetComponentInChildren<MeshFilter>() : null;
      var v = new GameObject($"vis{i}_{o.kind}"); v.transform.SetParent(visRoot, false);
      v.transform.SetPositionAndRotation(pos, rot); v.transform.localScale = new Vector3(o.along, o.height, o.across);
      if (mf != null) {
        v.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        v.AddComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
      } else {   // props not built yet: plain boxes
        v.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        v.AddComponent<MeshRenderer>().sharedMaterial = Mat("Obstacle", new Color(0.2f, 0.5f, 0.8f));
      }
    }
    Debug.Log($"[TrackBuilder] {map.obstacles.Count} obstacle geoms (max height {map.obstacles.Max(x => x.height) * 100:F1} cm)");
  }

  /// <summary>Map 04 only: Track04 from the four-racer scene. Racers use the steeplechase policy when it exists
  /// (graduated s1 run, Assets/Policies/go2_loco_steeple.onnx), otherwise the slope-trained bank policy.</summary>
  [MenuItem("PoRace/Build Steeplechase")]
  public static void BuildSteeple() {
    CreateMaps();
    const string path = "Assets/Scenes/Track04.unity";
    BuildScene(MapDir + "/Map04_Steeplechase.asset", "Assets/Scenes/Track01x4.unity", path);
    var policy = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Policies/go2_loco_steeple.onnx")
              ?? AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Policies/go2_loco_bank.onnx");
    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    foreach (var c in Object.FindObjectsByType<Go2Controller>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { c.locomotionModel = policy; EditorUtility.SetDirty(c); }
    EditorSceneManager.SaveScene(scene);
    Debug.Log($"[TrackBuilder] Track04 locomotion policy: {AssetDatabase.GetAssetPath(policy)}");
    if (!EditorBuildSettings.scenes.Any(sc => sc.path == path)) {
      var list = EditorBuildSettings.scenes.ToList(); int at = list.FindIndex(sc => sc.path == "Assets/Scenes/Track03.unity") + 1;
      list.Insert(at > 0 ? at : list.Count, new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = list.ToArray();
    }
  }

  [MenuItem("PoRace/Rebuild Launch Tracks")]
  public static void RebuildAll() {
    CreateMaps();
    BuildScene(MapDir + "/Map01_PlanarSprint.asset", "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track01x4.unity");
    BuildScene(MapDir + "/Map02_FlatOval.asset", "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track02.unity");
    BuildBanked();
  }

  /// <summary>Map 03 only: Track03 from the four-racer scene, added to the build after the existing tracks.</summary>
  [MenuItem("PoRace/Build Banked Oval")]
  public static void BuildBanked() {
    CreateMaps();
    BuildScene(MapDir + "/Map03_BankedOval.asset", "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track03.unity");
    const string path = "Assets/Scenes/Track03.unity";
    // Slope-trained locomotion for this track only; the flat tracks keep the parity-gated policy. The graduated steeplechase
    // policy (s7, also slope-trained) scores bank 30/30 against r7's 6/10 and races Track03 seeds 1-5 in 138.5-149.9 s
    // (r7: 148.1-165.0 s, one back-off), so it is preferred; go2_loco_bank.onnx (r7) is the fallback.
    var bankPolicy = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Policies/go2_loco_steeple.onnx")
                     ?? AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/Policies/go2_loco_bank.onnx");
    if (bankPolicy != null) {
      var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
      foreach (var c in Object.FindObjectsByType<Go2Controller>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { c.locomotionModel = bankPolicy; EditorUtility.SetDirty(c); }
      EditorSceneManager.SaveScene(scene);
    }
    if (!EditorBuildSettings.scenes.Any(sc => sc.path == path)) {
      var list = EditorBuildSettings.scenes.ToList(); int at = list.FindIndex(sc => sc.path == "Assets/Scenes/Track02.unity") + 1;
      list.Insert(at > 0 ? at : list.Count, new EditorBuildSettingsScene(path, true)); EditorBuildSettings.scenes = list.ToArray();
    }
    EditorSceneManager.OpenScene("Assets/Scenes/Testbed.unity", OpenSceneMode.Single);
  }
}

/// <summary>
/// Banked track surface as MuJoCo collision geometry: the bank is cut into small pieces, each a convex prism from the
/// floor up to four surface points, as a mesh geom (MuJoCo collides with the convex hull). Neighbouring pieces share
/// their corner points exactly, so the surface has no steps; on the turns each piece is exactly planar (its corners
/// lie on two generator lines of the bank cone). Drawn by one combined visual mesh. Meshes are saved under
/// Assets/Generated so the player has them (MjMeshShape reads Mesh.vertices at runtime).
/// </summary>
class BankSurface {
  public const float PieceLength = 0.7f;   // along the track
  const int Across = 4;
  const float Bottom = -0.05f;             // prisms reach below the floor plane: a solid embankment, no undercut
  readonly MapDefinition _map;
  readonly List<Vector3[]> _pieces = new List<Vector3[]>();   // top corners in MuJoCo (x, y, z), counter-clockwise from above

  public BankSurface(MapDefinition map) { _map = map; }

  public void AddSegment(int i, float s0, float segLen, Vector2 ci, Vector2 cj, Vector2 mi, Vector2 mj) {
    float W = _map.BankHalfWidth;
    int na = Mathf.Max(1, Mathf.CeilToInt(segLen / PieceLength));
    System.Func<float, float, Vector3> P = (u, v) => {
      var p = Vector2.Lerp(ci + mi * v * W, cj + mj * v * W, u);
      return new Vector3(p.x, p.y, _map.SurfaceHeight(s0 + u * segLen, v * W));
    };
    for (int t = 0; t < na; t++) {
      float u0 = (float)t / na, u1 = (float)(t + 1) / na;
      if (Mathf.Abs(_map.BankAt(s0 + u0 * segLen)) < 1e-3f && Mathf.Abs(_map.BankAt(s0 + u1 * segLen)) < 1e-3f) continue;
      for (int c = 0; c < Across; c++) {
        float v0 = -1f + 2f * c / Across, v1 = -1f + 2f * (c + 1) / Across;   // right to left
        _pieces.Add(new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) });
      }
    }
  }

  static Vector3 U(Vector3 mj) => new Vector3(mj.x, mj.z, mj.y);

  public void Build(string mapAssetPath, Transform mjRoot, Transform visRoot, Material mat) {
    if (_pieces.Count == 0) return;
    if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
    string path = "Assets/Generated/" + System.IO.Path.GetFileNameWithoutExtension(mapAssetPath) + "_bank.asset";
    AssetDatabase.DeleteAsset(path);
    var root = new GameObject("bank"); root.transform.SetParent(mjRoot, false);
    Object container = null;

    // Visual: tops and sides of every prism with flat normals, one mesh.
    var vv = new List<Vector3>(); var vt = new List<int>();
    System.Action<Vector3, Vector3, Vector3, Vector3> Quad = (a, b, c, d) => {   // counter-clockwise from outside, MuJoCo frame
      int k = vv.Count; vv.Add(U(a)); vv.Add(U(b)); vv.Add(U(c)); vv.Add(U(d));
      vt.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });                   // reversed: Unity front faces are clockwise from outside
    };

    for (int k = 0; k < _pieces.Count; k++) {
      var top = _pieces[k];
      var bot = top.Select(p => new Vector3(p.x, p.y, Bottom)).ToArray();
      // Collision mesh: the 8 prism corners, centred on the piece (MuJoCo builds the convex hull from the vertices).
      var all = top.Concat(bot).Select(U).ToArray();
      var centroid = all.Aggregate(Vector3.zero, (s, p) => s + p) / all.Length;
      var mesh = new Mesh { name = "bank_" + k, vertices = all.Select(p => p - centroid).ToArray() };
      mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 7, 6, 5, 7, 5, 4, 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0 };
      mesh.RecalculateBounds();
      if (container == null) { AssetDatabase.CreateAsset(mesh, path); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
      var go = new GameObject(mesh.name); go.transform.SetParent(root.transform, false); go.transform.position = centroid;
      var g = go.AddComponent<MjGeom>(); g.ShapeType = MjShapeComponent.ShapeTypes.Mesh; g.Mesh.Mesh = mesh;
      // Same contact as the floor plane: priority 1 (its friction wins), friction 0.6, condim 3, contype 1 / conaffinity 0.
      var st = g.Settings; st.Priority = 1; st.Friction.Sliding = 0.6f; st.Solver.ConDim = 3; st.Filtering.Contype = 1; st.Filtering.Conaffinity = 0; g.Settings = st;

      Quad(top[0], top[1], top[2], top[3]);
      for (int e = 0; e < 4; e++) { int f = (e + 1) % 4; Quad(top[e], bot[e], bot[f], top[f]); }
    }

    var visual = new Mesh { name = "bank_visual", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
    visual.SetVertices(vv); visual.SetTriangles(vt, 0); visual.RecalculateNormals(); visual.RecalculateBounds();
    AssetDatabase.AddObjectToAsset(visual, container);
    var vgo = new GameObject("bank_visual"); vgo.transform.SetParent(visRoot, false);
    vgo.AddComponent<MeshFilter>().sharedMesh = visual; vgo.AddComponent<MeshRenderer>().sharedMaterial = mat;
    AssetDatabase.SaveAssets();
    Debug.Log($"[TrackBuilder] bank: {_pieces.Count} MuJoCo mesh pieces, max height {_pieces.Max(p => p.Max(q => q.z)):F2} m -> {path}");
  }
}
