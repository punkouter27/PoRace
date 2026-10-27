using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Standalone players failed every frame with "Couldn't find the required resources for the
/// ScreenSpaceAmbientOcclusion render feature" (then "Blitter is already initialized") and drew nothing. The PC renderer
/// was the only one carrying SSAO (the Mobile renderer, which the phone uses, has no features), so it is removed here.
///   Unity.exe -batchmode -quit -projectPath . -executeMethod RenderFix.RemoveSsao</summary>
public static class RenderFix {
  [MenuItem("PoRace/Fix: Remove SSAO From PC Renderer")]
  public static void RemoveSsao() {
    const string path = "Assets/Settings/PC_Renderer.asset";
    var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
    var ssao = data.rendererFeatures.Where(f => f is ScreenSpaceAmbientOcclusion).ToList();
    foreach (var f in ssao) { data.rendererFeatures.Remove(f); AssetDatabase.RemoveObjectFromAsset(f); Object.DestroyImmediate(f, true); }
    var so = new SerializedObject(data); so.FindProperty("m_RendererFeatureMap").ClearArray(); so.ApplyModifiedPropertiesWithoutUndo();
    data.SetDirty(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
    Debug.Log($"[RenderFix] removed {ssao.Count} SSAO feature(s); {data.rendererFeatures.Count} feature(s) left on {path}");
  }
}
