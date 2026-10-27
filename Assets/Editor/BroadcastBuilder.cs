using System.IO;
using System.Linq;
using PoRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Adds the broadcast layer (features 1-10) to every race scene as one "Broadcast" object, wired to the
/// scene's orchestrator and chase camera. Idempotent: an existing Broadcast object is replaced. UI is created at
/// runtime by the components themselves, on the HUD canvas.
///   Unity.exe -batchmode -quit -projectPath . -executeMethod BroadcastBuilder.BuildAll</summary>
public static class BroadcastBuilder {
  public static readonly string[] RaceScenes = { "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track02.unity", "Assets/Scenes/Track03.unity", "Assets/Scenes/Track04.unity" };
  const string ParticleMat = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

  /// <summary>Translucent blue URP Lit for the time-trial ghost (a material asset, so the transparent variant ships).</summary>
  static Material GhostMaterial() {
    const string path = "Assets/Materials/Ghost.mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
    m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent");
    m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
    m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
    m.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, 0.35f));
    EditorUtility.SetDirty(m); return m;
  }

  /// <summary>A material asset per custom shader, so the shader ships in player builds (runtime copies are made from it).</summary>
  static Material ShaderMaterial(string path, string shader) {
    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
    return m;
  }

  /// <summary>H.4: the phone keeps its single shadow cascade but only draws shadows to 30 m (was 50).</summary>
  static void MobileShadows() {
    var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
    if (a == null) return;
    a.shadowDistance = 30f; a.shadowCascadeCount = 1; EditorUtility.SetDirty(a);
  }

  /// <summary>H.6 trackside dressing from tools/blender/trackside_props.py: grandstands along the straights, 2.5 m past
  /// the bank edge and behind the boards so the TRACKSIDE camera (rail + 0.6 m) never sits in one (alternating sides on
  /// open tracks, outside the oval on closed ones; none on the first 16 m), each with an arena reverb zone,
  /// and advertising boards every 8 m. Each prop is a LODGroup (LOD0 / LOD1 / culled). Visual only, no colliders.</summary>
  static void Trackside(MapDefinition map) {
    var old = GameObject.Find("Trackside"); if (old != null) Object.DestroyImmediate(old);
    if (map == null) return;
    var stand = Prop("grandstand"); var board = Prop("barrier");
    if (stand.lod0 == null || board.lod0 == null) { Debug.LogWarning("[BroadcastBuilder] trackside props not built (run tools/blender/trackside_props.py)"); return; }
    var root = new GameObject("Trackside"); root.isStatic = true;
    float L = map.Length; Vector2 centroid = Vector2.zero; foreach (var c in map.centerline) centroid += c; centroid /= Mathf.Max(1, map.centerline.Count);
    // Outward lateral direction (+1 = left of travel) at s.
    System.Func<float, Vector2, Vector2, float> side = (s, p, t) => map.closed ? (Vector2.Dot(MapDefinition.Left(t), centroid - p) > 0f ? 1f : -1f) : 1f;
    int stands = 0, boards = 0;
    for (float s = 8f; s < L - 4f; s += 8f) {   // boards from 8 m, stands from 16 m (the start straight stays open for the grid shots)
      var p = map.PointAt(s, out var t);
      map.PointAt(Mathf.Max(0f, s - 3f), out var t0); map.PointAt(Mathf.Min(L, s + 3f), out var t1);
      bool straight = Vector2.Angle(t0, t1) < 8f;
      float sideSign = side(s, p, t);
      if (!map.closed) { Place(board, root.transform, "board", p, t, 1f, map.halfWidth + 1f); Place(board, root.transform, "board", p, t, -1f, map.halfWidth + 1f); boards += 2; }
      else if (straight) { Place(board, root.transform, "board", p, t, -sideSign, map.BankHalfWidth + 1f); boards++; }
      bool standHere = straight && s >= 16f && ((!map.closed && Mathf.Repeat(s, 24f) < 0.1f) || (map.closed && Mathf.Repeat(s, 16f) < 0.1f));
      if (!standHere) continue;
      float sgn = map.closed ? -sideSign : (stands % 2 == 0 ? 1f : -1f);
      var go = Place(stand, root.transform, "stand", p, t, sgn, map.BankHalfWidth + 2.5f);
      var rz = new GameObject("reverb").AddComponent<AudioReverbZone>(); rz.transform.SetParent(go.transform, false); rz.transform.localPosition = new Vector3(0f, 1.5f, 1.2f);
      rz.reverbPreset = AudioReverbPreset.Arena; rz.minDistance = 3f; rz.maxDistance = 9f;
      stands++;
    }
    Debug.Log($"[BroadcastBuilder] trackside: {stands} grandstands, {boards} boards on {map.name}");
  }

  struct PropMeshes { public Mesh lod0, lod1; public Material mat; }

  static PropMeshes Prop(string name) {
    const string dir = "Assets/Art/Exports/GLB/";
    Mesh M(string f) { var g = AssetDatabase.LoadAssetAtPath<GameObject>(dir + f + ".fbx"); var mf = g != null ? g.GetComponentInChildren<MeshFilter>() : null; return mf != null ? mf.sharedMesh : null; }
    string matPath = $"Assets/Materials/Trackside_{name}.mat";
    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
    mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + name + "_albedo.png")); mat.SetFloat("_Smoothness", 0.2f); EditorUtility.SetDirty(mat);
    return new PropMeshes { lod0 = M(name + "_lod0"), lod1 = M(name + "_lod1"), mat = mat };
  }

  /// <summary>Prop at `lateral` m to one side of centreline point p, facing the track (the export maps Blender +Y,
  /// the prop's depth, to Unity -Z, so local +Z points at the track).</summary>
  static GameObject Place(PropMeshes prop, Transform parent, string name, Vector2 p, Vector2 t, float sideSign, float lateral) {
    var left = MapDefinition.Left(t) * sideSign; var at = p + left * lateral;
    var go = new GameObject(name); go.transform.SetParent(parent, false); go.isStatic = true;
    go.transform.SetPositionAndRotation(new Vector3(at.x, 0f, at.y), Quaternion.LookRotation(new Vector3(-left.x, 0f, -left.y)));
    var lods = new LOD[2];
    for (int i = 0; i < 2; i++) {
      var mesh = i == 0 ? prop.lod0 : prop.lod1;
      var child = new GameObject("lod" + i); child.transform.SetParent(go.transform, false); child.isStatic = true;
      child.AddComponent<MeshFilter>().sharedMesh = mesh;
      var r = child.AddComponent<MeshRenderer>(); r.sharedMaterials = Enumerable.Repeat(prop.mat, Mathf.Max(1, mesh.subMeshCount)).ToArray();
      lods[i] = new LOD(i == 0 ? 0.18f : 0.02f, new Renderer[] { r });
    }
    go.AddComponent<LODGroup>().SetLODs(lods);
    return go;
  }

  [MenuItem("PoRace/Rebuild Broadcast Layer")]
  public static void BuildAll() {
    var ghost = GhostMaterial(); var particles = AssetDatabase.LoadAssetAtPath<Material>(ParticleMat);
    var decal = ShaderMaterial("Assets/Materials/Decal.mat", "PoRace/Decal"); var rim = ShaderMaterial("Assets/Materials/StrainRim.mat", "PoRace/StrainRim");
    MobileShadows();
    AssetDatabase.SaveAssets();
    foreach (var path in RaceScenes) {
      if (!File.Exists(path)) continue;
      var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
      var race = Object.FindAnyObjectByType<MultiRaceOrchestrator>(FindObjectsInactive.Include);
      if (race == null) continue;
      var old = GameObject.Find("Broadcast"); if (old != null) Object.DestroyImmediate(old);
      var go = new GameObject("Broadcast");
      var sensors = go.AddComponent<RaceSensors>(); sensors.race = race;
      var contacts = go.AddComponent<ContactImpulse>(); contacts.race = race;
      var strain = go.AddComponent<StrainMonitor>(); strain.race = race; strain.sensors = sensors; strain.contacts = contacts; strain.rimMaterial = rim;
      go.AddComponent<BroadcastTicker>();
      var director = go.AddComponent<RaceDirector>(); director.race = race; director.followCam = race.followCam; director.strain = strain;
      var arrows = go.AddComponent<IntentArrows>(); arrows.race = race; arrows.lineMaterial = particles;
      var chat = go.AddComponent<ChatLink>(); chat.race = race;
      var hazards = go.AddComponent<HazardDesk>(); hazards.race = race; hazards.chat = chat;
      var bets = go.AddComponent<Predictions>(); bets.race = race; bets.chat = chat;
      var weather = go.AddComponent<TrackWeather>(); weather.race = race; weather.rainMaterial = particles;
      var replay = go.AddComponent<RaceReplay>(); replay.race = race; replay.director = director; replay.ghostMaterial = ghost;
      var tele = go.AddComponent<RaceTelemetry>(); tele.race = race; tele.sensors = sensors; tele.strain = strain; tele.contacts = contacts;
      var audio = go.AddComponent<RaceAudio>(); audio.race = race; audio.sensors = sensors; audio.contacts = contacts; audio.director = director; audio.weather = weather;
      var fx = go.AddComponent<ImpactFx>(); fx.race = race; fx.contacts = contacts; fx.director = director; fx.particleMaterial = particles; fx.decalMaterial = decal;
      var lights = go.AddComponent<BroadcastLighting>(); lights.race = race; lights.director = director; lights.decalMaterial = decal;
      var post = go.AddComponent<BroadcastPost>(); post.race = race; post.director = director; post.weather = weather;
      Trackside(race.map);
      EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
      int phys = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include).Length + Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length;
      Debug.Log($"[BroadcastBuilder] {path}: broadcast layer added ({go.GetComponents<MonoBehaviour>().Length} components), PhysX components={phys}");
    }
    EditorSceneManager.OpenScene("Assets/Scenes/Testbed.unity", OpenSceneMode.Single);
  }

  /// <summary>Everything in order, for the command line: maps + steeplechase scene, broadcast layer, menu.</summary>
  public static void BuildEverything() {
    TrackBuilder.BuildSteeple();
    BuildAll();
    MenuBuilder.Build();
    Debug.Log("[BroadcastBuilder] build settings: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
  }
}
