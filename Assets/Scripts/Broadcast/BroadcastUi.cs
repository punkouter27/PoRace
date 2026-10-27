using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Runtime uGUI helpers for the broadcast layer. Everything is placed on the race HUD canvas (720 x 1280
/// reference, portrait) next to the anchors the scene already uses: TL standings, TC telemetry + race clock,
/// TR mode / Flip / Menu, BL Drop / Cube / Shove / Restart, BR version + build watermark.</summary>
public static class BroadcastUi {
  static Font _font;
  public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
  public static readonly Color Dark = new Color(0.08f, 0.09f, 0.12f, 0.82f);
  public static readonly Color Accent = new Color(0.95f, 0.78f, 0.1f);
  public static readonly Color Live = new Color(0.9f, 0.15f, 0.15f);

  static Canvas _canvas;
  /// <summary>The HUD canvas (the one holding Hud), or a new overlay canvas when a scene has none.</summary>
  public static Transform Root {
    get {
      if (_canvas != null) return _canvas.transform;
      var hud = UnityEngine.Object.FindAnyObjectByType<Hud>();
      _canvas = hud != null ? hud.GetComponentInParent<Canvas>() : null;
      if (_canvas == null) {
        var go = new GameObject("BroadcastCanvas"); _canvas = go.AddComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(720, 1280);
        go.AddComponent<GraphicRaycaster>();
      }
      return _canvas.transform;
    }
  }

  public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size) {
    var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
    var rt = (RectTransform)go.transform; rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
    return rt;
  }

  public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, TextAnchor align, string text = "") {
    var t = Rect(parent, name, anchor, pivot, pos, size).gameObject.AddComponent<Text>();
    t.font = Font; t.fontSize = fontSize; t.color = Color.white; t.alignment = align; t.text = text; t.supportRichText = true;
    t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
    var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.8f); sh.effectDistance = new Vector2(1.5f, -1.5f);
    return t;
  }

  public static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color) {
    var img = Rect(parent, name, anchor, pivot, pos, size).gameObject.AddComponent<Image>(); img.color = color; img.raycastTarget = false;
    return img;
  }

  public static Button Button(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color, int fontSize, Action onClick) {
    var img = Panel(parent, name, anchor, pivot, pos, size, color); img.raycastTarget = true;
    var b = img.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => onClick());
    var t = Label(img.transform, "Label", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, fontSize, TextAnchor.MiddleCenter, label);
    t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.sizeDelta = Vector2.zero;
    return b;
  }

  public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
  public static string Paint(string s, Color c) => $"<color={Hex(c)}>{s}</color>";

  /// <summary>True for headless runs (-batchmode / -nographics): broadcast visuals and audio stay off, physics untouched.</summary>
  public static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
}

/// <summary>Lower-third ticker (right of the BL buttons): the last three broadcast messages, each shown for a few
/// seconds. Any component posts with BroadcastTicker.Post. Headless runs print to the log instead.</summary>
public class BroadcastTicker : MonoBehaviour {
  static BroadcastTicker _i;
  struct Line { public string text; public float until; }
  readonly List<Line> _lines = new List<Line>();
  Text _text; Image _bg;

  public static void Post(string text, float seconds = 5f) {
    if (BroadcastUi.Headless || _i == null) { Debug.Log("[Ticker] " + StripTags(text)); return; }
    _i._lines.Add(new Line { text = text, until = Time.unscaledTime + seconds });
    if (_i._lines.Count > 3) _i._lines.RemoveAt(0);
  }

  static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

  void Awake() { _i = this; }
  void OnDestroy() { if (_i == this) _i = null; }

  void Start() {
    if (BroadcastUi.Headless) return;
    var race = FindAnyObjectByType<MultiRaceOrchestrator>();
    if (race != null && race.raceText != null) {
      var rt = race.raceText.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
      rt.anchoredPosition = new Vector2(0f, -300f);
    }
    _bg = BroadcastUi.Panel(BroadcastUi.Root, "Ticker", Vector2.zero, Vector2.zero, new Vector2(180, 64), new Vector2(524, 132), BroadcastUi.Dark);
    _text = BroadcastUi.Label(_bg.transform, "TickerText", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(500, 124), 22, TextAnchor.LowerLeft);
  }

  void Update() {
    if (_text == null) return;
    _lines.RemoveAll(l => l.until < Time.unscaledTime);
    _bg.enabled = _lines.Count > 0;
    var sb = new System.Text.StringBuilder();
    foreach (var l in _lines) sb.Append(l.text).Append('\n');
    _text.text = sb.ToString().TrimEnd('\n');
  }
}

}
