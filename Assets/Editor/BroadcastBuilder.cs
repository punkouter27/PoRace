using System.IO;
using System.Linq;
using PoRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

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

  [MenuItem("PoRace/Rebuild Broadcast Layer")]
  public static void BuildAll() {
    var ghost = GhostMaterial(); var particles = AssetDatabase.LoadAssetAtPath<Material>(ParticleMat);
    AssetDatabase.SaveAssets();
    foreach (var path in RaceScenes) {
      if (!File.Exists(path)) continue;
      var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
      var race = Object.FindAnyObjectByType<MultiRaceOrchestrator>(FindObjectsInactive.Include);
      if (race == null) continue;
      var old = GameObject.Find("Broadcast"); if (old != null) Object.DestroyImmediate(old);
      var go = new GameObject("Broadcast");
      var sensors = go.AddComponent<RaceSensors>(); sensors.race = race;
      var strain = go.AddComponent<StrainMonitor>(); strain.race = race; strain.sensors = sensors;
      go.AddComponent<BroadcastTicker>();
      var director = go.AddComponent<RaceDirector>(); director.race = race; director.followCam = race.followCam; director.strain = strain;
      var arrows = go.AddComponent<IntentArrows>(); arrows.race = race; arrows.lineMaterial = particles;
      var chat = go.AddComponent<ChatLink>(); chat.race = race;
      var hazards = go.AddComponent<HazardDesk>(); hazards.race = race; hazards.chat = chat;
      var bets = go.AddComponent<Predictions>(); bets.race = race; bets.chat = chat;
      var weather = go.AddComponent<TrackWeather>(); weather.race = race; weather.rainMaterial = particles;
      var replay = go.AddComponent<RaceReplay>(); replay.race = race; replay.director = director; replay.ghostMaterial = ghost;
      var tele = go.AddComponent<RaceTelemetry>(); tele.race = race; tele.sensors = sensors; tele.strain = strain;
      var audio = go.AddComponent<RaceAudio>(); audio.race = race; audio.sensors = sensors; audio.director = director; audio.particleMaterial = particles;
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
