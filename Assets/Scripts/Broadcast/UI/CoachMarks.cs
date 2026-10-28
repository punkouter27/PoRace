using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

/// <summary>C7, first-run coach marks: the three gestures, once. A second after the first race scene opens, a dim
/// overlay rings the hazard hub, the standings chips and the pause button, each with a one-line caption. Any tap (after
/// half a second) or 6 s dismisses it, and PlayerPrefs remembers. -coach shows it again (screenshots).</summary>
public class CoachMarks : MonoBehaviour {
  public float delay = 1f, duration = 6f;
  const string Key = "PoRace.coachSeen";
  Image _dim; RectTransform[] _rings; float _start, _shownAt = -1f; bool _done;

  void Start() {
    bool force = CliArgs.Has("-coach");
    if (BroadcastUi.Headless || (!force && PlayerPrefs.GetInt(Key, 0) == 1)) { enabled = false; return; }
    _start = Time.unscaledTime;
  }

  void Show() {
    var radial = FindAnyObjectByType<HazardRadial>(); var chips = FindAnyObjectByType<StandingsChips>(); var pause = FindAnyObjectByType<PauseSheet>();
    var targets = new[] {
      (radial != null ? radial.Hub : null, "Hold & slide: drop, throw or shove"),
      (chips != null ? chips.Row : null, "Tap a racer to back them"),
      (pause != null ? pause.PauseButton : null, "Pause · replay · restart"),
    };
    var root = (RectTransform)BroadcastUi.Root;
    _dim = BroadcastUi.Panel(root, "CoachMarks", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
    BroadcastUi.Fill(_dim.rectTransform); _dim.raycastTarget = true;   // swallows the dismissing tap
    _dim.transform.SetAsLastSibling();
    _rings = new RectTransform[targets.Length];
    var half = root.rect.size * 0.5f;
    for (int i = 0; i < targets.Length; i++) {
      var (t, caption) = targets[i]; if (t == null) continue;
      var c = BroadcastUi.CentreInRoot(t); var size = t.rect.size + new Vector2(28f, 28f); float d = size.y;
      _rings[i] = BroadcastUi.Round(_dim.transform, "Ring" + i, new Vector2(0.5f, 0.5f), c, d, BroadcastUi.Accent, true).rectTransform;
      _rings[i].sizeDelta = size;   // an ellipse round wide targets (the chip row)
      // caption below the target (above it in the lower half), kept on screen
      float y = c.y > 0f ? c.y - d * 0.5f - 34f : c.y + d * 0.5f + 34f, x = Mathf.Clamp(c.x, -half.x + 210f, half.x - 210f);
      var bg = BroadcastUi.Panel(_dim.transform, "Caption" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(400f, 64f), BroadcastUi.Dark);
      BroadcastUi.Label(bg.transform, "Text", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(390f, 60f), 24, TextAnchor.MiddleCenter, caption);
    }
    _shownAt = Time.unscaledTime;
  }

  void Update() {
    if (_done) return;
    float now = Time.unscaledTime;
    if (_shownAt < 0f) { if (now - _start >= delay) Show(); return; }
    float pulse = 1f + 0.06f * Mathf.Sin((now - _shownAt) * 6f);
    foreach (var r in _rings) if (r != null) r.localScale = new Vector3(pulse, pulse, 1f);
    bool tap = Pointer.current != null && Pointer.current.press.wasPressedThisFrame && now - _shownAt > 0.5f;
    if (!tap && now - _shownAt < duration) return;
    _done = true; Destroy(_dim.gameObject);
    PlayerPrefs.SetInt(Key, 1); PlayerPrefs.Save();
  }
}

}
