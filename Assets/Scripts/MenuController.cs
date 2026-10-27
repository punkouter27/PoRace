using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Pre-race screen (PRD 4.1): pick a map discovered by MapRegistry, the number of racers (capped by the
/// map's grid) and the laps, then launch. Headless: PoRace.exe -batchmode -nographics -menuMap 1 -raceTest ...</summary>
public class MenuController : MonoBehaviour {
  public Text mapText, infoText, racersText, lapsText, weatherText;
  public InputField chatInput;
  MapDefinition[] _maps; int _i, _racers = 4, _laps = 1, _weather;

  void Start() {
    _maps = MapRegistry.All();
    if (_maps.Length == 0) { if (mapText) mapText.text = "No maps in Resources/Maps"; return; }
    _racers = RaceConfig.fromMenu ? RaceConfig.racers : 4;
    _weather = PlayerPrefs.GetInt("PoRace.weatherCity", 0);
    if (chatInput) chatInput.text = PlayerPrefs.GetString("PoRace.twitch", "");
    SelectMap(0);
    var args = Environment.GetCommandLineArgs();
    for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-menuMap") { SelectMap(int.Parse(args[i + 1])); Invoke(nameof(OnLaunch), 0.5f); }
  }

  void SelectMap(int i) {
    _i = ((i % _maps.Length) + _maps.Length) % _maps.Length;
    _laps = _maps[_i].closed ? _maps[_i].defaultLaps : 1;
    _racers = Mathf.Clamp(_racers, 1, _maps[_i].gridSlots);
    Refresh();
  }

  void Refresh() {
    var m = _maps[_i];
    if (mapText) mapText.text = m.displayName;
    if (infoText) infoText.text = $"{m.Length * (m.closed ? _laps : 1):F0} m   {m.terrain}   difficulty {new string('*', m.difficulty)}" + System.Environment.NewLine
        + $"{(m.closed ? "circuit" : "point to point")}   grid {m.gridSlots}   map {_i + 1} of {_maps.Length}";
    if (racersText) racersText.text = _racers.ToString();
    if (lapsText) lapsText.text = m.closed ? _laps.ToString() : "-";
    if (weatherText) weatherText.text = _weather >= 0 && _weather < TrackWeather.Cities.Length ? "live: " + TrackWeather.Cities[_weather].name : "off";
  }

  /// <summary>Live weather circuit: each of TrackWeather.Cities, then off (-1).</summary>
  public void OnWeatherNext() { _weather = _weather + 1 >= TrackWeather.Cities.Length ? -1 : _weather + 1; Refresh(); }
  public void OnWeatherPrev() { _weather = _weather - 1 < -1 ? TrackWeather.Cities.Length - 1 : _weather - 1; Refresh(); }

  public void OnNextMap() => SelectMap(_i + 1);
  public void OnPrevMap() => SelectMap(_i - 1);
  public void OnRacersUp() { _racers = Mathf.Min(_racers + 1, _maps[_i].gridSlots); Refresh(); }
  public void OnRacersDown() { _racers = Mathf.Max(_racers - 1, 1); Refresh(); }
  public void OnLapsUp() { if (_maps[_i].closed) _laps = Mathf.Min(_laps + 1, 5); Refresh(); }
  public void OnLapsDown() { if (_maps[_i].closed) _laps = Mathf.Max(_laps - 1, 1); Refresh(); }

  public void OnLaunch() {
    RaceConfig.racers = _racers; RaceConfig.laps = _laps; RaceConfig.seed = -1; RaceConfig.fromMenu = true;
    RaceConfig.weatherCity = _weather; RaceConfig.twitchChannel = chatInput ? chatInput.text.Trim() : "";
    PlayerPrefs.SetInt("PoRace.weatherCity", _weather); PlayerPrefs.SetString("PoRace.twitch", RaceConfig.twitchChannel); PlayerPrefs.Save();
    SceneManager.LoadScene(_maps[_i].sceneName);
  }
}

}
