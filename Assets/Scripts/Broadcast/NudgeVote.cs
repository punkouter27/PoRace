using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature A8, the crowd nudge, physically bounded. Chat (ChatLink) votes with !nudge &lt;who&gt; (racer name,
/// or p1..p5); the first vote opens a 10 s window, one vote per viewer. The racer with the most votes (ties go to the
/// one further back) gets +5 % cruise speed for 10 s of race time, clamped inside the policy's trained command range:
/// Go2 &lt;= 2.5 m/s, Matt &lt;= 1.6 m/s (the graduated race band, training/matt/sim.py RACE_VX). 25 s cooldown after a
/// boost. Only the race's cruise speed (Racer.speed) changes; the steering law and the policy are untouched.
/// Off in -raceTest unless -nudge is given, so headless regression races stay bit for bit.</summary>
public class NudgeVote : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ChatLink chat;
  public float window = 10f, boostSeconds = 10f, boost = 0.05f, cooldown = 25f;
  public const float Go2MaxVx = 2.5f, MattMaxVx = 1.6f;

  readonly Dictionary<string, Racer> _votes = new Dictionary<string, Racer>();
  readonly Dictionary<Racer, int> _tally = new Dictionary<Racer, int>();
  float _closeAt = -1f, _boostUntil = -1f, _ready = 0f, _baseSpeed; Racer _boosted;
  Text _label; int _shownSec = -1; bool _dirty;
  public int Boosts { get; private set; }

  public static float MaxCruise(Racer r) => r.matt != null ? MattMaxVx : Go2MaxVx;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race)) || !BroadcastUi.Wired(this, chat, nameof(chat))) return;
    if ((CliArgs.RaceTest && !CliArgs.Has("-nudge"))) { enabled = false; return; }
    chat.Message += OnChat;
    race.Restarted += Clear;
    if (!BroadcastUi.Headless) {
      _label = BroadcastUi.Label(BroadcastUi.Root, "Nudge", Vector2.one, Vector2.one, new Vector2(-16, -290), new Vector2(330, 56), 19, TextAnchor.UpperRight);
      _label.enabled = false;
    }
  }

  void OnDestroy() { if (chat != null) chat.Message -= OnChat; }

  void Clear() { _votes.Clear(); _tally.Clear(); _closeAt = -1f; _boostUntil = -1f; _boosted = null; _ready = 0f; _dirty = true; }   // Draw() re-sets every speed

  float Now => (float)race.RaceTime;

  void OnChat(string user, string text) {
    if (!enabled || !ChatLink.Parse(text, out var cmd, out var arg) || cmd != "!nudge" || race.Phase != RacePhase.Racing) return;
    if (_boosted != null || Now < _ready) return;
    var target = Resolve(arg); if (target == null) return;
    if (_closeAt < 0f) { _closeAt = Now + window; BroadcastTicker.Post($"{BroadcastUi.Paint("NUDGE VOTE", BroadcastUi.Accent)} open: type !nudge and a racer's name for {window:F0} s"); }
    _votes[user] = target; _dirty = true;
  }

  /// <summary>Racer still running from a chat word: its name (Blue, Matt...) or a place p1..p5.</summary>
  Racer Resolve(string word) {
    var live = race.Live;
    if (word.Length == 2 && word[0] == 'p' && char.IsDigit(word[1])) {
      int k = word[1] - '0';
      for (int i = 0; i < live.Count; i++) if (live[i].finishTime < 0 && Place(live[i]) == k) return live[i];
      return null;
    }
    for (int i = 0; i < live.Count; i++) if (live[i].finishTime < 0 && string.Equals(live[i].name, word, StringComparison.OrdinalIgnoreCase)) return live[i];
    return null;
  }

  /// <summary>Running position (1 = furthest along) among racers still on track.</summary>
  int Place(Racer r) {
    int p = 1; var live = race.Live;
    for (int i = 0; i < live.Count; i++) if (live[i] != r && live[i].finishTime < 0 && live[i].distance > r.distance) p++;
    return p;
  }

  void Update() {
    if (race.Phase != RacePhase.Racing) { if (_boosted != null) End(); if (_label != null && _label.enabled) _label.enabled = false; return; }
    if (_closeAt >= 0f && Now >= _closeAt) Close();
    if (_boosted != null && (Now >= _boostUntil || _boosted.finishTime >= 0)) End();
    if (_label == null) return;
    bool on = _closeAt >= 0f || _boosted != null;
    int sec = on ? Mathf.CeilToInt((_boosted != null ? _boostUntil : _closeAt) - Now) : -1;
    if (on == _label.enabled && sec == _shownSec && !_dirty) return;   // text rebuilt once a second or on a vote, not per frame
    _label.enabled = on; _shownSec = sec; _dirty = false;
    if (!on) return;
    if (_boosted != null) { _label.text = $"{BroadcastUi.Paint("NUDGE", BroadcastUi.Accent)} {BroadcastUi.Paint(_boosted.name, _boosted.color)} +{boost * 100:F0}%  {sec}s"; return; }
    Tally(); var sb = new System.Text.StringBuilder(); sb.Append(BroadcastUi.Paint("NUDGE VOTE", BroadcastUi.Accent)).Append(' ').Append(sec).Append("s\n<size=16>");
    foreach (var kv in _tally) sb.Append(BroadcastUi.Paint(kv.Key.name, kv.Key.color)).Append(' ').Append(kv.Value).Append("  ");
    _label.text = sb.Append("</size>").ToString();
  }

  void Tally() { _tally.Clear(); foreach (var v in _votes.Values) if (v.finishTime < 0) _tally[v] = _tally.TryGetValue(v, out int n) ? n + 1 : 1; }

  void Close() {
    _closeAt = -1f; Tally(); _votes.Clear(); _dirty = true;
    Racer win = null; int best = 0;
    foreach (var kv in _tally) if (kv.Value > best || (kv.Value == best && win != null && kv.Key.distance < win.distance)) { win = kv.Key; best = kv.Value; }
    _tally.Clear();
    if (win == null) return;
    float cap = MaxCruise(win), want = Mathf.Min(win.speed * (1f + boost), cap);
    if (want <= win.speed + 1e-3f) { BroadcastTicker.Post($"{BroadcastUi.Paint(win.name, win.color)} is already at the policy's limit ({cap:F1} m/s): no nudge"); _ready = Now + cooldown; return; }
    _baseSpeed = win.speed; win.speed = want; _boosted = win; _boostUntil = Now + boostSeconds; Boosts++;
    BroadcastTicker.Post($"The crowd nudges {BroadcastUi.Paint(win.name, win.color)}: {_baseSpeed:F2} -> {want:F2} m/s for {boostSeconds:F0} s ({best} votes)");
    Debug.Log($"[Nudge] t={Now:F1}s {win.name} {_baseSpeed:F2} -> {want:F2} m/s (cap {cap:F1}) votes={best}");
  }

  void End() {
    if (_boosted != null && race.Phase != RacePhase.Countdown) _boosted.speed = _baseSpeed;   // a restart (Countdown) has re-drawn it already
    _boosted = null; _ready = Now + cooldown; _dirty = true;
  }
}

}
