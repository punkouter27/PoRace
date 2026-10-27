using System.Globalization;
using System.IO;
using UnityEngine;

namespace PoRace {

/// <summary>Screenshots for UI change reports (SCREENSHOTS/). Only exists when the player is started with
/// -shots &lt;dir&gt; [-shotTimes 0.5,12,40] [-shotPrefix name]; survives scene loads (menu -> race) and quits after the last shot.
/// Times are real seconds since launch (instant replay freezes game time). Needs a graphics device (not -nographics).</summary>
public class ShotProbe : MonoBehaviour {
  string _dir, _prefix = "shot"; float[] _times = { 1f }; int _next;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
  static void Boot() {
    var args = System.Environment.GetCommandLineArgs(); string dir = null;
    for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-shots") dir = args[i + 1];
    if (dir == null) return;
    var go = new GameObject("ShotProbe"); DontDestroyOnLoad(go);
    var p = go.AddComponent<ShotProbe>(); p._dir = dir;
    for (int i = 0; i + 1 < args.Length; i++) {
      if (args[i] == "-shotTimes") p._times = System.Array.ConvertAll(args[i + 1].Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
      if (args[i] == "-shotPrefix") p._prefix = args[i + 1];
    }
    Directory.CreateDirectory(dir);
  }

  void Update() {
    if (_next >= _times.Length) return;
    if (Time.unscaledTime < _times[_next]) return;
    string path = Path.Combine(_dir, $"{_prefix}_{_next:D2}_{_times[_next]:0.#}s.png".Replace(',', '.'));
    ScreenCapture.CaptureScreenshot(path);
    Debug.Log("[ShotProbe] " + path);
    if (++_next >= _times.Length) StartCoroutine(Quit());   // the capture is written at the end of the frame
  }

  System.Collections.IEnumerator Quit() { yield return new WaitForSecondsRealtime(1f); Application.Quit(0); }
}

}
