using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PoRace {

/// <summary>Feature A7, season mode: a seeded series of races over a map pool (each map roundsPerMap times, shuffled
/// per pass, each round with its own race seed), points per place (10 7 5 3 1; a racer still on track when the race
/// is called is classified by distance and scores only past half distance), the race's RaceTelemetry summary rolled
/// up per racer (falls, passes, redlines, metres). State: &lt;persistentDataPath&gt;/season.json. The self-running
/// app asks NextRound for each race (RaceConfig.LaunchNextAutoplay); a finished season starts the next one.
/// -season &lt;seed&gt; starts a fresh season with that seed, -noSeason keeps the plain map rotation.</summary>
public static class Season {
  [Serializable] public class Round { public string map, winner = "", telemetry = ""; public int seed; public bool done; }
  [Serializable] public class Entry { public string racer; public int points, races, wins, podiums, falls, overtakes, redlines; public float distance; }
  [Serializable] public class State {
    public int number = 1, seed, next; public string champion = "", lastChampion = "";
    public List<Round> rounds = new List<Round>(); public List<Entry> table = new List<Entry>();
    public bool Complete => next >= rounds.Count;
  }

  public static readonly int[] Points = { 10, 7, 5, 3, 1 };
  public static int roundsPerMap = 2;
  /// <summary>Round of Current being raced in the loaded scene (-1 = not a season race).</summary>
  public static int RacingRound { get; private set; } = -1;
  static State _s; static bool _loaded, _argDone;
  static string StatePath => Path.Combine(Application.persistentDataPath, "season.json");

  public static bool Enabled => !CliArgs.Has("-noSeason");
  public static State Current { get { if (!_loaded) { _loaded = true; _s = Load(); } return _s; } }

  /// <summary>Picks the next race of the season from `pool` (the maps the caller may race on): scores the race on
  /// screen first, starts a new season when none is running, returns the map and race seed. False = season off.</summary>
  public static bool NextRound(MapDefinition[] pool, out MapDefinition map, out int seed) {
    map = null; seed = -1;
    if (!Enabled || pool == null || pool.Length == 0) return false;
    SeasonMode.ScoreOpen();
    var s = Current;
    if (!CliArgs.TryInt("-season", out int argSeed)) argSeed = -1;
    if (s == null || s.Complete || (argSeed >= 0 && !_argDone)) s = Begin(pool, argSeed >= 0 && !_argDone ? argSeed : Environment.TickCount & 0xFFFF, s);
    _argDone = true;
    for (int guard = 0; guard < 2 * s.rounds.Count + 2; guard++) {
      if (s.Complete) s = Begin(pool, (s.seed * 1103515245 + 12345) & 0xFFFF, s);
      var rd = s.rounds[s.next];
      map = Array.Find(pool, m => m.name == rd.map);
      if (map != null) { seed = rd.seed; RacingRound = s.next; Save(); return true; }
      rd.done = true; s.next++;   // a map no longer in the pool: skip its round
    }
    return false;
  }

  static State Begin(MapDefinition[] pool, int seed, State prev) {
    var rng = new System.Random(seed);
    var s = new State { seed = seed, number = prev != null ? prev.number + 1 : 1, lastChampion = prev != null ? prev.champion : "" };
    for (int pass = 0; pass < Mathf.Max(1, roundsPerMap); pass++) {
      var names = Array.ConvertAll(pool, m => m.name);
      for (int i = names.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = names[i]; names[i] = names[j]; names[j] = t; }
      foreach (var n in names) s.rounds.Add(new Round { map = n, seed = rng.Next(0, 0x10000) });
    }
    _s = s; _loaded = true; RacingRound = -1; Save();
    Debug.Log($"[Season] season {s.number} seed {seed}: {s.rounds.Count} rounds");
    return s;
  }

  public static Entry EntryOf(string racer) {
    var s = Current; if (s == null) return null;
    var e = s.table.Find(x => x.racer == racer);
    if (e == null) s.table.Add(e = new Entry { racer = racer });
    return e;
  }

  /// <summary>Scores the race of RacingRound once and moves the season on. Classified order: finishers by place, then
  /// the rest by distance.</summary>
  public static void Score(MultiRaceOrchestrator race, List<RaceTelemetry.Summary> rows, string telemetryPath) {
    var s = Current; if (s == null || RacingRound < 0 || RacingRound >= s.rounds.Count) return;
    var rd = s.rounds[RacingRound]; RacingRound = -1;
    if (rd.done) return;
    var order = new List<Racer>(race.Live);
    order.Sort((a, b) => a.finishTime >= 0 && b.finishTime >= 0 ? a.place.CompareTo(b.place) : a.finishTime >= 0 ? -1 : b.finishTime >= 0 ? 1 : b.distance.CompareTo(a.distance));
    for (int i = 0; i < order.Count; i++) {
      var r = order[i]; var e = EntryOf(r.name);
      bool classified = r.finishTime >= 0 || r.distance >= 0.5f * race.TotalLength;
      if (classified && i < Points.Length) e.points += Points[i];
      e.races++; if (classified && i == 0) e.wins++; if (classified && i < 3) e.podiums++;
      e.falls += r.falls; e.overtakes += r.overtakes; e.distance += r.distance;
      if (rows != null) { var row = rows.Find(x => x.racer == r.name); if (row != null) e.redlines += row.redlines; }
    }
    rd.done = true; rd.winner = order.Count > 0 ? order[0].name : ""; rd.telemetry = telemetryPath ?? "";
    s.next = Mathf.Max(s.next, s.rounds.IndexOf(rd) + 1);
    s.table.Sort((a, b) => b.points != a.points ? b.points.CompareTo(a.points) : b.wins.CompareTo(a.wins));
    if (s.Complete && s.table.Count > 0) s.champion = s.table[0].racer;
    Save();
  }

  static void Save() {
    if (_s == null) return;
    try { File.WriteAllText(StatePath, JsonUtility.ToJson(_s, true)); }
    catch (Exception e) { Debug.LogWarning("[Season] save failed: " + e.Message); }
  }

  static State Load() {
    try {
      if (!File.Exists(StatePath)) return null;
      var s = JsonUtility.FromJson<State>(File.ReadAllText(StatePath));
      return s != null && s.rounds != null && s.rounds.Count > 0 && s.table != null ? s : null;
    } catch (Exception e) { Debug.LogWarning("[Season] load failed: " + e.Message); return null; }
  }
}

/// <summary>Season mode in a race scene: when this race is the season's current round it is scored at the finish
/// (or when the self-running app moves on), the standings show each racer's season points, and the ticker announces
/// the round, the table, the closest rivalry and the champion. Inactive outside a season race (menu races, -raceTest).</summary>
public class SeasonMode : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceTelemetry telemetry;

  static SeasonMode _live;
  bool _counts, _scored;
  readonly Dictionary<Racer, string> _pts = new Dictionary<Racer, string>();   // cached standings suffix

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (telemetry == null) telemetry = FindAnyObjectByType<RaceTelemetry>();
    var s = Season.Current; int k = Season.RacingRound;
    _counts = RaceConfig.autoplay && s != null && k >= 0 && k < s.rounds.Count && s.rounds[k].map == race.map.name
              && !CliArgs.RaceTest;
    if (!_counts) { enabled = false; return; }
    _live = this;
    Refresh();
    race.standingsExtras.Add(r => _pts.TryGetValue(r, out var p) ? p : "");
    race.PhaseChanged += p => { if (p == RacePhase.Finished) Score(); };
    var lead = s.table.Count > 0 ? $" · leader {s.table[0].racer} {s.table[0].points} pts" : s.lastChampion != "" ? $" · reigning champion {s.lastChampion}" : "";
    BroadcastTicker.Post($"{BroadcastUi.Paint($"SEASON {s.number}", BroadcastUi.Accent)} round {k + 1}/{s.rounds.Count}{lead}", 7f);
  }

  void OnDestroy() { if (_live == this) _live = null; }

  /// <summary>Scores the race on screen if it is a season round that started but was not scored (the app moving on
  /// after its timeout). Called by Season.NextRound before the next scene loads.</summary>
  public static void ScoreOpen() { if (_live != null && _live.race.Phase != RacePhase.Countdown) _live.Score(); }

  void Score() {
    if (!_counts || _scored) return;
    _scored = true;
    Season.Score(race, telemetry != null ? telemetry.Summaries() : null, telemetry != null ? telemetry.LastSummaryPath : null);
    Refresh();
    var s = Season.Current; if (s == null || s.table.Count == 0) return;
    var sb = new System.Text.StringBuilder($"Season table: ");
    for (int i = 0; i < Mathf.Min(4, s.table.Count); i++) sb.Append(i > 0 ? " · " : "").Append(s.table[i].racer).Append(' ').Append(s.table[i].points);
    BroadcastTicker.Post(sb.ToString(), 8f);
    // Rivalry: the closest pair in points among the top three.
    int best = int.MaxValue, at = -1;
    for (int i = 0; i + 1 < Mathf.Min(3, s.table.Count); i++) { int g = s.table[i].points - s.table[i + 1].points; if (g < best) { best = g; at = i; } }
    if (at >= 0 && !s.Complete) BroadcastTicker.Post($"Rivalry: {s.table[at + 1].racer} trails {s.table[at].racer} by {best} pts", 8f);
    if (s.Complete) BroadcastTicker.Post($"{BroadcastUi.Paint("SEASON CHAMPION", BroadcastUi.Accent)} {s.champion} with {s.table[0].points} pts", 10f);
    Debug.Log($"[Season] {s.number} after round {s.next}/{s.rounds.Count}: {sb}");
  }

  void Refresh() {
    _pts.Clear(); var live = race.Live;
    for (int i = 0; i < live.Count; i++) {
      var e = Season.EntryOf(live[i].name);
      _pts[live[i]] = $"  <size=16>{BroadcastUi.Paint(e.points + "pt", BroadcastUi.Accent)}</size>";
    }
  }
}

}
