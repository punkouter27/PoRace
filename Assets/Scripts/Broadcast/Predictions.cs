using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature 6, predictions and form guide. Before the start (the countdown is stretched to 10 s in interactive
/// play) the panel shows each racer's odds from this race's speed draw and its win record; tap a racer (or keys 1-4)
/// to stake 100 points (repeat to add, up to 500). Chat votes with !bet &lt;name&gt; (one per viewer) and the crowd split
/// is shown. During the race each standings row gets a live win chance from the remaining distance, speed and state.
/// The first finisher settles the stakes; points and win records persist in PlayerPrefs.</summary>
public class Predictions : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public ChatLink chat;
  public int stakeStep = 100, maxStake = 500, startingPoints = 1000;
  public float interactiveCountdown = 10f;

  const string PointsKey = "PoRace.points";
  readonly Dictionary<Racer, float> _odds = new Dictionary<Racer, float>(), _live = new Dictionary<Racer, float>();
  readonly Dictionary<Racer, int> _stake = new Dictionary<Racer, int>();
  readonly Dictionary<string, Racer> _chatBets = new Dictionary<string, Racer>();
  bool _settled; Image _panel; Text _title; readonly List<(Racer r, Text t)> _rows = new List<(Racer, Text)>();

  public static int Points { get => PlayerPrefs.GetInt(PointsKey, 1000); set { PlayerPrefs.SetInt(PointsKey, value); PlayerPrefs.Save(); } }
  public float Odds(Racer r) => _odds.TryGetValue(r, out var o) ? o : 0f;
  public float WinChance(Racer r) => _live.TryGetValue(r, out var p) ? p : 0f;

  void Awake() {
    if (race == null) race = FindAnyObjectByType<MultiRaceOrchestrator>();
    bool test = Environment.GetCommandLineArgs().Contains("-raceTest");
    if (!BroadcastUi.Headless && !test) race.countdownSeconds = Mathf.Max(race.countdownSeconds, interactiveCountdown);
  }

  void Start() {
    if (chat == null) chat = FindAnyObjectByType<ChatLink>();
    if (chat != null) chat.Message += OnChat;
    if (!PlayerPrefs.HasKey(PointsKey)) Points = startingPoints;
    race.standingsExtras.Add(r => race.Phase == RacePhase.Racing && r.finishTime < 0 ? $"  <size=18>{WinChance(r) * 100:F0}%</size>" : "");
    race.Restarted += Open; race.Finished += OnFinished;
    if (!BroadcastUi.Headless) BuildPanel();
    Open();
  }

  void Open() {
    _settled = false; _stake.Clear(); _chatBets.Clear();
    // Form: speed draw (the main thing that decides a clean race) + smoothed win rate from past races on any map.
    float vmax = race.Live.Max(r => r.speed);
    var score = race.Live.ToDictionary(r => r, r => (r.speed - vmax) / Mathf.Max(0.01f, race.speedSpread * r.cruiseSpeed) * 2.2f
        + 0.6f * Mathf.Log((PlayerPrefs.GetInt("PoRace.wins." + r.name, 0) + 1f) / (PlayerPrefs.GetInt("PoRace.races." + r.name, 0) + 4f) * 4f));
    var p = Softmax(score);
    foreach (var r in race.Live) _odds[r] = Mathf.Max(1.1f, 0.92f / Mathf.Max(0.02f, p[r]));
    BroadcastTicker.Post("Form guide: " + string.Join("   ", race.Live.OrderBy(r => _odds[r]).Select(r => $"{BroadcastUi.Paint(r.name, r.color)} {_odds[r]:F1}x")), 8f);
  }

  static Dictionary<Racer, float> Softmax(Dictionary<Racer, float> s) {
    float m = s.Values.Max(); var e = s.ToDictionary(kv => kv.Key, kv => Mathf.Exp(kv.Value - m)); float z = e.Values.Sum();
    return e.ToDictionary(kv => kv.Key, kv => kv.Value / z);
  }

  public void Back(Racer r) {
    if (race.Phase != RacePhase.Countdown || r == null) return;
    _stake.TryGetValue(r, out int s);
    int add = Mathf.Min(stakeStep, maxStake - _stake.Values.Sum(), Points);
    if (add <= 0) return;
    _stake[r] = s + add; Points -= add;
  }

  void OnChat(string user, string text) {
    var parts = text.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 2 || parts[0] != "!bet" || race.Phase != RacePhase.Countdown) return;
    var r = race.Live.FirstOrDefault(x => x.name.ToLowerInvariant() == parts[1]);
    if (r != null) _chatBets[user] = r;
  }

  void OnFinished(Racer r) {
    if (r.place != 1 || _settled) return;
    _settled = true;
    foreach (var x in race.Live) { PlayerPrefs.SetInt("PoRace.races." + x.name, PlayerPrefs.GetInt("PoRace.races." + x.name, 0) + 1); }
    PlayerPrefs.SetInt("PoRace.wins." + r.name, PlayerPrefs.GetInt("PoRace.wins." + r.name, 0) + 1);
    int stake = _stake.Values.Sum();
    if (_stake.TryGetValue(r, out int won)) {
      int pay = Mathf.RoundToInt(won * _odds[r]); Points += pay;
      BroadcastTicker.Post($"You backed {BroadcastUi.Paint(r.name, r.color)}: +{pay} pts  (balance {Points})", 8f);
    } else if (stake > 0) BroadcastTicker.Post($"{BroadcastUi.Paint(r.name, r.color)} wins. Your {stake} pts are gone (balance {Points})", 8f);
    if (_chatBets.Count > 0) BroadcastTicker.Post($"Chat: {_chatBets.Values.Count(x => x == r)} of {_chatBets.Count} picked {BroadcastUi.Paint(r.name, r.color)}", 8f);
    PlayerPrefs.Save();
    Debug.Log($"[Predictions] winner {r.name} odds {_odds[r]:F2} stake {stake} chat {_chatBets.Values.Count(x => x == r)}/{_chatBets.Count}");
  }

  void Update() {
    if (race.Phase == RacePhase.Racing) {
      // Live chance: estimated time to finish, with a penalty for being down; softmax over the gaps (1.5 s scale).
      var eta = race.Live.Where(r => r.finishTime < 0).ToDictionary(r => r,
          r => (race.TotalLength - r.distance) / Mathf.Max(0.3f, r.speed) + (r.state == RaceState.FALLEN_RECOVERING ? 4f : r.state == RaceState.STUMBLE ? 1f : 0f));
      if (eta.Count > 0) {
        float tau = Mathf.Max(1.5f, 0.06f * eta.Values.Min());
        foreach (var kv in Softmax(eta.ToDictionary(kv => kv.Key, kv => -kv.Value / tau))) _live[kv.Key] = kv.Value;
      }
    }
    if (_panel == null) return;
    bool open = race.Phase == RacePhase.Countdown;
    _panel.gameObject.SetActive(open);
    if (!open) return;
    var kb = Keyboard.current;
    if (kb != null) for (int i = 0; i < race.Live.Count && i < 4; i++) if (kb[Key.Digit1 + i].wasPressedThisFrame) Back(race.Live[i]);
    _title.text = $"PICK THE WINNER   {BroadcastUi.Paint(Points + " pts", BroadcastUi.Accent)}";
    foreach (var (r, t) in _rows) {
      _stake.TryGetValue(r, out int s); int crowd = _chatBets.Values.Count(x => x == r);
      t.text = $"{BroadcastUi.Paint(r.name, r.color)}   {_odds[r]:F1}x" + (s > 0 ? $"   staked {s}" : "") + (_chatBets.Count > 0 ? $"   chat {100 * crowd / _chatBets.Count}%" : "");
    }
  }

  void BuildPanel() {
    _panel = BroadcastUi.Panel(BroadcastUi.Root, "Predictions", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(620, 110 + 64 * race.Live.Count), BroadcastUi.Dark);
    _title = BroadcastUi.Label(_panel.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(580, 40), 26, TextAnchor.MiddleCenter);
    BroadcastUi.Label(_panel.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 10), new Vector2(580, 30), 18, TextAnchor.MiddleCenter, "tap to stake 100 (keys 1-4)   chat: !bet <name>");
    for (int i = 0; i < race.Live.Count; i++) {
      var r = race.Live[i];
      var b = BroadcastUi.Button(_panel.transform, "Back_" + r.name, "", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -64 - 64 * i), new Vector2(580, 56), new Color(r.color.r * 0.3f, r.color.g * 0.3f, r.color.b * 0.3f, 0.95f), 24, () => Back(r));
      _rows.Add((r, b.GetComponentInChildren<Text>()));
    }
  }
}

}
