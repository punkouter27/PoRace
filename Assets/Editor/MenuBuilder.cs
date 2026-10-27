using System.Linq;
using PoRace;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Editor-time authoring of the pre-race menu scene and the "Menu" button on the race HUDs. UI only.</summary>
public static class MenuBuilder {
  static Font F => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
  static readonly Color Dark = new Color(0.15f, 0.15f, 0.2f, 0.9f);
  static readonly Vector2 C = new Vector2(0.5f, 0.5f);

  static Text T(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, int fs, string text = null) {
    var go = new GameObject(name); go.transform.SetParent(parent, false);
    var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = C; rt.anchoredPosition = pos; rt.sizeDelta = size;
    var t = go.AddComponent<Text>(); t.font = F; t.fontSize = fs; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter; t.text = text ?? name;
    t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; return t;
  }

  static Button B(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, UnityAction act, Color col, int fs = 26) {
    var go = new GameObject(name); go.transform.SetParent(parent, false);
    var rt = go.AddComponent<RectTransform>(); rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
    go.AddComponent<Image>().color = col; var b = go.AddComponent<Button>();
    var tgo = new GameObject("Label"); tgo.transform.SetParent(go.transform, false);
    var trt = tgo.AddComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
    var t = tgo.AddComponent<Text>(); t.font = F; t.fontSize = fs; t.color = Color.white; t.alignment = TextAnchor.MiddleCenter; t.text = label;
    UnityEventTools.AddPersistentListener(b.onClick, act); return b;
  }

  static InputField Input(Transform parent, string name, Vector2 pos, Vector2 size, string placeholder) {
    var go = new GameObject(name); go.transform.SetParent(parent, false);
    var rt = go.AddComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = C; rt.pivot = C; rt.anchoredPosition = pos; rt.sizeDelta = size;
    go.AddComponent<Image>().color = Dark;
    Text Child(string n, string s, Color col) {
      var c = new GameObject(n); c.transform.SetParent(go.transform, false);
      var crt = c.AddComponent<RectTransform>(); crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = new Vector2(16, 6); crt.offsetMax = new Vector2(-16, -6);
      var t = c.AddComponent<Text>(); t.font = F; t.fontSize = 28; t.color = col; t.alignment = TextAnchor.MiddleCenter; t.text = s; t.supportRichText = false; return t;
    }
    var ph = Child("Placeholder", placeholder, new Color(1f, 1f, 1f, 0.35f)); var txt = Child("Text", "", Color.white);
    var input = go.AddComponent<InputField>(); input.textComponent = txt; input.placeholder = ph; input.characterLimit = 25;
    return input;
  }

  [MenuItem("PoRace/Rebuild Menu")]
  public static void Build() {
    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
    var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.09f, 0.10f, 0.14f);
    var canvasGo = new GameObject("MenuCanvas"); var canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    var sc = canvasGo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(720, 1280);
    canvasGo.AddComponent<GraphicRaycaster>();
    var es = new GameObject("EventSystem"); es.AddComponent<UnityEngine.EventSystems.EventSystem>(); es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    var mc = canvasGo.AddComponent<MenuController>(); var tr = canvasGo.transform;

    T(tr, "Title", new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(600, 80), 64, "PoRace");
    T(tr, "TrackLabel", C, new Vector2(0, 410), new Vector2(400, 40), 24, "TRACK");
    mc.mapText = T(tr, "MapName", C, new Vector2(0, 345), new Vector2(420, 70), 44);
    mc.infoText = T(tr, "MapInfo", C, new Vector2(0, 265), new Vector2(600, 80), 24);
    B(tr, "BtnPrevMap", "<", C, C, new Vector2(-290, 345), new Vector2(80, 80), mc.OnPrevMap, Dark);
    B(tr, "BtnNextMap", ">", C, C, new Vector2(290, 345), new Vector2(80, 80), mc.OnNextMap, Dark);
    T(tr, "RacersLabel", C, new Vector2(0, 165), new Vector2(400, 40), 24, "RACERS");
    mc.racersText = T(tr, "Racers", C, new Vector2(0, 100), new Vector2(200, 70), 48);
    B(tr, "BtnRacersDown", "-", C, C, new Vector2(-150, 100), new Vector2(80, 80), mc.OnRacersDown, Dark);
    B(tr, "BtnRacersUp", "+", C, C, new Vector2(150, 100), new Vector2(80, 80), mc.OnRacersUp, Dark);
    T(tr, "LapsLabel", C, new Vector2(0, 15), new Vector2(400, 40), 24, "LAPS");
    mc.lapsText = T(tr, "Laps", C, new Vector2(0, -50), new Vector2(200, 70), 48);
    B(tr, "BtnLapsDown", "-", C, C, new Vector2(-150, -50), new Vector2(80, 80), mc.OnLapsDown, Dark);
    B(tr, "BtnLapsUp", "+", C, C, new Vector2(150, -50), new Vector2(80, 80), mc.OnLapsUp, Dark);
    T(tr, "WeatherLabel", C, new Vector2(0, -135), new Vector2(400, 40), 24, "TRACK WEATHER");
    mc.weatherText = T(tr, "Weather", C, new Vector2(0, -190), new Vector2(360, 60), 32);
    B(tr, "BtnWeatherPrev", "<", C, C, new Vector2(-230, -190), new Vector2(80, 70), mc.OnWeatherPrev, Dark);
    B(tr, "BtnWeatherNext", ">", C, C, new Vector2(230, -190), new Vector2(80, 70), mc.OnWeatherNext, Dark);
    T(tr, "ChatLabel", C, new Vector2(0, -270), new Vector2(500, 40), 24, "TWITCH CHAT (optional)");
    mc.chatInput = Input(tr, "ChatChannel", new Vector2(0, -325), new Vector2(460, 64), "channel name");
    B(tr, "BtnStart", "START RACE", C, C, new Vector2(0, -470), new Vector2(420, 110), mc.OnLaunch, new Color(0.15f, 0.55f, 0.25f, 1f), 32);
    T(tr, "Version", new Vector2(1f, 0f), new Vector2(-110, 30), new Vector2(200, 30), 20, "v" + PlayerSettings.bundleVersion).gameObject.AddComponent<Watermark>();
    EditorSceneManager.SaveScene(scene, "Assets/Scenes/Menu.unity");

    foreach (var path in new[] { "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track02.unity", "Assets/Scenes/Track03.unity", "Assets/Scenes/Track04.unity" }) {
      if (!System.IO.File.Exists(path)) continue;
      var s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
      var hud = Object.FindFirstObjectByType<Hud>();
      var old = hud.transform.Find("BtnMenu"); if (old != null) Object.DestroyImmediate(old.gameObject);
      B(hud.transform, "BtnMenu", "Menu", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -154), new Vector2(130, 56), hud.OnMenu, Dark, 22);
      EditorSceneManager.SaveScene(s);
    }
    EditorBuildSettings.scenes = new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/Track01x4.unity", "Assets/Scenes/Track02.unity", "Assets/Scenes/Track03.unity", "Assets/Scenes/Track04.unity", "Assets/Scenes/Testbed.unity" }
        .Where(System.IO.File.Exists).Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
    EditorSceneManager.OpenScene("Assets/Scenes/Testbed.unity", OpenSceneMode.Single);
    Debug.Log("[MenuBuilder] Menu.unity saved; maps discovered=" + MapRegistry.All().Length);
  }
}
