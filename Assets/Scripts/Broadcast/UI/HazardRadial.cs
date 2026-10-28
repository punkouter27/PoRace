using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PoRace {

/// <summary>C1, one-thumb radial hazard picker (BR thumb zone, above the ticker). Press the hub, slide toward a wedge
/// (Drop / Cube / Shove fan out up and left), release to fire it; release on the hub or away cancels. Fires through
/// HazardDesk.TryFire as viewer "you", so the chat rules hold (racing only, 15 s per viewer, 2.5 s global). Each
/// wedge and the hub show the binding cooldown as a ring. Target: the leader, or the runner-up when the leader is
/// the racer you back. Replaces the BL Drop / Cube / Shove buttons (Restart stays).</summary>
public class HazardRadial : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public HazardDesk desk;
  public float radius = 150f, hubSize = 120f, wedgeSize = 108f, deadZone = 55f;
  /// <summary>The hub (coach marks point at it).</summary>
  public RectTransform Hub { get; private set; }

  static readonly string[] Labels = { "DROP", "CUBE", "SHOVE" };
  static readonly float[] Angles = { 90f, 135f, 180f };   // degrees, from +x (right), counter-clockwise
  Image _hub, _hubRing; Text _hubText; RectTransform _fan;
  readonly Image[] _wedge = new Image[3], _ring = new Image[3];
  int _sel = -1; bool _open; float _shownFrac = -1f; bool _shownReady;

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race)) || !BroadcastUi.Wired(this, desk, nameof(desk))) return;
    if (BroadcastUi.Headless) { enabled = false; return; }
    var hud = FindAnyObjectByType<Hud>();
    if (hud != null) foreach (var n in new[] { "BtnDrop", "BtnCube", "BtnShove" }) { var b = hud.transform.Find(n); if (b != null) b.gameObject.SetActive(false); }

    var root = BroadcastUi.Rect(BroadcastUi.Root, "HazardRadial", new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-112f, 352f), new Vector2(hubSize, hubSize));
    _fan = BroadcastUi.Rect(root, "Fan", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    for (int i = 0; i < 3; i++) {
      float a = Angles[i] * Mathf.Deg2Rad; var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
      _wedge[i] = BroadcastUi.Round(_fan, "Wedge_" + Labels[i], new Vector2(0.5f, 0.5f), p, wedgeSize, BroadcastUi.Dark);
      _ring[i] = BroadcastUi.Round(_wedge[i].transform, "Cooldown", new Vector2(0.5f, 0.5f), Vector2.zero, wedgeSize, BroadcastUi.Accent, true, true);
      BroadcastUi.Label(_wedge[i].transform, "Label", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(wedgeSize, 30f), 22, TextAnchor.MiddleCenter, Labels[i]);
    }
    _fan.gameObject.SetActive(false);
    _hub = BroadcastUi.Round(root, "Hub", new Vector2(0.5f, 0.5f), Vector2.zero, hubSize, BroadcastUi.Dark); _hub.raycastTarget = true;
    _hubRing = BroadcastUi.Round(_hub.transform, "Cooldown", new Vector2(0.5f, 0.5f), Vector2.zero, hubSize, BroadcastUi.Accent, true, true);
    _hubText = BroadcastUi.Label(_hub.transform, "Label", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(hubSize, 60f), 22, TextAnchor.MiddleCenter, "HAZARD");
    Hub = _hub.rectTransform;
    var ptr = _hub.gameObject.AddComponent<UiPointer>();
    ptr.Down = e => { _open = true; _sel = -1; _fan.gameObject.SetActive(true); Track(e); };
    ptr.Drag = Track;
    ptr.Up = e => { Track(e); int k = _sel; Close(); if (k >= 0 && Time.timeScale > 0f) desk.TryFire(HazardDesk.Local, HazardDesk.Kinds[k], Target()); };
  }

  /// <summary>Wedge under the finger: beyond the dead zone, within 25 degrees of a wedge's direction.</summary>
  void Track(PointerEventData e) {
    if (!_open) return;
    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Hub, e.position, e.pressEventCamera, out var local)) return;
    int sel = -1;
    if (local.magnitude > deadZone) {
      float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg, best = 25f;
      for (int i = 0; i < 3; i++) { float d = Mathf.Abs(Mathf.DeltaAngle(ang, Angles[i])); if (d < best) { best = d; sel = i; } }
    }
    if (sel == _sel) return;
    _sel = sel;
    for (int i = 0; i < 3; i++) {
      _wedge[i].color = i == sel ? new Color(0.25f, 0.22f, 0.08f, 0.95f) : BroadcastUi.Dark;
      _wedge[i].rectTransform.localScale = Vector3.one * (i == sel ? 1.15f : 1f);
    }
  }

  void Close() {
    _open = false; _sel = -1; _fan.gameObject.SetActive(false);
    for (int i = 0; i < 3; i++) { _wedge[i].color = BroadcastUi.Dark; _wedge[i].rectTransform.localScale = Vector3.one; }
  }

  /// <summary>The leader, or the runner-up when the leader is the backed racer (your pick is spared).</summary>
  Racer Target() {
    Racer first = null, second = null;
    foreach (var r in race.Live) {
      if (r.finishTime >= 0) continue;
      if (first == null || r.distance > first.distance) { second = first; first = r; }
      else if (second == null || r.distance > second.distance) second = r;
    }
    return first != null && first == StandingsChips.Backed && second != null ? second : first;
  }

  void Update() {
    float left = desk.CooldownLeft(HazardDesk.Local, out float frac);
    bool ready = race.Phase == RacePhase.Racing && left <= 0f && Time.timeScale > 0f;   // not during a replay or the pause sheet
    if (Mathf.Abs(frac - _shownFrac) > 0.005f) {
      _shownFrac = frac; _hubRing.fillAmount = frac;
      for (int i = 0; i < 3; i++) _ring[i].fillAmount = frac;
    }
    if (ready != _shownReady) {
      _shownReady = ready;
      _hub.color = ready ? new Color(0.35f, 0.08f, 0.08f, 0.9f) : BroadcastUi.Dark;
      _hubText.color = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);
    }
  }
}

}
