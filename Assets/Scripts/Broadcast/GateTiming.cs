using System;
using System.Globalization;
using System.IO;
using Mujoco;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature A2, lap-delta gate timing. A timing gate every 10 m of race distance; each racer's crossing time
/// is interpolated between the two physics steps that straddle the gate (sub-step, from Racer.distance and race time).
/// The first racer through a gate is compared with the map's record run ("40 m  Blue  -0.42", green = ahead of the
/// record), the second with the first ("Red +0.31"). The winner's splits become the record when its finish beats
/// it: &lt;persistentDataPath&gt;/splits_&lt;map&gt;_L&lt;laps&gt;.json. Racers that start past 1 m (a head start) are
/// timed but never set the record. Read only; off in headless runs.</summary>
public class GateTiming : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public const float Spacing = 10f;
  public float showSeconds = 3.5f;

  [Serializable] class Best { public string racer; public float time; public float[] gates; }

  int _gates; float[][] _t; int[] _next; float[] _prevD, _prevT, _startD, _first; int[] _firstBy, _count; bool[] _started;
  Best _best; Image _box; Text _text;
  // Written on the physics step, shown in Update.
  int _leadGate = -1, _leadBy, _gapBy = -1; float _leadDelta, _gapDelta, _showUntil; bool _dirty, _finishLine;
  static readonly Color Ahead = new Color(0.3f, 0.95f, 0.4f), Behind = new Color(1f, 0.35f, 0.3f);

  string BestPath => Path.Combine(Application.persistentDataPath, $"splits_{race.map.name}_L{race.Laps}.json");

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (BroadcastUi.Headless) { enabled = false; return; }
    int n = race.Live.Count;
    _gates = Mathf.Max(0, Mathf.FloorToInt((race.TotalLength - 0.5f) / Spacing));
    _t = new float[n][]; for (int i = 0; i < n; i++) _t[i] = new float[_gates];
    _next = new int[n]; _prevD = new float[n]; _prevT = new float[n]; _startD = new float[n]; _started = new bool[n];
    _first = new float[_gates]; _firstBy = new int[_gates]; _count = new int[_gates];
    _best = Load();
    _box = BroadcastUi.Panel(BroadcastUi.Root, "GateDelta", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -455), new Vector2(400, 72), BroadcastUi.Dark);
    _text = BroadcastUi.Label(_box.transform, "Delta", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(380, 68), 24, TextAnchor.MiddleCenter);
    _box.gameObject.SetActive(false);
    Clear();
    race.Restarted += Clear;
    race.Finished += OnFinished;
    MjScene.Instance.postUpdateEvent += OnStep;
    if (_best != null) BroadcastTicker.Post($"Track record {_best.time:F2} s ({_best.racer})", 6f);
  }

  void OnDestroy() { if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= OnStep; }

  void Clear() {
    for (int i = 0; i < _t.Length; i++) { _next[i] = 0; _prevD[i] = 0f; _prevT[i] = 0f; _started[i] = false; for (int g = 0; g < _gates; g++) _t[i][g] = float.NaN; }
    for (int g = 0; g < _gates; g++) { _first[g] = float.NaN; _firstBy[g] = -1; _count[g] = 0; }
    _leadGate = -1; _gapBy = -1; _showUntil = 0f; _dirty = true;
  }

  /// <summary>After the race's own post-step (it subscribed in Awake), so distances are this step's.</summary>
  void OnStep(object sender, MjStepArgs a) {
    if (race.Phase != RacePhase.Racing) return;
    var live = race.Live; float now = (float)race.RaceTime;
    for (int i = 0; i < live.Count; i++) {
      float d = live[i].distance;
      if (!_started[i]) {   // gates behind the start (a head start) stay untimed
        _started[i] = true; _startD[i] = d; _prevD[i] = d; _prevT[i] = now;
        while (_next[i] < _gates && (_next[i] + 1) * Spacing <= d) _next[i]++;
        continue;
      }
      Advance(i, d, now);
    }
  }

  /// <summary>Times every gate between the previous sample and (d, now), linearly interpolated inside the step.</summary>
  void Advance(int i, float d, float now) {
    while (_next[i] < _gates && (_next[i] + 1) * Spacing <= d) {
      int g = _next[i]++; float gate = (g + 1) * Spacing;
      float u = d > _prevD[i] ? Mathf.Clamp01((gate - _prevD[i]) / (d - _prevD[i])) : 1f;
      Cross(i, g, _prevT[i] + u * (now - _prevT[i]));
    }
    _prevD[i] = d; _prevT[i] = now;
  }

  void Cross(int i, int g, float t) {
    _t[i][g] = t; int k = ++_count[g];
    if (k == 1) {
      _first[g] = t; _firstBy[g] = i;
      _leadGate = g; _leadBy = i; _gapBy = -1; _finishLine = false;
      _leadDelta = _best != null && _best.gates != null && g < _best.gates.Length && !float.IsNaN(_best.gates[g]) ? t - _best.gates[g] : float.NaN;
      _dirty = true;
    } else if (k == 2 && g == _leadGate) { _gapBy = i; _gapDelta = t - _first[g]; _dirty = true; }
  }

  void OnFinished(Racer r) {
    int i = IndexOf(r); if (i < 0) return;
    float t = (float)r.finishTime;
    if (_started[i]) Advance(i, r.distance, t);   // Finished fires inside the race's step, before OnStep: time this step's gates first
    if (r.place == 1) {
      _leadGate = _gates; _leadBy = i; _gapBy = -1; _finishLine = true;
      _leadDelta = _best != null ? t - _best.time : float.NaN; _dirty = true;
      if ((_best == null || t < _best.time) && _startD[i] < 1f && Array.TrueForAll(_t[i], x => !float.IsNaN(x))) {
        string was = _best != null ? $" (was {_best.time:F2} s, {_best.racer})" : "";
        _best = new Best { racer = r.name, time = t, gates = (float[])_t[i].Clone() }; Save();
        BroadcastTicker.Post($"{BroadcastUi.Paint("TRACK RECORD", BroadcastUi.Accent)} {BroadcastUi.Paint(r.name, r.color)} {t:F2} s{was}", 6f);
      }
    } else if (r.place == 2 && _finishLine) { _gapBy = i; _gapDelta = t - (float)race.Live[_leadBy].finishTime; _dirty = true; }
  }

  int IndexOf(Racer r) { var live = race.Live; for (int i = 0; i < live.Count; i++) if (live[i] == r) return i; return -1; }

  static string Signed(float x) => (x >= 0f ? "+" : "-") + Mathf.Abs(x).ToString("F2", CultureInfo.InvariantCulture);

  void Update() {
    if (_dirty) {
      _dirty = false;
      if (_leadGate >= 0) {   // rebuilt only when a gate is crossed, not per frame
        var live = race.Live; var lead = live[_leadBy];
        string where = _finishLine ? "FINISH" : $"{(_leadGate + 1) * Spacing:F0} m";
        string delta = float.IsNaN(_leadDelta) ? "" : "  " + BroadcastUi.Paint(Signed(_leadDelta), _leadDelta <= 0f ? Ahead : Behind) + "<size=16> rec</size>";
        string gap = _gapBy >= 0 ? $"\n<size=20>{BroadcastUi.Paint(live[_gapBy].name, live[_gapBy].color)} {Signed(_gapDelta)}</size>" : "";
        _text.text = $"<size=18>{where}</size>  {BroadcastUi.Paint(lead.name, lead.color)}{delta}{gap}";
        _showUntil = Time.unscaledTime + showSeconds;
      }
    }
    bool on = _leadGate >= 0 && Time.unscaledTime < _showUntil && race.Phase != RacePhase.Countdown;
    if (_box.gameObject.activeSelf != on) _box.gameObject.SetActive(on);
  }

  void Save() {
    try { File.WriteAllText(BestPath, JsonUtility.ToJson(_best)); }
    catch (Exception e) { Debug.LogWarning("[Splits] save failed: " + e.Message); }
  }

  Best Load() {
    try {
      if (!File.Exists(BestPath)) return null;
      var b = JsonUtility.FromJson<Best>(File.ReadAllText(BestPath));
      return b != null && b.time > 0f && b.gates != null && b.gates.Length == _gates ? b : null;   // a different track length: start over
    } catch (Exception e) { Debug.LogWarning("[Splits] load failed: " + e.Message); return null; }
  }
}

}
