using UnityEngine;

namespace PoRace {

/// <summary>H.1, one switch for the presentation budget. Mobile on phones, PC elsewhere; -gfx mobile | pc overrides
/// (a desktop player started with -gfx mobile also switches to the Mobile quality level, so its URP asset and
/// renderer are the phone's, for side-by-side captures and PerfProbe runs).</summary>
public static class GfxTier {
  static bool? _mobile;
  public static bool Mobile {
    get {
      if (_mobile == null) {
        _mobile = Application.isMobilePlatform;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-gfx") _mobile = args[i + 1] == "mobile";
        if (_mobile.Value && !Application.isMobilePlatform) {
          int q = System.Array.IndexOf(QualitySettings.names, "Mobile"); if (q >= 0) QualitySettings.SetQualityLevel(q, true);
        }
      }
      return _mobile.Value;
    }
  }

  /// <summary>Non-foot particle bursts per frame.</summary>
  public static int BurstsPerFrame => Mobile ? 1 : 2;
  /// <summary>Scuff marks alive at once (oldest recycled).</summary>
  public static int Scuffs => Mobile ? 24 : 64;
  public static int MaxParticles => Mobile ? 600 : 1500;
  /// <summary>Tracking spotlight with soft shadows (one extra shadow-casting light).</summary>
  public static bool Spotlight => !Mobile;
  /// <summary>Real-time shadows from every robot (PC) or only the director's subject plus blob shadows (Mobile).</summary>
  public static bool BlobShadows => Mobile;
  /// <summary>Vignette and chromatic aberration (bloom and colour grading run on both).</summary>
  public static bool FullPost => !Mobile;
}

}
