using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Pre-race screen (PRD 4.1): pick a map discovered by MapRegistry, which racers start (roster chips: the
/// four dogs and Matt) and the laps, then launch. Headless: PoRace.exe -batchmode -nographics -menuMap 1 -raceTest ...</summary>
public class MenuController : MonoBehaviour {
  public Text mapText, infoText, rosterText, lapsText, weatherText;
  [Tooltip("Roster chips: RaceConfig.Dogs in order, then Matt. Each has a Text child.")]
  public Image[] rosterChips;
  public InputField chatInput;
  MapDefinition[] _maps; int _i, _dogMask = 0b1111, _laps = 1, _weather; bool _matt;
  Text _cbLabel;

  void Start() {
    _maps = MapRegistry.All();
    if (_maps.Length == 0) { if (mapText) mapText.text = "No maps in Resources/Maps"; return; }
    if (RaceConfig.autoplay && !CliArgs.Has("-menuMap") && !CliArgs.RaceTest) { RaceConfig.LaunchNextAutoplay(); return; }
    _dogMask = PlayerPrefs.GetInt("PoRace.dogMask", 0b1111) & ((1 << RaceConfig.Dogs.Length) - 1);
    _weather = PlayerPrefs.GetInt("PoRace.weatherCity", -1);
    _matt = PlayerPrefs.GetInt("PoRace.matt", 0) == 1;
    if (chatInput) chatInput.text = PlayerPrefs.GetString("PoRace.twitch", "");
    // C8 colour-blind palette toggle (BL corner); built at runtime so the menu scene needs no rebuild
    _cbLabel = BroadcastUi.Button(transform, "BtnColorBlind", "", Vector2.up, Vector2.up, new Vector2(12, -12), new Vector2(250, 44),   // TL: clear of START RACE
        new Color(0.08f, 0.09f, 0.12f, 0.9f), 20, () => { RacerPalette.ColorBlind = !RacerPalette.ColorBlind; Refresh(); }).GetComponentInChildren<Text>();
    SelectMap(0);
    if (CliArgs.TryInt("-menuMap", out int mm)) { SelectMap(mm); Invoke(nameof(OnLaunch), 0.5f); }
  }

  void SelectMap(int i) {
    _i = ((i % _maps.Length) + _maps.Length) % _maps.Length;
    _laps = _maps[_i].closed ? _maps[_i].defaultLaps : 1;
    if (Count() == 0) _dogMask = 1;   // Matt alone, then a map he cannot race: Blue starts
    Refresh();
  }

  void Refresh() {
    var m = _maps[_i];
    if (mapText) mapText.text = m.displayName;
    if (infoText) infoText.text = $"{m.Length * (m.closed ? _laps : 1):F0} m   {m.terrain}   difficulty {new string('*', m.difficulty)}" + System.Environment.NewLine
        + $"{(m.closed ? "circuit" : "point to point")}   grid {m.gridSlots}   map {_i + 1} of {_maps.Length}";
    if (lapsText) lapsText.text = m.closed ? _laps.ToString() : "-";
    // Matt (the human racer, roster Creature02): flat maps only, his policy never trained on banks or obstacles
    bool flat = m.terrain == "flat";
    for (int i = 0; rosterChips != null && i < rosterChips.Length; i++) {
      bool isMatt = i == RaceConfig.Dogs.Length, on = isMatt ? _matt && flat : (_dogMask >> i & 1) != 0;
      var col = RacerPalette.Get(i, isMatt ? RaceConfig.MattColor : RaceConfig.Dogs[i].color);   // chip index = roster / palette index
      rosterChips[i].color = on ? col : new Color(col.r * 0.22f, col.g * 0.22f, col.b * 0.22f, 0.95f);
      var label = rosterChips[i].GetComponentInChildren<Text>();
      label.text = RacerPalette.Glyph(i) + (RacerPalette.ColorBlind ? " " : "") + (isMatt ? "Matt" : RaceConfig.Dogs[i].name) + "\n" + (isMatt && !flat ? "flat only" : on ? "IN" : "out");
      label.color = on ? new Color(0.08f, 0.09f, 0.12f) : new Color(1f, 1f, 1f, isMatt && !flat ? 0.35f : 0.7f);
    }
    if (rosterText) rosterText.text = $"RACERS  {Count()}";
    if (_cbLabel) _cbLabel.text = RacerPalette.ColorBlind ? "Colour-blind colours: ON" : "Colour-blind colours: off";
    if (weatherText) weatherText.text = _weather >= 0 && _weather < TrackWeather.Cities.Length ? "live: " + TrackWeather.Cities[_weather].name : "off";
  }

  /// <summary>Live weather circuit: each of TrackWeather.Cities, then off (-1).</summary>
  public void OnWeatherNext() { _weather = _weather + 1 >= TrackWeather.Cities.Length ? -1 : _weather + 1; Refresh(); }
  public void OnWeatherPrev() { _weather = _weather - 1 < -1 ? TrackWeather.Cities.Length - 1 : _weather - 1; Refresh(); }

  /// <summary>Roster chip i (RaceConfig.Dogs, then Matt): in or out. The last racer on the grid stays.</summary>
  public void OnRosterToggle(int i) {
    if (i == RaceConfig.Dogs.Length) { if (_maps[_i].terrain != "flat" || (_matt && Count() == 1)) return; _matt = !_matt; }
    else { int bit = 1 << i; if ((_dogMask & bit) != 0 && Count() == 1) return; _dogMask ^= bit; }
    Refresh();
  }

  /// <summary>Racers on the grid for the current map (Matt counts on flat maps only).</summary>
  int Count() {
    int n = _matt && _maps[_i].terrain == "flat" ? 1 : 0;
    for (int m = _dogMask; m != 0; m &= m - 1) n++;
    return n;
  }

  public void OnNextMap() => SelectMap(_i + 1);
  public void OnPrevMap() => SelectMap(_i - 1);
  public void OnLapsUp() { if (_maps[_i].closed) _laps = Mathf.Min(_laps + 1, 5); Refresh(); }
  public void OnLapsDown() { if (_maps[_i].closed) _laps = Mathf.Max(_laps - 1, 1); Refresh(); }

  public void OnLaunch() {
    RaceConfig.dogMask = _dogMask; RaceConfig.laps = _laps; RaceConfig.seed = -1; RaceConfig.fromMenu = true;
    RaceConfig.weatherCity = _weather; RaceConfig.twitchChannel = chatInput ? chatInput.text.Trim() : "";
    RaceConfig.matt = _matt && _maps[_i].terrain == "flat"; PlayerPrefs.SetInt("PoRace.matt", _matt ? 1 : 0); PlayerPrefs.SetInt("PoRace.dogMask", _dogMask);
    PlayerPrefs.SetInt("PoRace.weatherCity", _weather); PlayerPrefs.SetString("PoRace.twitch", RaceConfig.twitchChannel); PlayerPrefs.Save();
    SceneManager.LoadScene(_maps[_i].sceneName);
  }
}

}
