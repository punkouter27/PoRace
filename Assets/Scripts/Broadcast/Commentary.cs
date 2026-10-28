using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PoRace {

/// <summary>Feature A1, commentary captions. Race events (Overtook / Fell / Recovered / Finished, StrainMonitor
/// REDLINING) and changes in the gap at the front (closing up, pulling away, the final stretch) fill a small template
/// bank ("Red takes Blue into turn 2!") and go to the lower-third ticker in italics. Turns are found once from the
/// map's centreline (heading change over 6 m above 15 deg), so a caption can say where it happened. Cooldowns: 3 s
/// between captions, 8 s per kind; lead changes, falls and the win always speak. Template picks are seeded from the
/// race seed. Read only; headless runs print to the log through the ticker.</summary>
public class Commentary : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public StrainMonitor strain;
  public float gap = 3f, kindGap = 8f;

  enum Kind { Start, Pass, Lead, Fall, Up, Reset, Finish, Redline, Close, Away, Final, Count }
  static readonly string[] PassT = { "{0} takes {1} {2}!", "{0} gets the move done on {1} {2}.", "Lovely pass, {0} goes by {1} {2}." };
  static readonly string[] LeadT = { "{0} takes the lead {2}!", "{0} snatches P1 from {1} {2}!", "New leader! {0} gets by {1} {2}." };
  static readonly string[] DownPassT = { "{0} slips past the fallen {1}.", "{1} is down and {0} takes the place." };
  static readonly string[] FallT = { "{0} goes down {2}!", "Trouble for {0}, down {2}!", "Oh no, {0} is on the deck {2}!" };
  static readonly string[] UpT = { "{0} is back up and running.", "Up again, {0} is fighting back!" };
  static readonly string[] RedT = { "{0} is on the redline, can those joints hold?", "{0} is pushing to the limit!" };
  static readonly string[] CloseT = { "Nothing between {0} and {1}!", "{1} is right on {0}'s tail!" };
  static readonly string[] WinT = { "{0} wins it in {3} s!", "And it's {0}! Victory in {3} s." };

  readonly float[] _last = new float[(int)Kind.Count];
  readonly List<Vector2> _turns = new List<Vector2>();   // (start, end) arc length on one lap
  readonly List<(Racer r, int resets)> _down = new List<(Racer, int)>();   // fallen racers, with their reset count at the fall
  System.Random _rng; float _lastAny = -100f, _nextGap; int _gapState; bool _final; Racer _pairA, _pairB; float _pairAt = -100f;

  float Now => (float)race.RaceTime;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (strain == null) strain = FindAnyObjectByType<StrainMonitor>();
    FindTurns();
    ClearRace();
    race.Restarted += ClearRace;
    race.PhaseChanged += p => { if (p == RacePhase.Racing) Say(Kind.Start, true, $"And they're off at {race.map.displayName}!"); };
    race.Overtook += OnPass;
    // Back up = RACING again after a fall, polled in Update: race.Recovered only fires on a one-step FALLEN -> RACING
    // jump (a reset), not on a getup that passes through STUMBLE.
    race.Fell += r => { if (!_down.Exists(x => x.r == r)) _down.Add((r, r.autoReset != null ? r.autoReset.Resets : 0)); Say(Kind.Fall, true, Pick(FallT, r, null)); };
    race.Finished += r => {
      if (r.place == 1) Say(Kind.Finish, true, Pick(WinT, r, null));
      else Say(Kind.Finish, false, $"{Name(r)} home in P{r.place}.");
    };
    if (strain != null) strain.Redline += r => { if (Ahead(r) <= 1) Say(Kind.Redline, false, Pick(RedT, r, null)); };
  }

  void ClearRace() {   // not Reset(): Unity calls that on AddComponent in the editor
    _rng = new System.Random(race.Seed ^ 0x5eed); for (int i = 0; i < _last.Length; i++) _last[i] = -100f;
    _lastAny = -100f; _nextGap = 0f; _gapState = 0; _final = false; _pairA = _pairB = null; _down.Clear();
  }

  /// <summary>Corners of the lap: runs of 2 m samples whose heading turns more than 15 deg over 6 m.</summary>
  void FindTurns() {
    var m = race.map; float L = m.Length, start = -1f;
    for (float s = 0f; s <= L; s += 2f) {
      m.PointAt(m.closed ? s - 3f : Mathf.Max(0f, s - 3f), out var a); m.PointAt(s + 3f, out var b);
      bool turning = Mathf.Abs(Vector2.SignedAngle(a, b)) > 15f;
      if (turning && start < 0f) start = s;
      if (!turning && start >= 0f) { _turns.Add(new Vector2(start, s)); start = -1f; }
    }
    if (start >= 0f) _turns.Add(new Vector2(start, L));
    // A closed lap that starts mid-corner: join the last run onto the first.
    if (m.closed && _turns.Count > 1 && _turns[0].x <= 0f && _turns[_turns.Count - 1].y >= L - 2f) {
      _turns[0] = new Vector2(_turns[_turns.Count - 1].x - L, _turns[0].y); _turns.RemoveAt(_turns.Count - 1);
    }
  }

  /// <summary>Where on the track a distance is: off the line, into / through turn k, the run to the line, the straight.</summary>
  string Where(float d) {
    if (race.TotalLength - d < 15f) return "on the run to the line";
    if (d < 12f) return "off the line";
    float s = race.map.closed ? Mathf.Repeat(d, race.map.Length) : d;
    for (int i = 0; i < _turns.Count; i++) {
      var t = _turns[i];
      if (s >= t.x && s <= t.y) return $"through turn {i + 1}";
      if (s < t.x && t.x - s < 10f) return $"into turn {i + 1}";
    }
    return "on the straight";
  }

  /// <summary>Racers still running ahead of r (0 = r leads).</summary>
  int Ahead(Racer r) {
    int n = 0; var live = race.Live;
    for (int i = 0; i < live.Count; i++) if (live[i] != r && live[i].finishTime < 0 && live[i].distance > r.distance) n++;
    return n;
  }

  void OnPass(Racer a, Racer b) {
    // Two racers side by side swap places back and forth: one caption per pair per 8 s.
    bool same = (a == _pairA && b == _pairB) || (a == _pairB && b == _pairA);
    if (same && Now - _pairAt < kindGap) return;
    _pairA = a; _pairB = b; _pairAt = Now;
    if (b.state != RaceState.RACING) Say(Kind.Pass, false, Pick(DownPassT, a, b));
    else if (Ahead(a) == 0) Say(Kind.Lead, true, Pick(LeadT, a, b));
    else Say(Kind.Pass, false, Pick(PassT, a, b));
  }

  static string Name(Racer r) => BroadcastUi.Paint(r.name, r.color);

  string Pick(string[] bank, Racer a, Racer b) =>
    string.Format(CultureInfo.InvariantCulture, bank[_rng.Next(bank.Length)], Name(a), b != null ? Name(b) : "", Where(a.distance),
                  a.finishTime >= 0 ? a.finishTime.ToString("F2", CultureInfo.InvariantCulture) : "", a.place);

  void Say(Kind kind, bool priority, string text) {
    float now = Now;
    if (now - _lastAny < (priority ? 1f : gap) || (!priority && now - _last[(int)kind] < kindGap)) return;
    _lastAny = now; _last[(int)kind] = now;
    BroadcastTicker.Post("» <i>" + text + "</i>", 5f);
  }

  void Update() {
    if (race.Phase != RacePhase.Racing) return;
    for (int i = _down.Count - 1; i >= 0; i--) {
      var (r, resets) = _down[i];
      if (r.finishTime >= 0) { _down.RemoveAt(i); continue; }
      if (r.state != RaceState.RACING) continue;
      _down.RemoveAt(i);
      if (r.autoReset != null && r.autoReset.Resets > resets) Say(Kind.Reset, false, $"{Name(r)} is sent back to the last checkpoint.");
      else Say(Kind.Up, false, Pick(UpT, r, null));
    }
    if (Now < _nextGap) return;
    _nextGap = Now + 0.5f;   // the gap at the front, twice a second (no allocation unless a caption fires)
    Racer lead = null, second = null; var live = race.Live;
    for (int i = 0; i < live.Count; i++) {
      var r = live[i]; if (r.finishTime >= 0) continue;
      if (lead == null || r.distance > lead.distance) { second = lead; lead = r; }
      else if (second == null || r.distance > second.distance) second = r;
    }
    if (lead == null || second == null) return;
    float g = lead.distance - second.distance;
    if (g < 0.6f && _gapState != 1 && Now > 8f) { _gapState = 1; Say(Kind.Close, false, Pick(CloseT, lead, second)); }
    else if (g > 6f && _gapState != 2) { _gapState = 2; Say(Kind.Away, false, string.Format(CultureInfo.InvariantCulture, "{0} is pulling away, {1:F0} m clear of {2}.", Name(lead), g, Name(second))); }
    else if (g > 2f && g < 4f) _gapState = 0;   // hysteresis: re-arm between the two thresholds
    if (!_final && lead.distance > 0.85f * race.TotalLength) {
      _final = true; Say(Kind.Final, true, string.Format(CultureInfo.InvariantCulture, "Final stretch! {0} leads {1} by {2:F1} m.", Name(lead), Name(second), g));
    }
  }
}

}
