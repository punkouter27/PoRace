using System;
using System.Collections.Generic;
using System.IO;
using Mujoco;
using UnityEngine;
using UnityEngine.UI;

namespace PoRace {

/// <summary>Feature A3, the recovery race. While a racer is down (FALLEN_RECOVERING until it is RACING again) a ring
/// over it drains toward its AutoReset deadline (fallenSeconds: the getup policy's window before the forced reset to
/// the last checkpoint), with the seconds down and the nearest chaser within 10 m ("can he get up before Green
/// passes?"). The getup time is measured in race time on the physics step; the fastest real getup (no reset) per map
/// is kept in &lt;persistentDataPath&gt;/getup_records.json and announced on the ticker. Read only on mjData (racer
/// state only); off in headless runs, so tests write no records.</summary>
public class RecoveryRace : MonoBehaviour {
  public MultiRaceOrchestrator race;
  public float ringSize = 72f;

  class Down { public bool on; public float since; public int resets; public Image ring; public Text secs, chaser; public int shownTenths = -1, shownChaser = -1; }
  [Serializable] class Rec { public string map, racer; public float time; }
  [Serializable] class RecFile { public List<Rec> records = new List<Rec>(); }

  Down[] _d; string[] _tenths; string[][] _chaserText;   // cached strings: no allocation while a ring is up
  RecFile _recs; Camera _cam;
  string RecPath => Path.Combine(Application.persistentDataPath, "getup_records.json");

  void Start() {
    if (!BroadcastUi.Wired(this, race, nameof(race))) return;
    if (BroadcastUi.Headless) { enabled = false; return; }
    _cam = race.ViewCamera;
    var live = race.Live; int n = live.Count;
    _d = new Down[n]; _tenths = new string[100]; _chaserText = new string[n][];
    for (int i = 0; i < 100; i++) _tenths[i] = (i / 10f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
    var sprite = RingSprite();
    for (int i = 0; i < n; i++) {
      var r = live[i];
      var ring = BroadcastUi.Panel(BroadcastUi.Root, "GetupRing_" + r.name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ringSize, ringSize), Color.white);
      ring.sprite = sprite; ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360; ring.fillOrigin = (int)Image.Origin360.Top; ring.fillClockwise = false;
      var secs = BroadcastUi.Label(ring.transform, "Secs", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ringSize, 30), 20, TextAnchor.MiddleCenter);
      var chaser = BroadcastUi.Label(ring.transform, "Chaser", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(200, 26), 17, TextAnchor.UpperCenter);
      ring.gameObject.SetActive(false);
      _d[i] = new Down { ring = ring, secs = secs, chaser = chaser };
      _chaserText[i] = new string[11];
      for (int m = 0; m <= 10; m++) _chaserText[i][m] = $"{BroadcastUi.Paint(r.name, r.color)} {m} m back";
    }
    _recs = Load(); if (_recs.records == null) _recs.records = new List<Rec>();
    MjScene.Instance.postUpdateEvent += OnStep;
    race.Restarted += () => { foreach (var d in _d) { d.on = false; d.ring.gameObject.SetActive(false); } };
  }

  void OnDestroy() { if (MjScene.InstanceExists) MjScene.Instance.postUpdateEvent -= OnStep; }

  /// <summary>After the race's own post-step (it subscribed in Awake), so r.state is this step's.</summary>
  void OnStep(object sender, MjStepArgs a) {
    if (race.Phase != RacePhase.Racing) return;
    var live = race.Live; float now = (float)race.RaceTime;
    for (int i = 0; i < live.Count; i++) {
      var r = live[i]; var d = _d[i];
      if (!d.on && r.state == RaceState.FALLEN_RECOVERING && r.finishTime < 0) { d.on = true; d.since = now; d.resets = r.autoReset != null ? r.autoReset.Resets : 0; continue; }
      if (d.on && (r.state == RaceState.RACING || r.finishTime >= 0)) { d.on = false; if (r.finishTime < 0) Up(r, now - d.since, r.autoReset != null && r.autoReset.Resets > d.resets); }
    }
  }

  void Up(Racer r, float t, bool reset) {
    var who = BroadcastUi.Paint(r.name, r.color);
    if (reset) { BroadcastTicker.Post($"{who} could not get up: reset after {t:F1} s"); return; }
    var rec = _recs.records.Find(x => x.map == race.map.name);
    if (rec == null || t < rec.time) {
      if (rec == null) _recs.records.Add(rec = new Rec { map = race.map.name });
      rec.racer = r.name; rec.time = t; Save();
      BroadcastTicker.Post($"{BroadcastUi.Paint("GETUP RECORD", BroadcastUi.Accent)} {who} up in {t:F2} s on {race.map.displayName}", 6f);
    } else BroadcastTicker.Post($"{who} up in {t:F2} s  (record {rec.time:F2} s, {rec.racer})");
  }

  void LateUpdate() {
    var live = race.Live; var canvas = BroadcastUi.Root as RectTransform; float now = (float)race.RaceTime;
    for (int i = 0; i < live.Count; i++) {
      var r = live[i]; var d = _d[i]; var b = r.Body.BaseTransform;
      bool show = d.on && race.Phase == RacePhase.Racing && b != null && _cam != null;
      Vector3 sp = show ? _cam.WorldToScreenPoint(b.position + Vector3.up * 0.6f) : Vector3.zero;
      show &= sp.z > 0f;
      if (d.ring.gameObject.activeSelf != show) d.ring.gameObject.SetActive(show);
      if (!show) continue;
      RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, sp, null, out var lp);
      d.ring.rectTransform.anchoredPosition = lp;
      float window = r.autoReset != null ? r.autoReset.fallenSeconds : 5f, down = Mathf.Max(0f, now - d.since), u = Mathf.Clamp01(down / window);
      d.ring.fillAmount = 1f - u;
      d.ring.color = Color.Lerp(new Color(0.3f, 0.9f, 0.4f, 0.9f), new Color(1f, 0.15f, 0.1f, 0.95f), u);
      int tenths = Mathf.Min(99, (int)(down * 10f));
      if (tenths != d.shownTenths) { d.shownTenths = tenths; d.secs.text = _tenths[tenths]; }
      // Nearest racer behind still running, within 10 m.
      int ci = -1; float gap = 10.5f;
      for (int j = 0; j < live.Count; j++) {
        var o = live[j]; float g = r.distance - o.distance;
        if (o != r && o.finishTime < 0 && g >= 0f && g < gap) { gap = g; ci = j; }
      }
      int key = ci < 0 ? -1 : ci * 16 + Mathf.Min(10, Mathf.RoundToInt(gap));
      if (key != d.shownChaser) { d.shownChaser = key; d.chaser.text = ci < 0 ? "" : _chaserText[ci][key % 16]; }
    }
  }

  /// <summary>A soft white ring (alpha only) for the radial fill.</summary>
  static Sprite RingSprite() {
    const int N = 96; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
    var px = new Color32[N * N]; float c = (N - 1) * 0.5f;
    for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) {
      float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
      float a = Mathf.Clamp01((1f - r) * c * 0.5f) * Mathf.Clamp01((r - 0.74f) * c * 0.5f);
      px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
    }
    tex.SetPixels32(px); tex.Apply(false, true);
    return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
  }

  void Save() {
    try { File.WriteAllText(RecPath, JsonUtility.ToJson(_recs, true)); }
    catch (Exception e) { Debug.LogWarning("[Getup] save failed: " + e.Message); }
  }

  RecFile Load() {
    try { if (File.Exists(RecPath)) return JsonUtility.FromJson<RecFile>(File.ReadAllText(RecPath)) ?? new RecFile(); }
    catch (Exception e) { Debug.LogWarning("[Getup] load failed: " + e.Message); }
    return new RecFile();
  }
}

}
