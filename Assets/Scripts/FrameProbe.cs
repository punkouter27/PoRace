using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PoRace {

/// <summary>H.7 whole-frame cost (sim + inference + rendering + effects), for checking presentation changes against
/// the 16.7 ms (60 fps) budget. Only exists when a player is started with -frameProbe &lt;seconds&gt; [-frameWarmup 15]:
/// turns vsync off, samples unscaled frame times after the warm-up (scene load, betting countdown), logs mean / p95 /
/// max and quits. Combine with -gfx mobile to measure the phone tier on a desktop.</summary>
public class FrameProbe : MonoBehaviour {
  float _seconds, _warmup = 15f, _t0 = -1f;
  readonly List<float> _ms = new List<float>(4096);

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    var args = System.Environment.GetCommandLineArgs(); float seconds = -1f, warmup = 15f;
    for (int i = 0; i + 1 < args.Length; i++) {
      if (args[i] == "-frameProbe") seconds = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
      if (args[i] == "-frameWarmup") warmup = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
    }
    if (seconds <= 0f) return;
    QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
    var go = new GameObject("FrameProbe"); DontDestroyOnLoad(go);
    var p = go.AddComponent<FrameProbe>(); p._seconds = seconds; p._warmup = warmup;
  }

  void Update() {
    float now = Time.realtimeSinceStartup;
    if (Application.targetFrameRate != -1) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }   // Hud caps at 60 on scene start
    if (now < _warmup) return;
    if (_t0 < 0f) { _t0 = now; return; }
    _ms.Add(Time.unscaledDeltaTime * 1000f);
    if (now - _t0 < _seconds) return;
    _ms.Sort(); float sum = 0f; foreach (var m in _ms) sum += m;
    Debug.Log(string.Format(CultureInfo.InvariantCulture, "[FrameProbe] tier {0} quality {1}: {2} frames, mean {3:F2} ms ({4:F0} fps), p95 {5:F2} ms, max {6:F2} ms",
      GfxTier.Mobile ? "Mobile" : "PC", QualitySettings.names[QualitySettings.GetQualityLevel()], _ms.Count, sum / _ms.Count, 1000f * _ms.Count / sum,
      _ms[(int)(_ms.Count * 0.95f)], _ms[_ms.Count - 1]));
    enabled = false; Application.Quit(0);
  }
}

}
