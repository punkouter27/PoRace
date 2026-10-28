using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Screenshots for UI change reports (SCREENSHOTS/). Only exists when the player is started with
/// -shots &lt;dir&gt; [-shotTimes 0.5,12,40] [-shotPrefix name]; survives scene loads (menu -> race) and quits after the last shot.
/// Times are real seconds since launch (instant replay freezes game time). Needs a graphics device (not -nographics).
/// C3, text scale check: at every shot each visible legacy Text is measured as it would render on three reference
/// phones (720 x 1280 @ 320 dpi, 1080 x 2340 @ 420 dpi, 1440 x 3120 @ 560 dpi, through the canvas' CanvasScaler).
/// A label under 11 pt (Apple HIG minimum; pt = 1/160 in, the dp) on any of them, or whose text overflows its
/// RectTransform, is logged as a [TextScale] warning once; each shot ends with a summary line.</summary>
public class ShotProbe : MonoBehaviour {
  string _dir, _prefix = "shot"; float[] _times = { 1f }; int _next;
  public const float MinPoints = 11f;
  static readonly (string name, int w, int h, float dpi)[] Phones = { ("720p@320dpi", 720, 1280, 320f), ("1080p@420dpi", 1080, 2340, 420f), ("1440p@560dpi", 1440, 3120, 560f) };
  readonly HashSet<string> _reported = new HashSet<string>();

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    if (!CliArgs.TryString("-shots", out var dir)) return;
    var go = new GameObject("ShotProbe"); DontDestroyOnLoad(go);
    var p = go.AddComponent<ShotProbe>(); p._dir = dir;
    if (CliArgs.TryString("-shotTimes", out var st)) p._times = System.Array.ConvertAll(st.Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
    if (CliArgs.TryString("-shotPrefix", out var sp)) p._prefix = sp;
    Directory.CreateDirectory(dir);
  }

  void Update() {
    if (_next >= _times.Length) return;
    if (Time.unscaledTime < _times[_next]) return;
    string path = Path.Combine(_dir, $"{_prefix}_{_next:D2}_{_times[_next]:0.#}s.png".Replace(',', '.'));
    ScreenCapture.CaptureScreenshot(path);
    Debug.Log("[ShotProbe] " + path);
    CheckText(_next);
    if (++_next >= _times.Length) StartCoroutine(Quit());   // the capture is written at the end of the frame
  }

  System.Collections.IEnumerator Quit() { yield return new WaitForSecondsRealtime(1f); Application.Quit(0); }

  /// <summary>C3: every active, enabled, non-empty Text under a root canvas: smallest physical size over the
  /// reference phones, and overflow of its own rect (wrapped height, or width when it does not wrap).</summary>
  void CheckText(int shot) {
    int labels = 0, small = 0, over = 0;
    foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None)) {
      if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.canvas == null) continue;
      var canvas = t.canvas.rootCanvas; labels++;
      // font size in canvas units (best fit: the size it settled on), times any scale between the label and the canvas
      float units = t.resizeTextForBestFit ? t.cachedTextGenerator.fontSizeUsedForBestFit / Mathf.Max(1e-4f, t.pixelsPerUnit) : t.fontSize;
      units *= t.rectTransform.lossyScale.y / Mathf.Max(1e-6f, canvas.transform.lossyScale.y);
      var scaler = canvas.GetComponent<CanvasScaler>();
      float minPt = float.MaxValue; string where = "";
      foreach (var p in Phones) {
        float pt = units * ScaleFactor(scaler, canvas, p.w, p.h) / p.dpi * 160f;
        if (pt < minPt) { minPt = pt; where = p.name; }
      }
      var r = t.rectTransform.rect;
      bool overH = !t.resizeTextForBestFit && t.preferredHeight > r.height + 1f;
      bool overW = !t.resizeTextForBestFit && t.horizontalOverflow == HorizontalWrapMode.Overflow && t.preferredWidth > r.width + 1f;
      if (minPt < MinPoints) { small++; Report($"small|{NodePath(t.transform)}", $"'{NodePath(t.transform)}' {units:F0} units -> {minPt:F1} pt on {where} (< {MinPoints} pt)"); }
      if (overH || overW) { over++; Report($"over|{NodePath(t.transform)}", $"'{NodePath(t.transform)}' overflows its rect {r.width:F0}x{r.height:F0}: text needs {t.preferredWidth:F0}x{t.preferredHeight:F0} \"{Clip(t.text)}\""); }
    }
    Debug.Log($"[TextScale] shot {shot:D2}: {labels} labels, {small} under {MinPoints} pt, {over} overflowing");
  }

  /// <summary>CanvasScaler's scale factor for a w x h screen (ScaleWithScreenSize match-width-or-height, else as set).</summary>
  static float ScaleFactor(CanvasScaler s, Canvas c, int w, int h) {
    if (s == null) return c.scaleFactor;   // no scaler: constant pixel size, physical size shrinks as dpi grows
    switch (s.uiScaleMode) {
      case CanvasScaler.ScaleMode.ScaleWithScreenSize:
        if (s.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight) {
          float kx = w / s.referenceResolution.x, ky = h / s.referenceResolution.y;
          return s.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(kx, ky) : Mathf.Max(kx, ky);
        }
        float lw = Mathf.Log(w / s.referenceResolution.x, 2f), lh = Mathf.Log(h / s.referenceResolution.y, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, s.matchWidthOrHeight));
      default: return s.scaleFactor;
    }
  }

  void Report(string key, string msg) { if (_reported.Add(key)) Debug.LogWarning("[TextScale] " + msg); }

  static string NodePath(Transform t) { var s = t.name; for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s; return s; }
  static string Clip(string s) { s = s.Replace("\n", " / "); return s.Length > 40 ? s.Substring(0, 40) + "..." : s; }
}

}
