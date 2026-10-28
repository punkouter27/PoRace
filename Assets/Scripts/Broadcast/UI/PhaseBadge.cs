using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>C10, one TC badge for the whole race instead of the race-text swaps: 3 / 2 / 1 (each digit pops in) ->
/// a green GO! -> the race clock (tenths) -> "Blue WINS" for 3 s on the first finish -> FINISH + winner. PhaseChanged
/// and Finished drive it; a small unscaled scale tween animates every change. Replaces MultiRaceOrchestrator.raceText.
/// Text is rebuilt only when what it shows changes (the clock 10 times a second).</summary>
public class PhaseBadge : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public Vector2 position = new Vector2(0f, -166f);
  /// <summary>The badge (coach marks / tests).</summary>
  public RectTransform Badge { get; private set; }

  static readonly Color Go = new Color(0.15f, 0.6f, 0.25f, 0.95f);
  static readonly string[] Digits = { "GO!", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
  Image _bg; Text _text;
  int _shownCount = -1, _shownTenths = -1; float _goUntil, _winUntil, _pop; Racer _winner; string _winText;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (BroadcastUi.Headless) { enabled = false; return; }
    if (race.raceText != null) race.raceText.enabled = false;   // the orchestrator keeps writing it; nobody sees it
    var top = new Vector2(0.5f, 1f);
    _bg = BroadcastUi.Panel(BroadcastUi.Root, "PhaseBadge", top, top, position, new Vector2(220f, 64f), BroadcastUi.Dark);
    Badge = _bg.rectTransform;
    _text = BroadcastUi.Label(Badge, "Text", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 64f), 40, TextAnchor.MiddleCenter);
    _text.fontStyle = FontStyle.Bold;
    race.PhaseChanged += OnPhase;
    race.Finished += r => { if (_winner == null) { _winner = r; _winText = BroadcastUi.Paint(r.name, r.color) + " WINS"; _winUntil = Time.unscaledTime + 3f; Show(_winText, BroadcastUi.Dark, 34); } };
    race.Restarted += () => { _winner = null; _shownCount = _shownTenths = -1; };
  }

  void OnPhase(RacePhase p) {
    if (p == RacePhase.Racing) { _goUntil = Time.unscaledTime + 1.2f; Show(Digits[0], Go, 44); }
    if (p == RacePhase.Finished) Show(_winner != null ? "FINISH\n" + _winText : "FINISH", BroadcastUi.Dark, _winner != null ? 24 : 34);
  }

  void Show(string s, Color bg, int size) { _text.text = s; _text.fontSize = size; _bg.color = bg; _pop = 1f; }

  void Update() {
    float now = Time.unscaledTime;
    if (race.Phase == RacePhase.Countdown) {
      int n = Mathf.Clamp(Mathf.CeilToInt(race.CountdownLeft), 1, Digits.Length - 1);
      if (n != _shownCount) { _shownCount = n; Show(Digits[n], BroadcastUi.Dark, 44); }
    } else if (race.Phase == RacePhase.Racing && now > _goUntil && now > _winUntil) {
      int t = Mathf.FloorToInt((float)race.RaceTime * 10f);
      if (t != _shownTenths) {
        if (_shownTenths < 0 || _bg.color != BroadcastUi.Dark) { _bg.color = BroadcastUi.Dark; _text.fontSize = 34; }
        _shownTenths = t; _text.text = (t / 10f).ToString("F1", CultureInfo.InvariantCulture) + " s";
      }
    }
    if (_pop > 0f) {   // pop: 1.35x scale easing back to 1
      _pop = Mathf.Max(0f, _pop - Time.unscaledDeltaTime * 4f);
      float s = 1f + 0.35f * _pop * _pop; Badge.localScale = new Vector3(s, s, 1f);
    }
  }
}

}
