using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>C2, standings as colour chips: one row of up to 5 chips at the top (portrait; a left column in landscape),
/// each = racer colour band + place + name + gap in seconds to the leader (finish time once home, DOWN while fallen) +
/// a tiny effort bar (mean actuator effort, RaceSensors; red while StrainMonitor redlines). Chips slide to their new
/// slot on an overtake. Tap a chip to back that racer (Backed: haptics follow it, hazards spare it). Replaces the
/// standings text. Text is only rebuilt when a value changes; no per-frame allocation.</summary>
public class StandingsChips : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public RaceSensors sensors;
  public StrainMonitor strain;

  /// <summary>The racer the viewer backs (tapped chip), or null.</summary>
  public static Racer Backed { get; private set; }
  public static event Action<Racer> BackedChanged;
  /// <summary>The chip row / column (coach marks point at it).</summary>
  public RectTransform Row { get; private set; }

  const float W = 104f, H = 72f, Space = 4f;
  static readonly string[] Places = { "1", "2", "3", "4", "5", "6", "7", "8" };
  const string Lead = "LEAD", Down = "DOWN", NoGap = "";

  class Chip {
    public Racer r; public int roster; public RectTransform rt; public Image band, bar, pick; public Text place, name, gap, glyph;
    public int shownPlace = -1; public string shownGap, finish; public float shownBar = -1f; public bool shownRed; public Vector2 target;
  }
  readonly List<Chip> _chips = new List<Chip>();
  readonly List<Chip> _order = new List<Chip>();
  bool _landscape;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (sensors == null) sensors = FindAnyObjectByType<RaceSensors>();
    if (strain == null) strain = FindAnyObjectByType<StrainMonitor>();
    if (BroadcastUi.Headless) { enabled = false; return; }
    if (race.standingsText != null) race.standingsText.enabled = false;   // the chips replace the text list
    Row = BroadcastUi.Rect(BroadcastUi.Root, "StandingsChips", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -84f), new Vector2(5 * W + 4 * Space, H));
    foreach (var r in race.Live) _chips.Add(Build(r, race.racers.IndexOf(r)));
    _order.AddRange(_chips);
    race.Restarted += () => { foreach (var c in _chips) { c.finish = null; c.shownGap = null; } };
    RacerPalette.Changed += Recolor; UiLayout.Changed += Relayout;
    Recolor(); Relayout(BroadcastUi.Landscape);
    foreach (var c in _chips) c.rt.anchoredPosition = c.target;
  }

  void OnDestroy() {
    RacerPalette.Changed -= Recolor; UiLayout.Changed -= Relayout;
    if (Backed != null) { Backed = null; BackedChanged?.Invoke(null); }   // racers do not outlive the scene
  }

  Chip Build(Racer r, int roster) {
    var c = new Chip { r = r, roster = roster };
    var bg = BroadcastUi.Panel(Row, "Chip_" + r.name, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(W, H), BroadcastUi.Dark);
    bg.raycastTarget = true; c.rt = bg.rectTransform;
    bg.gameObject.AddComponent<Button>().onClick.AddListener(() => Back(r));
    c.pick = BroadcastUi.Panel(c.rt, "Backed", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(BroadcastUi.Accent.r, BroadcastUi.Accent.g, BroadcastUi.Accent.b, 0.3f));
    BroadcastUi.Fill(c.pick.rectTransform); c.pick.enabled = false;
    c.band = BroadcastUi.Panel(c.rt, "Band", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(W, 5f), Color.white);
    c.place = BroadcastUi.Label(c.rt, "Place", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -6f), new Vector2(28f, 36f), 30, TextAnchor.UpperCenter);
    c.place.fontStyle = FontStyle.Bold;
    c.glyph = BroadcastUi.Label(c.rt, "Glyph", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -40f), new Vector2(28f, 26f), 22, TextAnchor.UpperCenter);
    c.name = BroadcastUi.Label(c.rt, "Name", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -6f), new Vector2(W - 34f, 28f), 22, TextAnchor.UpperLeft, r.name);
    c.name.horizontalOverflow = HorizontalWrapMode.Overflow;
    c.gap = BroadcastUi.Label(c.rt, "Gap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -34f), new Vector2(W - 34f, 28f), 22, TextAnchor.UpperLeft);
    c.gap.horizontalOverflow = HorizontalWrapMode.Overflow;
    c.bar = BroadcastUi.Panel(c.rt, "Effort", Vector2.zero, Vector2.zero, new Vector2(4f, 4f), new Vector2(W - 8f, 4f), Color.white);
    c.bar.rectTransform.localScale = new Vector3(0f, 1f, 1f);
    return c;
  }

  void Back(Racer r) {
    Backed = Backed == r ? null : r;
    foreach (var c in _chips) c.pick.enabled = c.r == Backed;
    if (Backed != null) BroadcastTicker.Post($"You back {BroadcastUi.Paint(r.name, r.color)}", 3f);
    BackedChanged?.Invoke(Backed);
  }

  void Recolor() {
    foreach (var c in _chips) {
      var col = RacerPalette.Get(c.roster, c.r.color);
      c.band.color = col; c.place.color = col; c.glyph.color = col; c.shownRed = !c.shownRed; c.shownBar = -1f;
      c.glyph.text = RacerPalette.Glyph(c.roster);
    }
  }

  void Relayout(bool landscape) {
    _landscape = landscape;
    Row.sizeDelta = landscape ? new Vector2(W, 5 * H + 4 * Space) : new Vector2(5 * W + 4 * Space, H);
    for (int i = 0; i < _order.Count; i++) _order[i].target = Slot(i);
  }

  Vector2 Slot(int i) => _landscape ? new Vector2(0f, -i * (H + Space)) : new Vector2(i * (W + Space), 0f);

  static float Key(Racer r) => r.finishTime >= 0 ? 1e6f - (float)r.finishTime : r.distance;   // as the orchestrator's standings

  void Update() {
    // order: insertion sort (stable, ties keep the previous order) into the reused list
    for (int i = 1; i < _order.Count; i++) {
      var c = _order[i]; float k = Key(c.r); int j = i;
      while (j > 0 && Key(_order[j - 1].r) < k) { _order[j] = _order[j - 1]; j--; }
      _order[j] = c;
    }
    Racer leader = null; foreach (var c in _order) if (c.r.finishTime < 0) { leader = c.r; break; }
    float ease = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
    for (int i = 0; i < _order.Count; i++) {
      var c = _order[i]; var r = c.r;
      c.target = Slot(i);
      if ((c.rt.anchoredPosition - c.target).sqrMagnitude > 0.25f) c.rt.anchoredPosition = Vector2.Lerp(c.rt.anchoredPosition, c.target, ease);
      if (c.shownPlace != i) { c.shownPlace = i; c.place.text = Places[Mathf.Min(i, Places.Length - 1)]; }
      string g;
      if (r.finishTime >= 0) g = c.finish ?? (c.finish = r.finishTime.ToString("F1", CultureInfo.InvariantCulture) + "s");
      else if (race.Phase == RacePhase.Countdown) g = NoGap;
      else if (r.state != RaceState.RACING) g = Down;
      else if (r == leader) g = Lead;
      else g = BroadcastUi.Gap((leader.distance - r.distance) / Mathf.Max(0.5f, r.speed));
      if (!ReferenceEquals(g, c.shownGap)) { c.shownGap = g; c.gap.text = g; c.gap.color = g == Down ? BroadcastUi.Live : Color.white; }
      float e = Effort(r); bool red = strain != null && strain.IsRedlining(r);
      if (Mathf.Abs(e - c.shownBar) > 0.02f) { c.shownBar = e; c.bar.rectTransform.localScale = new Vector3(e, 1f, 1f); }
      if (red != c.shownRed) { c.shownRed = red; c.bar.color = red ? BroadcastUi.Live : RacerPalette.Get(c.roster, r.color); }
    }
  }

  /// <summary>Mean actuator effort 0..1 (scaled so a hard sprint fills the bar).</summary>
  float Effort(Racer r) {
    var s = sensors != null ? sensors.Of(r) : null;
    if (s == null || s.effort.Length == 0) return 0f;
    float sum = 0f; for (int i = 0; i < s.effort.Length; i++) sum += s.effort[i];
    return Mathf.Clamp01(1.6f * sum / s.effort.Length);
  }
}

}
