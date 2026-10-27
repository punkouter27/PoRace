using System.Linq;
using PoRace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Point every racer of one scene at a locomotion policy, for A/B race tests with the editor closed:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod PolicyAssign.Assign -scene Assets/Scenes/Track03.unity -policy Assets/Policies/go2_loco_steeple.onnx</summary>
public static class PolicyAssign {
  public static void Assign() {
    var args = System.Environment.GetCommandLineArgs(); string scenePath = null, policyPath = null;
    for (int i = 0; i + 1 < args.Length; i++) { if (args[i] == "-scene") scenePath = args[i + 1]; if (args[i] == "-policy") policyPath = args[i + 1]; }
    var policy = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(policyPath);
    if (scenePath == null || policy == null) { Debug.LogError($"[PolicyAssign] need -scene and a valid -policy ({policyPath})"); EditorApplication.Exit(1); return; }
    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    var cs = Object.FindObjectsByType<Go2Controller>(FindObjectsInactive.Include);
    foreach (var c in cs) { c.locomotionModel = policy; EditorUtility.SetDirty(c); }
    EditorSceneManager.SaveScene(scene);
    Debug.Log($"[PolicyAssign] {scenePath}: {cs.Length} controllers -> {policyPath}");
  }
}
