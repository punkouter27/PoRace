using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Build date watermark (YYMMDD). Every player build writes Resources/BuildStamp.txt first
/// (Assets/Editor/BuildStamp.cs); in the editor, where no build ran, today's date is shown.</summary>
public static class BuildInfo {
  static string _stamp;
  public static string Stamp {
    get {
      if (_stamp != null) return _stamp;
      var t = Resources.Load<TextAsset>("BuildStamp");
      _stamp = t != null && !Application.isEditor ? t.text.Trim() : System.DateTime.Now.ToString("yyMMdd");
      return _stamp;
    }
  }
  public static string Watermark => $"v{Application.version}  {Stamp}";
}

/// <summary>Puts the version + build watermark on a Text (bottom-right of the menu and the HUDs).</summary>
[RequireComponent(typeof(Text))]
public class Watermark : MonoBehaviour {
  void Awake() => GetComponent<Text>().text = BuildInfo.Watermark;
}

}
