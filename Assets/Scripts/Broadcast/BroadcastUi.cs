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

  /// <summary>A reference BroadcastBuilder wires: true when set; otherwise logs one error and disables `self` (the
  /// caller returns). No silent FindAnyObjectByType fallback, so a broken build fails loudly.</summary>
  public static bool Wired(MonoBehaviour self, UnityEngine.Object o, string field) {
    if (o != null) return true;
    Debug.LogError($"[{self.GetType().Name}] {field} not wired (BroadcastBuilder)"); self.enabled = false;
    return false;
  }

  /// <summary>Wider than tall (tablet / desktop landscape): UiLayout switches the canvas to match height.</summary>
  public static bool Landscape => Screen.width > Screen.height;

  static Sprite _disc, _ring;
  /// <summary>Anti-aliased white disc (Image.Type.Filled radial works on it).</summary>
  public static Sprite Disc => _disc != null ? _disc : (_disc = Circle(0f));
  /// <summary>Anti-aliased white ring, 14 % of the radius thick: cooldown rings and coach-mark highlights.</summary>
  public static Sprite Ring => _ring != null ? _ring : (_ring = Circle(0.72f));

  static Sprite Circle(float inner) {
    const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
    var px = new Color32[n * n]; float r = n * 0.5f - 1f, edge = 1.5f;
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
      float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
      float a = Mathf.Clamp01((r - d) / edge); if (inner > 0f) a *= Mathf.Clamp01((d - r * inner) / edge);
      px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
    }
    tex.SetPixels32(px); tex.Apply(false, true);
    return Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
  }

  /// <summary>Image using the disc / ring sprite; `fill` makes it a radial 360 fill from the top (cooldown ring).</summary>
  public static Image Round(Transform parent, string name, Vector2 anchor, Vector2 pos, float diameter, Color color, bool ring = false, bool fill = false) {
    var img = Panel(parent, name, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(diameter, diameter), color);
    img.sprite = ring ? Ring : Disc;
    if (fill) { img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = (int)Image.Origin360.Top; img.fillClockwise = false; }
    return img;
  }

  /// <summary>Stretch a RectTransform over its parent.</summary>
  public static RectTransform Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero; return rt; }

  /// <summary>Position of a RectTransform's centre in Root-local units (coach marks point at it whatever its anchors).</summary>
  public static Vector2 CentreInRoot(RectTransform rt) {
    var root = (RectTransform)Root; var c = rt.TransformPoint(rt.rect.center);
    return root.InverseTransformPoint(c);
  }

  /// <summary>"+0.0s" .. "+99.9s" gap strings, built once (standings chips rebuild text with no allocation).</summary>
  static readonly string[] _gaps = new string[1000];
  public static string Gap(float seconds) {
    int k = Mathf.Clamp(Mathf.RoundToInt(seconds * 10f), 0, _gaps.Length - 1);
    return _gaps[k] ?? (_gaps[k] = "+" + (k / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s");
  }
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
    _i._dirty = true;
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
    _dirty = true;
  }

  readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(); bool _dirty;

  void Update() {
    if (_text == null) return;
    bool dirty = _dirty; _dirty = false;   // text is rebuilt only when a line arrives or expires
    for (int i = _lines.Count - 1; i >= 0; i--) if (_lines[i].until < Time.unscaledTime) { _lines.RemoveAt(i); dirty = true; }
    if (!dirty) return;
    _bg.enabled = _lines.Count > 0;
    _sb.Clear();
    for (int i = 0; i < _lines.Count; i++) { if (i > 0) _sb.Append('\n'); _sb.Append(_lines[i].text); }
    _text.text = _sb.ToString();
  }
}

}
