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
        _mobile = Application.isMobilePlatform || RaceConfig.autoplay;   // the self-running app keeps the GPU light
        if (CliArgs.TryString("-gfx", out var gfx)) _mobile = gfx == "mobile";
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
  /// <summary>B2 refraction shimmer over hot joints (needs the camera opaque texture); Mobile keeps the leg tint only.</summary>
  public static bool HeatShimmer => !Mobile;
  /// <summary>B5 team rim on every robot link (PC) or on the trunk / Matt's skin only (Mobile: one extra pass each).</summary>
  public static bool TeamRimAll => !Mobile;
  /// <summary>B3 footfall rings alive at once.</summary>
  public static int FootRings => Mobile ? 24 : 64;
  /// <summary>B4 confetti particles for the winner's burst (later finishers get a fifth).</summary>
  public static int Confetti => Mobile ? 200 : 500;
  /// <summary>B10 one-shot sound voices (impacts, steps, stingers), lowest priority stolen first.</summary>
  public static int AudioVoices => Mobile ? 8 : 24;
  /// <summary>B6 servo partials per racer: one per limb (PC) or one mixed whine (Mobile), always one voice per racer.</summary>
  public static int ServoPartials => Mobile ? 1 : 4;
}

}
