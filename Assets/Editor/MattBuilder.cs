using System.Linq;
using Mujoco;
using PoRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Editor-time authoring of Matt as a racer (tasks E.4), also headless:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod MattBuilder.BuildCli
/// - Assets/Scenes/MattTestbed.unity: Matt alone on the trainer floor (matt_testbed.xml through the MuJoCo importer)
///   with MattController, ParityRecorder and ReplayHarness: zero-brain, replay and closed-loop parity gates.
/// - Flat race scenes (Track01x4, Track02): Matt's body (matt_unity.xml, robot only) imported into the race's single
///   MuJoCo model next to the dogs, his MattController + AutoReset, the skinned scan driven by MattSkin, and a Racer
///   entry. The orchestrator puts him on the grid only when picked (RaceConfig.matt / -matt). Rerunnable: a previous
///   Matt is removed first. The MuJoCo primitives stay invisible; the scan is what the camera sees.</summary>
public static class MattBuilder {
  const string UnityXml = "Assets/MuJoCo/matt/matt_unity.xml", TestbedXml = "Assets/MuJoCo/matt/matt_testbed.xml";
  const string PolicyPath = "Assets/Policies/matt_loco.onnx", PrefabPath = "Assets/Art/Prefabs/Matt.prefab";
  static readonly string[] RaceScenes = { "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track02.unity" };
  static readonly Color RosterColor = new Color(1f, 0.62f, 0.18f);   // readable on the dark standings panel

  static Unity.InferenceEngine.ModelAsset Policy => AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(PolicyPath);

  [MenuItem("PoRace/Build Matt Testbed")]
  public static void BuildTestbed() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    var root = new MjImporterWithAssets().ImportFile(System.IO.Path.GetFullPath(TestbedXml));
    root.name = "matt_scene";
    var gs = Object.FindFirstObjectByType<MjGlobalSettings>();
    gs.UseRawGameObjectNames = true; EditorUtility.SetDirty(gs);

    var rig = new GameObject("MattRig");
    var ctrl = rig.AddComponent<MattController>(); ctrl.alone = true; ctrl.mode = MattController.Mode.HoldPose; ctrl.locomotionModel = Policy;
    var rec = rig.AddComponent<ParityRecorder>(); rec.controller = ctrl; rec.seconds = 1f; rec.fileName = "unity_matt_zero_brain.json"; rec.quitWhenDone = true;
    var rep = rig.AddComponent<ReplayHarness>(); rep.model = Policy; rep.obsDim = MattSpec.ObsDim; rep.actDim = MattSpec.Nu;
    AddSkin(rig, ctrl);

    var cam = new GameObject("Main Camera"); cam.tag = "MainCamera"; cam.AddComponent<Camera>();
    cam.transform.position = new Vector3(3.2f, 1.6f, -3.2f); cam.transform.LookAt(new Vector3(0f, 0.9f, 0f));
    var light = new GameObject("Directional Light"); var l = light.AddComponent<Light>(); l.type = LightType.Directional;
    light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/MattTestbed.unity");
    Debug.Log($"[MattBuilder] MattTestbed.unity: {Census(root)}; policy {(Policy != null ? "set" : "none yet")}");
  }

  [MenuItem("PoRace/Add Matt To Flat Tracks")]
  public static void AddToRaces() {
    foreach (var path in RaceScenes) {
      var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
      var race = Object.FindFirstObjectByType<MultiRaceOrchestrator>(FindObjectsInactive.Include);
      foreach (var old in new[] { "matt_racer", "MattRacer", "MattVisual" }) { var g = GameObject.Find(old); if (g != null) Object.DestroyImmediate(g); }
      race.racers.RemoveAll(r => r.matt != null || (r.controller == null && r.name == "Matt"));

      var root = new MjImporterWithAssets().ImportFile(System.IO.Path.GetFullPath(UnityXml));
      root.name = "matt_racer";
      foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;   // collision primitives: physics only

      var go = new GameObject("MattRacer");
      var ctrl = go.AddComponent<MattController>(); ctrl.locomotionModel = Policy;
      ctrl.mode = Policy != null ? MattController.Mode.Locomotion : MattController.Mode.HoldPose;
      var dogReset = race.racers.Select(r => r.autoReset).FirstOrDefault(a => a != null);
      var ar = go.AddComponent<AutoReset>(); ar.matt = ctrl;
      ar.fallenSeconds = 1.5f;   // no getup policy: a fall goes straight back to the last checkpoint
      if (dogReset != null) { ar.stallSeconds = dogReset.stallSeconds; ar.stallSpeed = dogReset.stallSpeed; ar.arenaRadius = dogReset.arenaRadius; }
      AddSkin(go, ctrl);
      race.racers.Add(new Racer { name = "Matt", matt = ctrl, autoReset = ar, mjRoot = root, cruiseSpeed = 1.5f, color = RosterColor });
      EditorUtility.SetDirty(race);

      int phys = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length +
                 Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
      EditorSceneManager.SaveScene(scene);
      Debug.Log($"[MattBuilder] {path}: {Census(root)}; racers {race.racers.Count} ({string.Join(", ", race.racers.Select(r => r.name))}); " +
                $"policy {(Policy != null ? "set" : "none yet (HoldPose)")}; PhysX components={phys}");
    }
  }

  public static void BuildCli() {
    BuildTestbed();
    AddToRaces();
    EditorApplication.Exit(0);
  }

  /// <summary>The skinned scan as a separate visual root, posed every frame by MattSkin from the MuJoCo bodies.</summary>
  static void AddSkin(GameObject owner, MattController ctrl) {
    var vis = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
    vis.name = "MattVisual";
    owner.AddComponent<MattSkin>().visual = vis.transform;
    var look = vis.GetComponent<MattLook>(); if (look != null) look.controller = ctrl;   // wear and effort of this racer
  }

  static string Census(GameObject root) =>
    $"MjBody={root.GetComponentsInChildren<MjBody>(true).Length} MjActuator={Object.FindObjectsByType<MjActuator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length} " +
    $"MjExclude={root.GetComponentsInChildren<MjExclude>(true).Length} MjGeom={root.GetComponentsInChildren<MjGeom>(true).Length}";
}
