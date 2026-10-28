using System;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>C6, orientation lock + landscape fallback. Phones (screen diagonal under 7 in) lock to portrait, the
/// primary layout. Tablets / desktop / browser may be landscape: the HUD canvas then matches height instead of width
/// (the 1280-unit tall layout keeps its size and the screen just gets wider) and Changed(true) tells the chips to
/// become a left column and the pause sheet to open on the right. Checked once per frame, no allocation.</summary>
public class UiLayout : MonoBehaviour {
  public float phoneMaxInches = 7f;
  public static event Action<bool> Changed;
  public static bool IsLandscape { get; private set; }
  int _w = -1, _h = -1; bool _applied;

  void Start() {
    if (BroadcastUi.Headless) { enabled = false; return; }
    if (Application.isMobilePlatform && IsPhone()) {
      Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = false;
      Screen.autorotateToPortrait = true; Screen.autorotateToPortraitUpsideDown = false;
      Screen.orientation = ScreenOrientation.Portrait;
    } else if (Application.isMobilePlatform) {
      Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = Screen.autorotateToPortrait = true;
      Screen.orientation = ScreenOrientation.AutoRotation;
    }
    Apply();
  }

  bool IsPhone() {
    float dpi = Screen.dpi; if (dpi <= 0f) return true;   // unknown: assume the small screen
    float w = Screen.width / dpi, h = Screen.height / dpi;
    return Mathf.Sqrt(w * w + h * h) < phoneMaxInches;
  }

  void Update() { if (Screen.width != _w || Screen.height != _h) Apply(); }

  void Apply() {
    _w = Screen.width; _h = Screen.height;
    bool land = BroadcastUi.Landscape;
    var scaler = BroadcastUi.Root.GetComponent<CanvasScaler>();
    if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) scaler.matchWidthOrHeight = land ? 1f : 0f;
    if (_applied && land == IsLandscape) return;
    _applied = true; IsLandscape = land; Changed?.Invoke(land);
  }
}

}
