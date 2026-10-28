using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Marks a serialized reference that may legitimately stay empty (UiSelfTest skips it).</summary>
[AttributeUsage(AttributeTargets.Field)]
public class OptionalRefAttribute : Attribute { }

/// <summary>C9, UI binding self-test. Only in -raceTest runs (headless included): two frames after every scene load it
/// reflects over the serialized fields (public or [SerializeField]) of the PoRace components and logs each empty one
/// as an error: every reference on the HUD components (Hud, MenuController), UI-typed ones (Graphic, Selectable,
/// RectTransform, Canvas, arrays / lists of them) on all others. Also: Buttons whose persistent onClick lost its
/// target or method, Texts without a font, a scene with Buttons but no EventSystem. [OptionalRef] fields are skipped.
/// On any error the player quits with exit code 3 so the race test fails (-uiSelfTestSoft: log only).</summary>
public class UiSelfTest : MonoBehaviour {
  public static int Errors { get; private set; }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Boot() {
    if (!CliArgs.RaceTest) return;
    var go = new GameObject("UiSelfTest"); DontDestroyOnLoad(go); var t = go.AddComponent<UiSelfTest>();
    SceneManager.sceneLoaded += (s, m) => { if (t != null) t.StartCoroutine(t.Run(s)); };
  }

  IEnumerator Run(Scene scene) {
    yield return null; yield return null;   // after every Start (components may resolve references there)
    int before = Errors, checkedFields = 0;
    foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
      if (mb == null || mb.gameObject.scene != scene) continue;
      var type = mb.GetType(); if (type.Namespace != "PoRace" || mb is UiSelfTest) continue;
      bool hud = mb is Hud || mb is MenuController;
      for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
          if (!Serialized(f) || f.IsDefined(typeof(OptionalRefAttribute), true)) continue;
          var elem = Element(f.FieldType); if (elem == null || !(hud || IsUi(elem))) continue;
          checkedFields++;
          var v = f.GetValue(mb);
          if (v is IList list) { if (list.Count == 0) Fail(mb, f.Name + " is empty"); for (int i = 0; i < list.Count; i++) if (Null(list[i])) Fail(mb, $"{f.Name}[{i}] is null"); }
          else if (Null(v)) Fail(mb, f.Name + " is null");
        }
    }
    var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    foreach (var b in buttons) {
      if (b.gameObject.scene != scene) continue;
      for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
        if (b.onClick.GetPersistentTarget(i) == null || string.IsNullOrEmpty(b.onClick.GetPersistentMethodName(i))) Fail(b, $"onClick[{i}] has no target / method");
    }
    foreach (var t in FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (t.gameObject.scene == scene && t.font == null) Fail(t, "Text without a font");
    if (buttons.Length > 0 && UnityEngine.EventSystems.EventSystem.current == null && FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null) Fail(this, scene.name + " has Buttons but no EventSystem");
    int errs = Errors - before;
    if (errs == 0) { Debug.Log($"[UiSelfTest] {scene.name}: ok ({checkedFields} fields, {buttons.Length} buttons)"); yield break; }
    Debug.LogError($"[UiSelfTest] {scene.name}: FAIL, {errs} broken UI bindings");
    if (!Application.isEditor && !CliArgs.Has("-uiSelfTestSoft")) Application.Quit(3);
  }

  static bool Serialized(FieldInfo f) => !f.IsStatic && !f.IsInitOnly && !f.IsDefined(typeof(NonSerializedAttribute), true)
      && (f.IsPublic || f.IsDefined(typeof(SerializeField), true));

  /// <summary>The UnityEngine.Object type a field holds (itself, or an array / List element type); null for others.</summary>
  static Type Element(Type t) {
    if (t.IsArray) t = t.GetElementType();
    else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>)) t = t.GetGenericArguments()[0];
    return typeof(UnityEngine.Object).IsAssignableFrom(t) ? t : null;
  }

  static bool IsUi(Type t) => typeof(UnityEngine.EventSystems.UIBehaviour).IsAssignableFrom(t) || typeof(RectTransform).IsAssignableFrom(t) || typeof(Canvas).IsAssignableFrom(t);

  static bool Null(object v) => v == null || (v is UnityEngine.Object o && o == null);   // Unity's destroyed / missing objects compare equal to null

  static void Fail(Component c, string what) { Errors++; Debug.LogError($"[UiSelfTest] {Path(c.transform)} ({c.GetType().Name}): {what}"); }

  static string Path(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }
}

}
