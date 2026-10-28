using System;
using UnityEngine;

namespace PoRace {

/// <summary>C8, colour-blind safe racer colours. Off: each racer keeps its livery. On: the Okabe-Ito set (distinct for
/// protan / deutan / tritan viewers) plus a shape glyph per racer on the chips. Index = roster index (RaceConfig.Dogs
/// order, then Matt; the same order as MultiRaceOrchestrator.racers). Persisted in PlayerPrefs.</summary>
public static class RacerPalette {
  const string Key = "PoRace.colorBlind";
  // Okabe & Ito 2008: sky blue, vermillion, bluish green, yellow, reddish purple (Blue, Red, Green, Gold, Matt)
  static readonly Color[] Safe = {
    new Color(0.337f, 0.706f, 0.914f), new Color(0.835f, 0.369f, 0f), new Color(0f, 0.620f, 0.451f),
    new Color(0.941f, 0.894f, 0.259f), new Color(0.800f, 0.475f, 0.655f), new Color(0.902f, 0.624f, 0f),
  };
  /// <summary>Shape per racer (glyphs in the built-in font, so WebGL draws them too).</summary>
  static readonly string[] Glyphs = { "●", "▲", "■", "▼", "◊", "►" };
  static int _on = -1;

  public static event Action Changed;

  public static bool ColorBlind {
    get { if (_on < 0) _on = PlayerPrefs.GetInt(Key, 0); return _on == 1; }
    set { _on = value ? 1 : 0; PlayerPrefs.SetInt(Key, _on); PlayerPrefs.Save(); Changed?.Invoke(); }
  }

  /// <summary>Racer colour for roster index `index`: the safe colour when the toggle is on, else `defaultColor`.</summary>
  public static Color Get(int index, Color defaultColor) => ColorBlind && index >= 0 ? Safe[index % Safe.Length] : defaultColor;

  /// <summary>Shape glyph for roster index `index` when the toggle is on, else "".</summary>
  public static string Glyph(int index) => ColorBlind && index >= 0 ? Glyphs[index % Glyphs.Length] : "";
}

}
