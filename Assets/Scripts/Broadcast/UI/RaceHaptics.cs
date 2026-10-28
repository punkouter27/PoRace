using UnityEngine;

namespace PoRace {

/// <summary>C4, haptics on key events (Android only; a no-op elsewhere). The racer you back (StandingsChips.Backed)
/// passing or being passed: a short buzz; falling: a double-length one; the finish: a strong one (your racer home,
/// or the winner when you back nobody). Android Vibrator through JNI (VibrationEffect amplitude on API 26+), with
/// Handheld.Vibrate as the fallback (its reference also makes Unity add the VIBRATE permission). PlayerPrefs toggle.</summary>
public class RaceHaptics : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public int passMs = 35, fallMs = 70, finishMs = 320;
  const string Key = "PoRace.haptics";
  static int _on = -1;

  public static bool Enabled {
    get { if (_on < 0) _on = PlayerPrefs.GetInt(Key, 1); return _on == 1; }
    set { _on = value ? 1 : 0; PlayerPrefs.SetInt(Key, _on); PlayerPrefs.Save(); }
  }

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
#if UNITY_ANDROID
    if (BroadcastUi.Headless) { enabled = false; return; }
    race.Overtook += (a, b) => { var me = StandingsChips.Backed; if (me != null && (a == me || b == me)) Buzz(passMs); };
    race.Fell += r => { if (r == StandingsChips.Backed) Buzz(fallMs); };
    race.Finished += r => { var me = StandingsChips.Backed; if (r == me || (me == null && r.place == 1)) Buzz(finishMs, 255); };
#else
    enabled = false;
#endif
  }

#if UNITY_ANDROID && !UNITY_EDITOR
  static AndroidJavaObject _vibrator; static int _sdk = -1;
#endif

  /// <summary>One vibration of `ms` milliseconds (amplitude 1..255, -1 = the device default). Android only.</summary>
  public static void Buzz(int ms, int amplitude = -1) {
#if UNITY_ANDROID && !UNITY_EDITOR
    if (!Enabled) return;
    try {
      if (_vibrator == null) {
        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity")) _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
        using (var ver = new AndroidJavaClass("android.os.Build$VERSION")) _sdk = ver.GetStatic<int>("SDK_INT");
      }
      if (_sdk >= 26) {
        using (var fx = new AndroidJavaClass("android.os.VibrationEffect"))
        using (var effect = fx.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, amplitude)) _vibrator.Call("vibrate", effect);
      } else _vibrator.Call("vibrate", (long)ms);
    } catch (System.Exception) { Handheld.Vibrate(); }
#endif
  }
}

}
